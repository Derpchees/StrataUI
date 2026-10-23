using System.Windows;
using Strata.Shell.Services.Modules;
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
        ModuleStore.Changed += OnModulesChanged;
    }

    /// <summary>El Explorador se reinició: la ventana del escritorio es otra y los widgets se vuelven a enganchar.</summary>
    private static void OnExplorerRestarted() =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in Windows.Values)
                window.AttachToDesktop();
        });

    /// <summary>Cambiaron las capas del escritorio de Strata UI: cada widget toma la de su monitor como dueña.</summary>
    public static void Reattach()
    {
        foreach (var window in Windows.Values)
            window.AttachToDesktop();
    }

    public static void Shutdown()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        ExplorerWatch.Restarted -= OnExplorerRestarted;
        ModuleStore.Changed -= OnModulesChanged;
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
    public static void Add(WidgetKind kind, bool showDate = true, string? module = null)
    {
        var config = new WidgetConfig
        {
            Kind = kind,
            ShowDate = showDate,
            Module = module,
            Device = App.MainScreenDevice(System.Windows.Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.Primary))),
            X = 0.5,
            Y = Math.Min(0.85, 0.15 + 0.2 * Settings.Current.Widgets.Count),
        };
        Settings.Update(s => s.Widgets.Add(config));
        Open(config);
        Log.Info($"Widget agregado: {kind}{(module is null ? "" : " " + module)}");
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

    /// <summary>Ventana de un widget abierto (el modo edición la muestra tal cual se ve).</summary>
    public static WidgetWindow? WindowOf(string id) => Windows.GetValueOrDefault(id);

    /// <summary>
    /// Aplica una lista completa de widgets (modo edición): cierra los que ya no están, abre los
    /// nuevos y mueve o cambia los demás. La lista ya debe estar guardada en la configuración.
    /// </summary>
    public static void Sync()
    {
        var wanted = Settings.Current.Widgets.ToDictionary(w => w.Id);
        foreach (var id in Windows.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            if (Windows.Remove(id, out var gone))
                gone.Close();
        foreach (var config in wanted.Values)
        {
            if (Windows.TryGetValue(config.Id, out var window))
                window.Update(config);
            else
                Open(config);
        }
        Changed?.Invoke();
        // Las barras nuevas cambian el área de trabajo un momento después: cada widget vuelve a su lugar.
        var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        later.Tick += (_, _) =>
        {
            later.Stop();
            foreach (var w in Windows.Values)
                w.PlaceFromConfig();
        };
        later.Start();
    }

    private static void Open(WidgetConfig config)
    {
        FrameworkElement content = config.Kind switch
        {
            WidgetKind.Visualizer => new VisualizerWidget(config),
            WidgetKind.Cpu or WidgetKind.Gpu or WidgetKind.Fps or WidgetKind.NetSpeed => new MeterWidget(config),
            WidgetKind.Custom => ModuleStore.Get<WidgetModule>(ModuleKind.Widget, config.Module) is { } module
                ? new CustomWidget(config, module)
                : CustomWidget.Missing(config.Module),
            _ => new ClockWidget(config),
        };
        var window = new WidgetWindow(config, content);
        Windows[config.Id] = window;
        window.Show();
    }

    /// <summary>Se reimportó o quitó un widget importado: los que lo usan se vuelven a crear.</summary>
    private static void OnModulesChanged(ModuleKind kind)
    {
        if (kind != ModuleKind.Widget)
            return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var config in Settings.Current.Widgets.Where(w => w.Kind == WidgetKind.Custom))
            {
                if (Windows.Remove(config.Id, out var old))
                    old.Close();
                Open(config);
            }
        });
    }

    /// <summary>Otra resolución o monitores: cada widget vuelve a su lugar relativo.</summary>
    private static void OnDisplaysChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in Windows.Values)
                window.PlaceFromConfig();
        });
}
