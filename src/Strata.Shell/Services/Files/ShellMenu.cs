using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Strata.Shell.Services.Files;

/// <summary>
/// "Mostrar más opciones": el menú contextual clásico de Windows (con las extensiones de otras apps:
/// 7-Zip, Abrir con Code, Enviar a, Propiedades…) en un proceso aparte
/// (<c>StrataUI.exe --shell-menu</c>), como los diálogos de <see cref="FilePicker"/>. Las extensiones
/// del Shell se cargan dentro de quien muestra el menú; si una falla, solo se cae ese proceso, no las
/// barras. Sigue vivo mientras tenga ventanas abiertas (Propiedades) y se cierra solo.
/// </summary>
public static class ShellMenu
{
    public const string Arg = "--shell-menu";

    /// <summary>Muestra el menú de <paramref name="paths"/> (o el del fondo de la carpeta) en el punto de la pantalla, en píxeles.</summary>
    public static void Show(IReadOnlyList<string> paths, bool background, int x, int y)
    {
        try
        {
            // El clic fue en Strata UI: el proceso del menú puede pasar al frente (si no, el menú no se cierra bien).
            AllowSetForegroundWindow(-1);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var arg in new[] { Arg, x.ToString(), y.ToString(), background ? "background" : "items" }.Concat(paths))
                start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo abrir el menú de Windows", ex);
        }
    }

    /// <summary>Cursor en píxeles de la pantalla (donde se abrió el menú de Archivos).</summary>
    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    // ── Proceso aparte ──────────────────────────────────────────────────────

    /// <summary>Muestra el menú y ejecuta lo elegido. Solo acepta rutas completas que existen.</summary>
    internal static int Run(string[] args)
    {
        if (args.Length < 5 || !int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y)
            || args[3] is not ("items" or "background"))
            return 2;
        var paths = args.Skip(4).ToList();
        if (paths.Any(p => !Path.IsPathFullyQualified(p) || (!File.Exists(p) && !Directory.Exists(p))))
            return 2;
        bool background = args[3] == "background";
        if (background && paths.Count != 1)
            return 2;

        var owner = new OwnerWindow();
        var pidls = new List<IntPtr>();
        IContextMenu? menu = null;
        IntPtr hmenu = IntPtr.Zero;
        try
        {
            menu = background ? BackgroundMenu(paths[0], owner.Handle, pidls) : ItemsMenu(paths, owner.Handle, pidls);
            if (menu is null)
                return 1;
            owner.Menu = menu;
            hmenu = CreatePopupMenu();
            uint flags = CmfNormal | CmfExplore | ((GetKeyState(VkShift) & 0x8000) != 0 ? CmfExtendedVerbs : 0);
            if (menu.QueryContextMenu(hmenu, 0, FirstCommand, 0x7FFF, flags) < 0)
                return 1;
            SetForegroundWindow(owner.Handle);
            int command = TrackPopupMenuEx(hmenu, TpmReturnCmd | TpmRightButton, x, y, owner.Handle, IntPtr.Zero);
            PostMessage(owner.Handle, 0, IntPtr.Zero, IntPtr.Zero); // cierra bien el menú (documentado en TrackPopupMenu)
            if (command <= 0)
                return 0;
            var invoke = new CmInvokeCommandInfoEx
            {
                cbSize = Marshal.SizeOf<CmInvokeCommandInfoEx>(),
                fMask = CmicMaskUnicode | CmicMaskPtInvoke,
                hwnd = owner.Handle,
                lpVerb = (IntPtr)(command - FirstCommand),
                lpVerbW = (IntPtr)(command - FirstCommand),
                nShow = SwShowNormal,
                ptInvoke = new Point { X = x, Y = y },
            };
            menu.InvokeCommand(ref invoke);
            WaitForWindows();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Menú de Windows: falló", ex);
            return 1;
        }
        finally
        {
            if (hmenu != IntPtr.Zero)
                DestroyMenu(hmenu);
            if (menu != null)
                Marshal.ReleaseComObject(menu);
            pidls.ForEach(Marshal.FreeCoTaskMem);
            owner.DestroyHandle();
        }
    }

    /// <summary>Menú de los elementos elegidos (todos de la misma carpeta).</summary>
    private static IContextMenu? ItemsMenu(List<string> paths, IntPtr owner, List<IntPtr> pidls)
    {
        IShellFolder? parent = null;
        var children = new List<IntPtr>();
        foreach (var path in paths)
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) < 0)
                return null;
            pidls.Add(pidl);
            var iid = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidl, ref iid, out var folder, out var child) < 0)
                return null;
            parent ??= (IShellFolder)folder;
            children.Add(child);
        }
        var menuIid = typeof(IContextMenu).GUID;
        return parent!.GetUIObjectOf(owner, (uint)children.Count, children.ToArray(), ref menuIid, IntPtr.Zero, out var menu) < 0
            ? null
            : menu as IContextMenu;
    }

    /// <summary>Menú del espacio libre de una carpeta (Nuevo, Pegar, Propiedades de la carpeta…).</summary>
    private static IContextMenu? BackgroundMenu(string folder, IntPtr owner, List<IntPtr> pidls)
    {
        if (SHParseDisplayName(folder, IntPtr.Zero, out var pidl, 0, out _) < 0)
            return null;
        pidls.Add(pidl);
        var iid = typeof(IShellFolder).GUID;
        if (SHBindToObject(IntPtr.Zero, pidl, IntPtr.Zero, ref iid, out var bound) < 0 || bound is not IShellFolder shellFolder)
            return null;
        var menuIid = typeof(IContextMenu).GUID;
        return shellFolder.CreateViewObject(owner, ref menuIid, out var menu) < 0 ? null : menu as IContextMenu;
    }

    /// <summary>
    /// Lo elegido puede abrir ventanas de este proceso (Propiedades, Abrir con…): se espera a que se
    /// cierren, atendiendo sus mensajes, y luego termina.
    /// </summary>
    private static void WaitForWindows()
    {
        var started = Stopwatch.StartNew();
        var lastWindow = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromHours(2))
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(50);
            if (HasVisibleWindow())
                lastWindow.Restart();
            // Un momento de gracia: la ventana de Propiedades tarda en aparecer.
            else if (lastWindow.Elapsed > TimeSpan.FromSeconds(3))
                return;
        }
    }

    private static bool HasVisibleWindow()
    {
        uint self = (uint)Environment.ProcessId;
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self && IsWindowVisible(hwnd))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Ventana oculta dueña del menú: le pasa al menú los mensajes de sus submenús (Enviar a, Abrir con).</summary>
    private sealed class OwnerWindow : System.Windows.Forms.NativeWindow
    {
        public IContextMenu? Menu { get; set; }

        public OwnerWindow() => CreateHandle(new System.Windows.Forms.CreateParams());

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)
            {
                if (Menu is IContextMenu3 menu3 && menu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result) >= 0)
                {
                    m.Result = result;
                    return;
                }
                if (Menu is IContextMenu2 menu2 && menu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) >= 0)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    private const int FirstCommand = 1;
    private const uint CmfNormal = 0, CmfExplore = 0x4, CmfExtendedVerbs = 0x100;
    private const uint TpmReturnCmd = 0x100, TpmRightButton = 0x2;
    private const int CmicMaskUnicode = 0x4000, CmicMaskPtInvoke = 0x20000000, SwShowNormal = 1;
    private const int WmInitMenuPopup = 0x117, WmDrawItem = 0x2B, WmMeasureItem = 0x2C, WmMenuChar = 0x120, VkShift = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmInvokeCommandInfoEx
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        public string? lpParametersW;
        public string? lpDirectoryW;
        public string? lpTitleW;
        public Point ptInvoke;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int CompareIDs(IntPtr param, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr owner, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid iid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder, out IntPtr child);
    [DllImport("shell32.dll")]
    private static extern int SHBindToObject(IntPtr parent, IntPtr pidl, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
