using System.Windows;
using Strata.Shell.Widgets;

namespace Strata.Shell.Services.Widgets;

/// <summary>
/// Widgets del escritorio (<see cref="StrataSettings.Widgets"/>): una ventana por widget, al fondo
/// de todo. Se agregan y quitan desde Hub › Widgets o con su clic derecho.
/// </summary>
public static class WidgetManager
{
    private static readonly Dictionary<string, WidgetWindow> Windows = new();

    /// <summary>Cambió la lista (para el Hub).</summary>
    public static event Action? Changed;

    public static void Initialize()
    {
        foreach (var config in Settings.Current.Widgets)
            Open(config);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        ExplorerWatch.Restarted += OnExplorerRestarted;
    }

    /// <summary>El Explorador se reinició: la ventana del escritorio es otra y los widgets se vuelven a enganchar.</summary>
    private static void OnExplorerRestarted() =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in Windows.Values)
                window.AttachToDesktop();
        });

    public static void Shutdown()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        ExplorerWatch.Restarted -= OnExplorerRestarted;
        foreach (var window in Windows.Values.ToList())
        {
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
                // Ya se cerró al salir la app.
            }
        }
        Windows.Clear();
    }

    /// <summary>Agrega un widget en el monitor principal de Strata UI (arriba al centro, o un poco más abajo si ya hay otros).</summary>
    public static void Add(WidgetKind kind, bool showDate = true)
    {
        var config = new WidgetConfig
        {
            Kind = kind,
            ShowDate = showDate,
            Device = App.MainScreenDevice(System.Windows.Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.Primary))),
            X = 0.5,
            Y = Math.Min(0.85, 0.15 + 0.2 * Settings.Current.Widgets.Count),
        };
        Settings.Update(s => s.Widgets.Add(config));
        Open(config);
        Log.Info($"Widget agregado: {kind}");
        Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        Settings.Update(s => s.Widgets.RemoveAll(w => w.Id == id));
        if (Windows.Remove(id, out var window))
            window.Close();
        Changed?.Invoke();
    }

    /// <summary>Cambia un widget, lo guarda y lo aplica a su ventana.</summary>
    public static void Save(string id, Action<WidgetConfig> change)
    {
        var config = Settings.Current.Widgets.FirstOrDefault(w => w.Id == id);
        if (config is null)
            return;
        Settings.Update(_ => change(config));
        if (Windows.TryGetValue(id, out var window))
            window.Update(config);
        Changed?.Invoke();
    }

    private static void Open(WidgetConfig config)
    {
        FrameworkElement content = config.Kind switch
        {
            WidgetKind.Visualizer => new VisualizerWidget(config),
            _ => new ClockWidget(config),
        };
        var window = new WidgetWindow(config, content);
        Windows[config.Id] = window;
        window.Show();
    }

    /// <summary>Otra resolución o monitores: cada widget vuelve a su lugar relativo.</summary>
    private static void OnDisplaysChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in Windows.Values)
                window.PlaceFromConfig();
        });
}
