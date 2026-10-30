using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Updates;
using Wpf.Ui.Controls;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Dashboard del Hub (--hub Home o Dashboard): versión y actualizaciones, uso de CPU, RAM y GPU en
/// vivo, los interruptores y acciones más usados y accesos a cada sección.
/// </summary>
public partial class HomePage : Page
{
    public sealed record Tile(SymbolRegular Icon, string Title, string Page);

    /// <summary>Un medidor del equipo; cambia cada vez que <see cref="SystemMonitor"/> lee.</summary>
    public sealed class Stat(SymbolRegular icon, string name) : INotifyPropertyChanged
    {
        public SymbolRegular Icon { get; } = icon;
        public string Name { get; } = name;
        public string Value { get; private set; } = "–";
        public double Percent { get; private set; }
        public string Detail { get; private set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Set(double? percent, string detail)
        {
            Value = percent is { } p ? $"{p:0}%" : "–";
            Percent = percent ?? 0;
            Detail = detail;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    private readonly Stat _cpu = new(SymbolRegular.DeveloperBoard24, Loc.T("CPU"));
    private readonly Stat _ram = new(SymbolRegular.Ram20, Loc.T("Memoria RAM"));
    private readonly Stat _gpu = new(SymbolRegular.Gauge24, Loc.T("GPU"));

    public HomePage()
    {
        InitializeComponent();
        Greeting.Text = DateTime.Now.Hour switch
        {
            < 12 => Loc.T("Buenos días"),
            < 19 => Loc.T("Buenas tardes"),
            _ => Loc.T("Buenas noches"),
        };
        HeroSubtitle.Text = Loc.F("Strata UI {0}", App.Version);
        Stats.ItemsSource = new[] { _cpu, _ram, _gpu };
        Tiles.ItemsSource = new[]
        {
            new Tile(SymbolRegular.PanelBottom20, Loc.T("Barra de tareas"), "Taskbar"),
            new Tile(SymbolRegular.Grid24, Loc.T("Menú Inicio"), "Start"),
            new Tile(SymbolRegular.Alert24, Loc.T("Notificaciones"), "Notifications"),
            new Tile(SymbolRegular.PaintBrush24, Loc.T("Tema y colores"), "Appearance"),
            new Tile(SymbolRegular.Apps24, Loc.T("Widgets"), "Widgets"),
            new Tile(SymbolRegular.Flash24, Loc.T("Optimización"), "Optimization"),
        };
        LoadSwitches();

        ApplyHeroBrush();
        Loaded += (_, _) =>
        {
            ThemeManager.ThemeChanged += ApplyHeroBrush;
            UpdateService.Changed += OnUpdateChanged;
            Settings.Changed += LoadSwitches;
            SystemMonitor.Changed += RefreshStats;
            SystemMonitor.Subscribe();
            RefreshStats();
        };
        Unloaded += (_, _) =>
        {
            ThemeManager.ThemeChanged -= ApplyHeroBrush;
            UpdateService.Changed -= OnUpdateChanged;
            Settings.Changed -= LoadSwitches;
            SystemMonitor.Changed -= RefreshStats;
            SystemMonitor.Unsubscribe();
        };
        RefreshUpdate();
    }

    private void LoadSwitches()
    {
        var s = Settings.Current;
        DndSwitch.IsChecked = s.NotificationDoNotDisturb;
        SoundSwitch.IsChecked = s.NotificationSounds;
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
    }

    private void RefreshStats()
    {
        const double Gb = 1073741824.0;
        _cpu.Set(SystemMonitor.CpuPercent, HardwareInfo.CpuName ?? "");
        _ram.Set(SystemMonitor.RamTotal > 0 ? SystemMonitor.RamPercent : null,
                 SystemMonitor.RamTotal > 0 ? Loc.F("{0:0.0} de {1:0.0} GB en uso", SystemMonitor.RamUsed / Gb, SystemMonitor.RamTotal / Gb) : "");
        _gpu.Set(SystemMonitor.GpuPercent, HardwareInfo.GpuNames.FirstOrDefault() ?? "");
    }

    /// <summary>Degradado diagonal del color de acento a un tono más oscuro.</summary>
    private void ApplyHeroBrush()
    {
        var accent = (TryFindResource("StrataAccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x67, 0xC0);
        Color Darker(double f) => Color.FromRgb((byte)(accent.R * f), (byte)(accent.G * f), (byte)(accent.B * f));
        Hero.Background = new LinearGradientBrush(
            [new GradientStop(accent, 0), new GradientStop(Darker(0.55), 1)],
            new Point(0, 0), new Point(1, 1));
    }

    private void OnUpdateChanged() => Dispatcher.BeginInvoke(RefreshUpdate);

    private void RefreshUpdate()
    {
        (UpdateIcon.Symbol, UpdateText.Text) = UpdateService.State switch
        {
            UpdateState.Available or UpdateState.Downloading when UpdateService.Available is { } u =>
                (SymbolRegular.ArrowCircleDown24, Loc.F("Strata UI {0} está disponible", u.Version.ToString(3))),
            UpdateState.UpToDate => (SymbolRegular.CheckmarkCircle24, Loc.T("Al día")),
            UpdateState.Checking => (SymbolRegular.ArrowSync24, Loc.T("Buscando…")),
            _ => (SymbolRegular.ArrowSync24, Loc.T("Buscar actualizaciones")),
        };
    }

    private void Navigate(string page) => (Window.GetWindow(this) as HubWindow)?.NavigateTo(page);

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Tile tile })
            Navigate(tile.Page);
    }

    private void UpdateChip_Click(object sender, RoutedEventArgs e) => Navigate("Updates");

    private void Dnd_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationDoNotDisturb = DndSwitch.IsChecked == true);

    private void Sound_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationSounds = SoundSwitch.IsChecked == true);

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AutoStart.IsEnabled = AutoStartSwitch.IsChecked == true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el inicio automático", ex);
            AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
        }
    }

    private async void FreeRam_Click(object sender, RoutedEventArgs e)
    {
        FreeRamButton.IsEnabled = false;
        ShowStatus(Loc.T("Liberando RAM…"));
        try
        {
            ulong before = SystemMonitor.AvailableRam();
            if (!await Sensors.FreeRamAsync())
            {
                ShowStatus(Loc.T("No se liberó la RAM"));
                return;
            }
            ulong after = SystemMonitor.AvailableRam();
            double freed = after > before ? (after - before) / 1073741824.0 : 0;
            ShowStatus(freed >= 0.05 ? Loc.F("RAM liberada: {0:0.0} GB", freed) : Loc.T("La memoria ya estaba libre"));
        }
        finally
        {
            FreeRamButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text)
    {
        ActionStatus.Text = text;
        ActionStatus.Visibility = Visibility.Visible;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell(restart: true);

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell();
}
