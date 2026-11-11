using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Reemplazo nativo del Explorador: con Archivos instalado y elegido, las carpetas que abre cualquier
/// app (doble clic, "Abrir carpeta", "Mostrar en la carpeta", Win+E de Windows) se abren en Archivos.
/// Dos caminos: un verbo predeterminado para carpetas y unidades en HKCU (solo este usuario), y un
/// gancho que atrapa las ventanas nuevas del Explorador, lee su carpeta y su selección, las cierra y
/// abre lo mismo en Archivos. Al salir de Strata UI se quita todo y Windows vuelve a lo suyo.
/// </summary>
internal static class ExplorerTakeover
{
    private const string Verb = "StrataFiles";
    private static readonly string[] Classes = [@"Software\Classes\Directory", @"Software\Classes\Drive"];
    private const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private const string RecycleBinClsid = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const uint WmClose = 0x0010;
    private const uint WmQuit = 0x0012;
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x2;

    private static Thread? _hookThread;
    private static uint _hookThreadId;
    private static bool _registered;
    private static long _allowUntil;
    private static readonly object Gate = new();
    private static readonly HashSet<IntPtr> Handling = [];
    // Ventanas que volvimos invisibles al crearse (antes de que se pinten) y que hay que devolver si no se atrapan.
    private static readonly HashSet<IntPtr> Faded = [];

    /// <summary>Encendido: Archivos instalado, elegido como explorador y con el reemplazo nativo activo.</summary>
    public static bool Active => OptionalModules.Files.ReplacesExplorer && Settings.Current.FilesTakeOverExplorer;

    /// <summary>Aplica el estado según los ajustes (al iniciar y cuando cambian).</summary>
    public static void Update()
    {
        if (Active)
        {
            Register();
            if (_hookThread is null)
            {
                // Hilo propio con su bucle de mensajes: los avisos llegan al instante aunque la interfaz esté ocupada,
                // y así la ventana del Explorador se vuelve invisible antes de pintarse.
                _hookThread = new Thread(HookLoop) { IsBackground = true, Name = "Strata UI: ventanas del Explorador" };
                _hookThread.Start();
                Log.Info("Archivos reemplaza al Explorador de Windows");
            }
        }
        else
            Stop();
    }

    public static void Stop()
    {
        if (_hookThread is { } thread)
        {
            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            thread.Join(1000);
            _hookThread = null;
            _hookThreadId = 0;
        }
        IntPtr[] faded;
        lock (Gate)
        {
            faded = [.. Faded];
            Faded.Clear();
            Handling.Clear();
        }
        foreach (var hwnd in faded)
            Unfade(hwnd);
        Unregister();
    }

    private static void HookLoop()
    {
        _hookThreadId = GetCurrentThreadId();
        WinEventProc proc = OnEvent;
        var hook = SetWinEventHook(EventObjectCreate, EventObjectShow, IntPtr.Zero, proc, 0, 0, WinEventOutOfContext);
        if (hook == IntPtr.Zero)
        {
            Log.Info("No se pudo vigilar las ventanas del Explorador");
            return;
        }
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWinEvent(hook);
        GC.KeepAlive(proc);
    }

    /// <summary>
    /// Deja pasar al Explorador de Windows unos segundos: para cuando el usuario lo pide a propósito
    /// ("Mostrar en el Explorador de Windows").
    /// </summary>
    public static void Allow() => Interlocked.Exchange(ref _allowUntil, Environment.TickCount64 + 4000);

    private static bool Allowed => Environment.TickCount64 < Interlocked.Read(ref _allowUntil);

    /// <summary>Abre la carpeta en el Explorador de Windows sin que Archivos la atrape.</summary>
    public static void OpenInWindowsExplorer(string? arguments)
    {
        Allow();
        Log.Info($"Explorador de Windows a pedido: {arguments ?? "(sin carpeta)"}");
        try
        {
            // El verbo "open" explícito usa el Explorador aunque el predeterminado sea el de Strata.
            var info = arguments is null
                ? new ProcessStartInfo("explorer.exe") { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true };
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo abrir el Explorador de Windows", ex);
        }
    }

    // ── Verbo predeterminado para carpetas ─────────────────────────────────

    private static void Register()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "StrataUI.exe");
        string command = $"\"{exe}\" --files \"%V\"";
        try
        {
            foreach (string root in Classes)
            {
                using var shell = Registry.CurrentUser.CreateSubKey(root + @"\shell");
                using (var verb = shell.CreateSubKey(Verb))
                {
                    verb.SetValue("", Loc.T("Abrir en Archivos"));
                    verb.SetValue("Icon", exe);
                    using var cmd = verb.CreateSubKey("command");
                    cmd.SetValue("", command);
                }
                // Solo si nadie más lo eligió: no pisamos el predeterminado de otra app.
                if (shell.GetValue("") is not string current || current.Length == 0 || current == Verb)
                    shell.SetValue("", Verb);
            }
            if (!_registered)
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
            _registered = true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo registrar Archivos para abrir carpetas", ex);
        }
    }

    private static void Unregister()
    {
        bool changed = false;
        try
        {
            foreach (string root in Classes)
            {
                using var shell = Registry.CurrentUser.OpenSubKey(root + @"\shell", writable: true);
                if (shell is null)
                    continue;
                if (shell.GetValue("") as string == Verb)
                {
                    shell.DeleteValue("", throwOnMissingValue: false);
                    changed = true;
                }
                if (shell.OpenSubKey(Verb) is { } existing)
                {
                    existing.Dispose();
                    shell.DeleteSubKeyTree(Verb, throwOnMissingSubKey: false);
                    changed = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo quitar el registro de Archivos para carpetas", ex);
        }
        _registered = false;
        if (changed)
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    // ── Ventanas nuevas del Explorador ─────────────────────────────────────

    private static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero)
            return;
        if (ev == EventObjectDestroy)
        {
            lock (Gate)
            {
                Faded.Remove(hwnd);
                Handling.Remove(hwnd);
            }
            return;
        }
        if (!IsExplorerWindow(hwnd))
            return;
        if (Allowed)
        {
            // El usuario pidió el Explorador de Windows: si lo volvimos invisible al crearse, se devuelve.
            bool wasFaded;
            lock (Gate)
                wasFaded = Faded.Remove(hwnd);
            if (wasFaded)
                Unfade(hwnd);
            return;
        }
        if (ev == EventObjectCreate)
        {
            // Todavía no se ve: se vuelve invisible ya, así no alcanza a asomarse. Si el aviso llegó tarde y la
            // ventana ya se mostró, se encarga el aviso de "mostrada" (y no tapamos una que se devolvió a propósito).
            bool busy;
            lock (Gate)
                busy = Handling.Contains(hwnd);
            if (busy || IsWindowVisible(hwnd))
                return;
            if (Fade(hwnd))
                lock (Gate)
                    Faded.Add(hwnd);
            return;
        }
        lock (Gate)
        {
            if (!Handling.Add(hwnd))
                return;
            if (!Faded.Contains(hwnd) && Fade(hwnd))
                Faded.Add(hwnd);
        }
        // Además se oculta (sin botón en la barra); si al final no es una carpeta, vuelve a mostrarse.
        ShowWindow(hwnd, SwHide);
        var worker = new Thread(() => Inspect(hwnd)) { IsBackground = true, Name = "Strata UI: carpeta del Explorador" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        var name = new System.Text.StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() == "CabinetWClass" && GetParent(hwnd) == IntPtr.Zero;
    }

    /// <summary>Lee la carpeta y la selección de la ventana (en su propio hilo: el COM del Explorador puede tardar).</summary>
    private static void Inspect(IntPtr hwnd)
    {
        string? path = null;
        string? selected = null;
        var clock = Stopwatch.StartNew();
        dynamic? shell = null;
        try
        {
            // Una sola conexión con el Explorador para todas las lecturas: crearla cuesta cientos de ms.
            if (Type.GetTypeFromProgID("Shell.Application") is { } type)
                shell = Activator.CreateInstance(type);
            if (shell != null)
            {
                var deadline = Environment.TickCount64 + 1500;
                while (Environment.TickCount64 < deadline && path is null)
                {
                    path = Read(shell, hwnd, out selected);
                    if (path is null)
                        Thread.Sleep(40);
                }
                // "Mostrar en la carpeta" marca el archivo un poco después de abrir la carpeta.
                if (path != null && selected is null)
                {
                    var selectDeadline = Environment.TickCount64 + 250;
                    while (Environment.TickCount64 < selectDeadline && selected is null)
                    {
                        Thread.Sleep(50);
                        Read(shell, hwnd, out selected);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer la carpeta del Explorador", ex);
        }
        finally
        {
            if (shell != null)
                Marshal.FinalReleaseComObject(shell);
        }

        string? target = Translate(path);
        bool take = path != null && (target != null || path == ThisPc);
        if (take && selected != null && File.Exists(selected))
            target = selected;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            bool wasFaded;
            lock (Gate)
            {
                Handling.Remove(hwnd);
                wasFaded = !take || !Active ? Faded.Remove(hwnd) : false;
            }
            if (!take || !Active)
            {
                if (wasFaded)
                    Unfade(hwnd);
                ShowWindow(hwnd, SwShow);
                return;
            }
            PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            Log.Info($"Carpeta del Explorador abierta en Archivos: {target ?? "Este equipo"} ({clock.ElapsedMilliseconds} ms)");
            App.Current.OpenFiles(target);
        });
    }

    /// <summary>Lo que Archivos sabe abrir: carpetas, unidades, la Papelera; null si no (Panel de control…).</summary>
    private static string? Translate(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (path.Equals(RecycleBinClsid, StringComparison.OrdinalIgnoreCase))
            return Places.RecycleBin;
        if (path.StartsWith("::", StringComparison.Ordinal))
            return null;
        return Directory.Exists(path) ? path : null;
    }

    private static string? Read(dynamic shell, IntPtr hwnd, out string? selected)
    {
        selected = null;
        try
        {
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic? window = null;
                try
                {
                    window = windows.Item(i);
                    if (window is null || (IntPtr)(long)window.HWND != hwnd)
                        continue;
                    dynamic document = window.Document;
                    if (document is null)
                        return null;
                    string? path = document.Folder?.Self?.Path;
                    dynamic? items = document.SelectedItems();
                    if (items is not null && (int)items!.Count == 1)
                        selected = (string?)items!.Item(0)?.Path;
                    return string.IsNullOrEmpty(path) ? null : path;
                }
                catch (COMException)
                {
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                }
                finally
                {
                    if (window != null)
                        Marshal.FinalReleaseComObject(window);
                }
            }
        }
        catch (COMException)
        {
        }
        return null;
    }

    /// <summary>Vuelve la ventana totalmente transparente y que no reciba clics; false si ya tenía transparencia propia.</summary>
    private static bool Fade(IntPtr hwnd)
    {
        int style = GetWindowLong(hwnd, GwlExStyle);
        if ((style & WsExLayered) != 0)
            return false;
        SetWindowLong(hwnd, GwlExStyle, style | WsExLayered | WsExTransparent);
        if ((GetWindowLong(hwnd, GwlExStyle) & WsExLayered) == 0)
            return false;
        SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
        return true;
    }

    private static void Unfade(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
            return;
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) & ~(WsExLayered | WsExTransparent));
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400); // INVALIDATE|ERASE|ALLCHILDREN|FRAME
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam; public IntPtr LParam; public uint Time; public int X; public int Y; }

    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
