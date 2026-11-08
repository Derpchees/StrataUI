using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Cuadros Abrir, Guardar como y Elegir carpeta de cualquier app en Archivos. Sin inyectar nada en la
/// otra app: un gancho ve aparecer el cuadro de Windows, lo vuelve invisible (y que no reciba clics),
/// abre Archivos en modo cuadro y, al elegir, escribe la ruta en el cuadro oculto y pulsa su Aceptar.
/// La app recibe lo mismo que si el usuario hubiera usado el de Windows. Si algo falla (la app es de
/// administrador, el cuadro no acepta la ruta…), el cuadro de Windows vuelve a verse tal cual.
/// </summary>
internal static class DialogTakeover
{
    private const uint EventObjectShow = 0x8002;
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x2;
    private const uint WmSetText = 0x000C;
    private const uint WmGetText = 0x000D;
    private const uint WmGetTextLength = 0x000E;
    private const uint WmCommand = 0x0111;
    private const uint CbGetCount = 0x0146;
    private const uint CbGetCurSel = 0x0147;
    private const uint CbGetLbText = 0x0148;
    private const uint CbGetLbTextLen = 0x0149;
    private const uint CbSetCurSel = 0x014E;
    private const int CbnSelChange = 1;
    private const int IdOk = 1;
    private const int IdCancel = 2;

    private static IntPtr _hook;
    private static WinEventProc? _proc;
    private static readonly HashSet<IntPtr> Handling = [];
    private static readonly int OwnPid = Environment.ProcessId;

    /// <summary>Encendido: Archivos instalado y elegido como explorador, y la opción de los cuadros activa.</summary>
    public static bool Active => OptionalModules.Files.ReplacesExplorer && Settings.Current.FilesReplaceDialogs;

    public static void Update()
    {
        if (Active)
        {
            if (_hook == IntPtr.Zero)
            {
                _proc = OnEvent;
                _hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _proc, 0, 0, 0);
                Log.Info("Archivos reemplaza los cuadros Abrir y Guardar de Windows");
            }
        }
        else
            Stop();
    }

    public static void Stop()
    {
        if (_hook == IntPtr.Zero)
            return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }

    // ── Detección ──────────────────────────────────────────────────────────

    private enum Kind { None, Open, Save, Folder }

    private static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero || Handling.Contains(hwnd) || ClassOf(hwnd) != "#32770")
            return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == OwnPid)
            return;
        Handling.Add(hwnd);
        if (!TryTake(hwnd))
        {
            // Al aparecer, el cuadro a veces aún no tiene sus controles: se revisa unas veces más.
            int tries = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                if (TryTake(hwnd))
                    timer.Stop();
                else if (++tries > 50 || !IsWindow(hwnd))
                {
                    timer.Stop();
                    Handling.Remove(hwnd);
                }
            };
            timer.Start();
        }
    }

    /// <summary>Si ya es un cuadro de archivos reconocible, lo oculta y lo atiende. false para volver a intentar.</summary>
    private static bool TryTake(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || FindWindowEx(hwnd, IntPtr.Zero, "DUIViewWndClassName", null) == IntPtr.Zero)
            return false;
        var kind = KindOf(hwnd);
        if (kind == Kind.None)
            return false;
        if (!Hide(hwnd, out bool wasLayered))
        {
            Log.Info("Cuadro de archivos sin reemplazar: Windows no deja ocultarlo");
            return true; // no se vuelve a intentar
        }
        var worker = new Thread(() => Inspect(hwnd, kind, wasLayered)) { IsBackground = true, Name = "Strata UI: cuadro de archivos" };
        worker.Start();
        return true;
    }

    private static Kind KindOf(IntPtr hwnd)
    {
        if (GetDlgItem(hwnd, 1152) is var folder && folder != IntPtr.Zero && ClassOf(folder) == "Edit")
            return Kind.Folder;
        if (GetDlgItem(hwnd, 1148) is var open && open != IntPtr.Zero && ClassOf(open) is "ComboBoxEx32" or "Edit")
            return Kind.Open;
        return SaveNameBox(hwnd) != IntPtr.Zero ? Kind.Save : Kind.None;
    }

    /// <summary>El cuadro del nombre en Guardar como: un Edit con id 1001 dentro de un ComboBox.</summary>
    private static IntPtr SaveNameBox(IntPtr hwnd) =>
        Descendants(hwnd).FirstOrDefault(c => GetDlgCtrlID(c) == 1001 && ClassOf(c) == "Edit" && ClassOf(GetParent(c)) == "ComboBox");

    /// <summary>Donde se escribe la ruta en cada tipo de cuadro.</summary>
    private static IntPtr NameBox(IntPtr hwnd, Kind kind) => kind switch
    {
        Kind.Folder => GetDlgItem(hwnd, 1152),
        Kind.Open when GetDlgItem(hwnd, 1148) is var combo && ClassOf(combo) == "Edit" => combo,
        Kind.Open => Descendants(GetDlgItem(hwnd, 1148)).FirstOrDefault(c => ClassOf(c) == "Edit"),
        Kind.Save => SaveNameBox(hwnd),
        _ => IntPtr.Zero,
    };

    /// <summary>La lista de tipos: el ComboBox sin cuadro de texto (el de la ruta y el del nombre lo tienen).</summary>
    private static IntPtr TypeBox(IntPtr hwnd)
    {
        if (GetDlgItem(hwnd, 1136) is var open && open != IntPtr.Zero && ClassOf(open) == "ComboBox")
            return open;
        return Descendants(hwnd).FirstOrDefault(c => ClassOf(c) == "ComboBox" && FindWindowEx(c, IntPtr.Zero, "Edit", null) == IntPtr.Zero
                                                    && ClassOf(GetParent(c)) != "ComboBoxEx32");
    }

    // ── Ocultar y volver a mostrar ─────────────────────────────────────────

    /// <summary>Invisible y sin recibir clics. false si Windows no lo deja (la app es de administrador).</summary>
    private static bool Hide(IntPtr hwnd, out bool wasLayered)
    {
        int style = GetWindowLong(hwnd, GwlExStyle);
        wasLayered = (style & WsExLayered) != 0;
        if (wasLayered)
            return false; // ya usa transparencia propia: no la pisamos
        SetWindowLong(hwnd, GwlExStyle, style | WsExLayered | WsExTransparent);
        if ((GetWindowLong(hwnd, GwlExStyle) & WsExLayered) == 0)
            return false;
        SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
        return true;
    }

    private static void Unhide(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
            return;
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) & ~(WsExLayered | WsExTransparent));
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400); // INVALIDATE|ERASE|ALLCHILDREN|FRAME
        SetForegroundWindow(hwnd);
    }

    // ── Leer el cuadro, preguntar en Archivos y responder ─────────────────

    private static void Inspect(IntPtr hwnd, Kind kind, bool wasLayered, int attempt = 0)
    {
        FilePickRequest? request = null;
        try
        {
            request = Read(hwnd, kind);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer el cuadro de archivos", ex);
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (request is null || dispatcher is null)
        {
            Done(hwnd, show: true);
            return;
        }
        dispatcher.BeginInvoke(async () =>
        {
            if (!Active || OptionalModules.Files.Load() is not { } files || !IsWindow(hwnd))
            {
                Done(hwnd, show: true);
                return;
            }
            Log.Info($"Cuadro {kind} de {ProcessName(hwnd)} abierto en Archivos (carpeta {request.Folder ?? "?"}, {request.Filters.Count} tipos)");
            FilePickResult? result;
            try
            {
                // La app del cuadro tiene el foco: se toma prestada su cola de entrada para poder dárselo a Archivos.
                uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                uint me = GetCurrentThreadId();
                bool attached = fg != 0 && fg != me && AttachThreadInput(me, fg, true);
                Task<FilePickResult?> pick;
                try { pick = files.PickAsync(request); }
                finally
                {
                    if (attached)
                        AttachThreadInput(me, fg, false);
                }
                // Si la app cierra su cuadro por su cuenta (o se cierra la app), Archivos también.
                result = await Task.WhenAny(pick, WaitClosedAsync(hwnd)) == pick ? await pick : null;
                if (!pick.IsCompleted)
                    CloseOwnPicker(request);
            }
            catch (Exception ex)
            {
                Log.Error("Falló Archivos como cuadro de archivos", ex);
                Done(hwnd, show: true);
                return;
            }
            var answer = new Thread(() => Answer(hwnd, kind, wasLayered, attempt, request, result)) { IsBackground = true, Name = "Strata UI: respuesta del cuadro" };
            answer.Start();
        });
    }

    private static void Done(IntPtr hwnd, bool show)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => Handling.Remove(hwnd));
        if (show)
            Unhide(hwnd);
    }

    private static async Task<FilePickResult?> WaitClosedAsync(IntPtr hwnd)
    {
        while (IsWindow(hwnd) && IsWindowVisible(hwnd))
            await Task.Delay(250);
        return null;
    }

    /// <summary>Cierra la ventana de Archivos que quedó abierta como cuadro (la app cerró el suyo).</summary>
    private static void CloseOwnPicker(FilePickRequest request)
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (window.GetType().Name == "FilesWindow" && new System.Windows.Interop.WindowInteropHelper(window).Owner == request.Owner
                && window.Title == (request.Title ?? window.Title))
            {
                window.Close();
                return;
            }
        }
    }

    private static FilePickRequest? Read(IntPtr hwnd, Kind kind)
    {
        string title = TextOf(hwnd);
        IntPtr owner = GetWindow(hwnd, 4); // GW_OWNER
        string? name = NameBox(hwnd, kind) is var box && box != IntPtr.Zero ? TextOf(box) : null;
        var filters = new List<FilePickFilter>();
        int index = 0;
        if (kind != Kind.Folder && TypeBox(hwnd) is var types && types != IntPtr.Zero)
        {
            int count = (int)SendMessage(types, CbGetCount, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < count && i < 64; i++)
            {
                int length = (int)SendMessage(types, CbGetLbTextLen, i, IntPtr.Zero);
                if (length <= 0 || length > 1024)
                    continue;
                var text = new StringBuilder(length + 1);
                SendMessage(types, CbGetLbText, i, text);
                filters.Add(ParseFilter(text.ToString()));
            }
            index = Math.Max(0, (int)SendMessage(types, CbGetCurSel, IntPtr.Zero, IntPtr.Zero));
        }
        return new FilePickRequest(
            kind switch { Kind.Save => FilePickMode.Save, Kind.Folder => FilePickMode.Folder, _ => FilePickMode.Open },
            kind != Kind.Save && CanSelectMultiple(hwnd),
            CurrentFolder(hwnd), string.IsNullOrWhiteSpace(name) ? null : name, filters, index,
            string.IsNullOrWhiteSpace(title) ? null : title, owner);
    }

    /// <summary>«Imágenes (*.png;*.jpg)» → los patrones entre paréntesis; sin ellos, todo.</summary>
    internal static FilePickFilter ParseFilter(string text)
    {
        int close = text.LastIndexOf(')');
        int open = close > 0 ? text.LastIndexOf('(', close) : -1;
        if (open >= 0 && text[(open + 1)..close] is var inner && inner.Contains('*'))
        {
            var patterns = inner.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)).SelectMany(p => p).ToList();
            return new FilePickFilter(text.Trim(), patterns);
        }
        return new FilePickFilter(text.Trim(), ["*"]);
    }

    /// <summary>Si la lista deja elegir varios archivos (la app pidió selección múltiple).</summary>
    private static bool CanSelectMultiple(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var list = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "UIItemsView"));
            return list?.TryGetCurrentPattern(SelectionPattern.Pattern, out object pattern) == true
                && ((SelectionPattern)pattern).Current.CanSelectMultiple;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// La carpeta que muestra el cuadro, leída de su barra de dirección («Dirección: Documentos»). Las
    /// carpetas conocidas aparecen con su nombre, no con la ruta.
    /// </summary>
    private static string? CurrentFolder(IntPtr hwnd)
    {
        foreach (var bar in Descendants(hwnd).Where(c => ClassOf(c) == "ToolbarWindow32" && ClassOf(GetParent(c)) == "Breadcrumb Parent"))
        {
            string text = TextOf(bar);
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0)
                continue;
            string shown = text[(colon + 2)..].Trim();
            if (Path.IsPathFullyQualified(shown) && Directory.Exists(shown))
                return shown;
            foreach (var known in KnownFolders())
            {
                if (string.Equals(DisplayName(known), shown, StringComparison.CurrentCultureIgnoreCase))
                    return known;
            }
        }
        return null;
    }

    private static IEnumerable<string> KnownFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Path.Combine(profile, "Downloads");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        yield return profile;
    }

    private static string DisplayName(string path)
    {
        var info = new ShFileInfo();
        return SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), 0x200) != IntPtr.Zero ? info.DisplayName : Path.GetFileName(path);
    }

    /// <summary>Escribe lo elegido en el cuadro oculto y lo acepta; o lo cancela. Si no se cierra, vuelve a verse.</summary>
    private static void Answer(IntPtr hwnd, Kind kind, bool wasLayered, int attempt, FilePickRequest request, FilePickResult? result)
    {
        bool ok = false, retry = false;
        try
        {
            if (!IsWindow(hwnd))
            {
                ok = true;
                return;
            }
            if (result is null)
            {
                PostMessage(hwnd, WmCommand, IdCancel, IntPtr.Zero);
                ok = WaitGone(hwnd, 1500);
                return;
            }
            IntPtr box = NameBox(hwnd, kind);
            if (box == IntPtr.Zero)
                return;
            if (kind != Kind.Folder && result.FilterIndex != request.FilterIndex)
                SelectType(hwnd, result.FilterIndex);
            var paths = result.Paths;
            if (paths.Count > 1 && Path.GetDirectoryName(paths[0]) is { } folder)
            {
                // Varios: primero se entra en la carpeta y luego van los nombres entre comillas.
                SendMessage(box, WmSetText, IntPtr.Zero, folder);
                PostMessage(hwnd, WmCommand, IdOk, IntPtr.Zero);
                Thread.Sleep(450);
                box = NameBox(hwnd, kind);
                SendMessage(box, WmSetText, IntPtr.Zero, string.Join(" ", paths.Select(p => $"\"{Path.GetFileName(p)}\"")));
            }
            else if (kind == Kind.Folder)
            {
                // Con una ruta escrita, el cuadro de carpetas entra en ella; con el nombre vacío elige la actual.
                SendMessage(box, WmSetText, IntPtr.Zero, paths[0]);
                PostMessage(hwnd, WmCommand, IdOk, IntPtr.Zero);
                if (WaitGone(hwnd, 600))
                {
                    ok = true;
                    return;
                }
                SendMessage(NameBox(hwnd, kind), WmSetText, IntPtr.Zero, "");
            }
            else
                SendMessage(box, WmSetText, IntPtr.Zero, paths[0]);
            PostMessage(hwnd, WmCommand, IdOk, IntPtr.Zero);
            ok = WaitGone(hwnd, 2500);
            // Lo rechazó (ya avisó por qué): se vuelve a elegir en Archivos; tras dos intentos, el de Windows.
            retry = !ok && attempt < 2 && IsWindow(hwnd) && IsWindowEnabled(hwnd);
            if (!ok)
                Log.Info(retry ? "El cuadro de archivos no aceptó la ruta: se vuelve a abrir Archivos"
                               : "El cuadro de archivos no aceptó la ruta: se muestra el de Windows");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo responder al cuadro de archivos", ex);
        }
        finally
        {
            if (retry)
                Inspect(hwnd, kind, wasLayered, attempt + 1);
            else
                Done(hwnd, show: !ok);
        }
    }

    /// <summary>
    /// Cambia el tipo de archivo del cuadro. Con Automatización de UI, como si lo eligiera el usuario:
    /// el cuadro sólo se entera del cambio así (un CB_SETCURSEL a secas no le avisa).
    /// </summary>
    private static void SelectType(IntPtr hwnd, int index)
    {
        try
        {
            var combo = TypeBox(hwnd) is var box && box != IntPtr.Zero ? AutomationElement.FromHandle(box) : null;
            if (combo is not null)
            {
                if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object expand))
                    ((ExpandCollapsePattern)expand).Expand();
                var items = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                bool done = false;
                if (index < items.Count && items[index].TryGetCurrentPattern(SelectionItemPattern.Pattern, out object item))
                {
                    ((SelectionItemPattern)item).Select();
                    done = true;
                }
                if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out expand))
                    ((ExpandCollapsePattern)expand).Collapse();
                if (done)
                {
                    Thread.Sleep(80);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el tipo en el cuadro de archivos", ex);
        }
        if (TypeBox(hwnd) is var types && types != IntPtr.Zero)
        {
            SendMessage(types, CbSetCurSel, index, IntPtr.Zero);
            SendMessage(GetParent(types), WmCommand, (CbnSelChange << 16) | (GetDlgCtrlID(types) & 0xFFFF), types);
        }
    }

    /// <summary>
    /// Espera a que el cuadro se cierre. Mientras la app pregunta algo encima (¿Reemplazar el
    /// archivo?), el cuadro está deshabilitado y se sigue esperando.
    /// </summary>
    private static bool WaitGone(IntPtr hwnd, int ms)
    {
        var deadline = Environment.TickCount64 + ms;
        var limit = Environment.TickCount64 + 120_000;
        while (Environment.TickCount64 < limit)
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd))
                return true;
            if (!IsWindowEnabled(hwnd))
                deadline = Environment.TickCount64 + ms;
            else if (Environment.TickCount64 >= deadline)
                return false;
            Thread.Sleep(60);
        }
        return false;
    }

    // ── Utilidades Win32 ───────────────────────────────────────────────────

    private static string ProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string ClassOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "";
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    private static string TextOf(IntPtr hwnd)
    {
        int length = (int)SendMessage(hwnd, WmGetTextLength, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0)
            return "";
        var text = new StringBuilder(length + 1);
        SendMessage(hwnd, WmGetText, length + 1, text);
        return text.ToString();
    }

    private static List<IntPtr> Descendants(IntPtr hwnd)
    {
        var found = new List<IntPtr>();
        if (hwnd == IntPtr.Zero)
            return found;
        EnumChildWindows(hwnd, (child, _) =>
        {
            found.Add(child);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static bool PostMessage(IntPtr hwnd, uint msg, int wParam, IntPtr lParam) => PostMessage(hwnd, msg, (IntPtr)wParam, lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static IntPtr SendMessage(IntPtr hwnd, uint msg, int wParam, IntPtr lParam) => SendMessage(hwnd, msg, (IntPtr)wParam, lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, int wParam, StringBuilder text);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
}
