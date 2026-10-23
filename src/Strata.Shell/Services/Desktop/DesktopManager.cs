using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Strata.Shell.Desktop;

namespace Strata.Shell.Services.Desktop;

/// <summary>
/// El escritorio de Strata UI: una capa por monitor pegada al fondo (como los widgets), con los íconos
/// en el monitor principal de Strata UI y el menú contextual en todos. Mientras está activo, los íconos
/// de Windows se ocultan (taparían o recibirían los clics); al salir vuelven como estaban. Los widgets
/// usan la capa de su monitor como dueña: así quedan siempre encima de los íconos.
/// </summary>
public static class DesktopManager
{
    private static readonly Dictionary<string, DesktopWindow> Layers = new();

    public static void Initialize()
    {
        Sync();
        Settings.Changed += OnSettingsChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        ExplorerWatch.Restarted += OnExplorerRestarted;
    }

    public static void Shutdown()
    {
        Settings.Changed -= OnSettingsChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        ExplorerWatch.Restarted -= OnExplorerRestarted;
        CloseAll();
        RestoreWindowsIcons();
    }

    /// <summary>La capa de ese monitor (dueña de sus widgets); cero si no hay.</summary>
    public static IntPtr OwnerFor(string? device) =>
        device != null && Layers.TryGetValue(device, out var layer) ? new WindowInteropHelper(layer).Handle : IntPtr.Zero;

    private static bool _enabled;
    private static string _layout = "";

    private static void OnSettingsChanged()
    {
        var s = Settings.Current;
        string layout = $"{s.DesktopShowIcons}|{s.DesktopIconSize}|{s.DesktopAutoArrange}|{s.DesktopSort}|{s.FilesShowHidden}|{s.FilesShowExtensions}";
        if (s.DesktopEnabled != _enabled)
        {
            Sync();
        }
        else if (layout != _layout)
        {
            _layout = layout;
            foreach (var layer in Layers.Values)
                layer.Refresh();
        }
    }

    private static void OnDisplaysChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(Sync, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>El Explorador se reinició: su escritorio es otro (dueño de las capas) y vuelve a mostrar sus íconos.</summary>
    private static void OnExplorerRestarted() =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!Settings.Current.DesktopEnabled)
                return;
            HideWindowsIcons();
            foreach (var layer in Layers.Values)
                layer.AttachToDesktop();
            Widgets.WidgetManager.Reattach();
        }, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>Una capa por monitor (los íconos en el principal de Strata UI), o ninguna si está apagado.</summary>
    public static void Sync()
    {
        var s = Settings.Current;
        _enabled = s.DesktopEnabled;
        _layout = $"{s.DesktopShowIcons}|{s.DesktopIconSize}|{s.DesktopAutoArrange}|{s.DesktopSort}|{s.FilesShowHidden}|{s.FilesShowExtensions}";
        if (!s.DesktopEnabled)
        {
            CloseAll();
            RestoreWindowsIcons();
            return;
        }
        // La primera vez, los íconos de Strata UI se ven si se veían los de Windows.
        if (s.DesktopShowIcons is null)
        {
            bool visible = WindowsIconsVisible();
            Settings.Update(x => x.DesktopShowIcons = visible);
        }
        HideWindowsIcons();

        var screens = System.Windows.Forms.Screen.AllScreens;
        string main = App.MainScreenDevice(screens.Select(sc => (sc.DeviceName, sc.Primary)));
        var gone = Layers.Keys.Where(d => screens.All(sc => sc.DeviceName != d)).ToList();
        if (gone.Count > 0)
        {
            var closing = gone.Select(d => Layers[d]).ToList();
            foreach (var device in gone)
                Layers.Remove(device);
            // Los widgets cambian de dueño antes de cerrar la capa (si no, Windows los cerraría con ella).
            Widgets.WidgetManager.Reattach();
            closing.ForEach(layer => layer.Close());
        }
        foreach (var screen in screens)
        {
            if (!Layers.TryGetValue(screen.DeviceName, out var layer))
            {
                layer = new DesktopWindow(screen.DeviceName);
                Layers[screen.DeviceName] = layer;
                layer.Show();
            }
            layer.Place(screen.DeviceName == main);
        }
        Widgets.WidgetManager.Reattach();
    }

    private static void CloseAll()
    {
        if (Layers.Count == 0)
            return;
        var closing = Layers.Values.ToList();
        Layers.Clear();
        Widgets.WidgetManager.Reattach();
        foreach (var layer in closing)
        {
            try
            {
                layer.Close();
            }
            catch (InvalidOperationException)
            {
                // Ya se cerró al salir la app.
            }
        }
    }

    // ── Íconos de Windows ───────────────────────────────────────────────────

    /// <summary>La vista de íconos del escritorio de Windows (SHELLDLL_DefView, dentro de Progman o de un WorkerW).</summary>
    private static IntPtr DefView()
    {
        var progman = FindWindow("Progman", null);
        var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr worker = IntPtr.Zero;
        while (view == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        return view;
    }

    private static bool WindowsIconsVisible()
    {
        var view = DefView();
        var list = view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", "FolderView");
        return list != IntPtr.Zero && IsWindowVisible(list);
    }

    /// <summary>El comando "Mostrar íconos del escritorio" del menú de Windows (cambia también su ajuste).</summary>
    private static void ToggleWindowsIcons()
    {
        var view = DefView();
        if (view != IntPtr.Zero)
            SendMessage(view, WmCommand, (IntPtr)ToggleDesktopIcons, IntPtr.Zero);
    }

    private static void HideWindowsIcons()
    {
        if (!WindowsIconsVisible())
            return;
        ToggleWindowsIcons();
        Settings.Update(s => s.DesktopRestoreWindowsIcons = true);
        Log.Info("Escritorio: se ocultan los íconos de Windows (vuelven al salir)");
    }

    /// <summary>Vuelven los íconos de Windows si Strata UI los había ocultado.</summary>
    public static void RestoreWindowsIcons()
    {
        if (!Settings.Current.DesktopRestoreWindowsIcons)
            return;
        if (!WindowsIconsVisible())
            ToggleWindowsIcons();
        Settings.Update(s => s.DesktopRestoreWindowsIcons = false);
        Log.Info("Escritorio: vuelven los íconos de Windows");
    }

    private const int WmCommand = 0x111, ToggleDesktopIcons = 0x7402;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
