using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using ManagedShell.WindowsTray;
using Winx.Shell.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.Themes;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Winx.Shell;

public partial class Taskbar : AppBarWindow
{
    private readonly ShellManager _shell;
    private readonly bool _multiMonitor;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _statusTimer;

    public Taskbar(ShellManager shell, AppBarScreen screen, bool multiMonitor)
        : base(shell.AppBarManager, shell.ExplorerHelper, shell.FullScreenHelper,
               screen, AppBarEdge.Bottom, AppBarMode.Normal, ReservedHeight)
    {
        _shell = shell;
        _multiMonitor = multiMonitor;
        InitializeComponent();

        VersionItem.Header = $"WINX v{App.Version}";
        TaskList.ItemsSource = CreateTaskView();

        if (screen.Primary)
        {
            var tray = shell.NotificationArea;
            PinnedTray.ItemsSource = tray.PinnedIcons;
            HiddenTray.ItemsSource = tray.UnpinnedIcons;
            ((INotifyCollectionChanged)tray.UnpinnedIcons).CollectionChanged += (_, _) => UpdateChevron();
            UpdateChevron();
            LocationChanged += (_, _) => ReportTrayPosition();
            SizeChanged += (_, _) => ReportTrayPosition();
        }
        else
        {
            TrayArea.Visibility = Visibility.Collapsed;
        }

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        Settings.Changed += ApplySettings;
        ApplySettings();

        App.Current.Audio.Changed += UpdateVolume;
        UpdateVolume();

        SourceInitialized += (_, _) => ApplyBackdrop();
        SizeChanged += (_, _) => ApplyBackdrop();
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    /// <summary>
    /// Cambia el alto reservado sin destruir la barra (recrearla obligaba a Windows a
    /// reacomodar todas las ventanas maximizadas dos veces).
    /// </summary>
    public void Resize(double height)
    {
        DesiredHeight = height;
        Height = height;
        UpdatePosition();
    }

    /// <summary>Alto reservado en pantalla: la barra más el margen inferior si flota.</summary>
    public static double ReservedHeight =>
        Settings.Current.TaskbarHeight + (ThemeManager.Theme.Floating ? ThemeCatalog.FloatingMargin : 0);

    /// <summary>
    /// Desenfoque detrás de la barra (Acrylic o vidrio limpio) y, si flota, recorte a la cápsula
    /// para que ni el desenfoque ni los clics ocupen el margen alrededor.
    /// </summary>
    private LiquidGlassSurface? _glass;

    private void ApplyBackdrop()
    {
        var theme = ThemeManager.Theme;
        // Con refracción en vivo el shader pinta el vidrio; el desenfoque de DWM queda debajo
        // solo para suavizar el borde antialiasado.
        WindowEffects.SetBlur(this, theme.Backdrop);
        UpdateGlassSurface();
        if (!theme.Floating || ActualWidth <= 0)
        {
            WindowEffects.SetRoundedRegion(this, null, 0);
            return;
        }
        double scale = DpiScale;
        int margin = (int)Math.Round(ThemeCatalog.FloatingMargin * scale);
        int width = (int)Math.Round(ActualWidth * scale) - 2 * margin;
        int height = (int)Math.Round(ActualHeight * scale) - margin;
        WindowEffects.SetRoundedRegion(this, new Int32Rect(margin, 0, width, height), height / 2);
    }

    private void UpdateGlassSurface()
    {
        bool wanted = ThemeManager.UseLiveGlass;
        if (wanted && _glass is null)
        {
            _glass = new LiquidGlassSurface { Refraction = 14, Frost = 3 };
            GlassHost.Children.Insert(0, _glass);
        }
        else if (!wanted && _glass != null)
        {
            GlassHost.Children.Remove(_glass);
            _glass = null;
        }
        if (_glass != null)
        {
            _glass.CornerRadius = Settings.Current.TaskbarHeight / 2;
            _glass.SetLook(ThemeManager.IsLight);
        }
    }

    // WPF-UI termina de aplicar su tema de forma asíncrona sobre la ventana principal (la barra)
    // y deshace el efecto: se reaplica cuando la app queda inactiva.
    private void OnThemeChanged() =>
        Dispatcher.BeginInvoke(ApplyBackdrop, DispatcherPriority.ApplicationIdle);

    private void ApplySettings()
    {
        var s = Settings.Current;
        TaskArea.HorizontalAlignment = s.TaskbarAlignment == TaskbarAlignment.Left
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        DateText.Visibility = s.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        NetworkButton.Visibility = s.ShowNetworkModule ? Visibility.Visible : Visibility.Collapsed;
        BluetoothButton.Visibility = s.ShowBluetoothModule ? Visibility.Visible : Visibility.Collapsed;
        VolumeButton.Visibility = s.ShowVolumeModule ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock();
        UpdateStatus();
    }

    private ICollectionView CreateTaskView()
    {
        var view = _shell.Tasks.CreateGroupedWindowsCollection();
        view.Filter = item => item is ApplicationWindow window
                              && window.ShowInTaskbar
                              && (!_multiMonitor || window.HMonitor == Screen.HMonitor);

        if (view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.ShowInTaskbar));
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.HMonitor));
            live.IsLiveFiltering = true;
        }
        return view;
    }

    private void UpdateChevron() =>
        ChevronButton.Visibility = _shell.NotificationArea.UnpinnedIcons
            .OfType<ShellNotifyIcon>().Any(icon => !TrayIcon.IsReplacedByWinx(icon))
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Algunas apps preguntan dónde está la bandeja para colocar sus menús.</summary>
    private void ReportTrayPosition()
    {
        var rect = WindowRect;
        _shell.NotificationArea.SetTrayHostSizeData(new TrayHostSizeData
        {
            edge = NativeMethods.ABEdge.ABE_BOTTOM,
            rc = new NativeMethods.Rect
            {
                Left = (int)(rect.Left * DpiScale),
                Top = (int)(rect.Top * DpiScale),
                Right = (int)(rect.Right * DpiScale),
                Bottom = (int)(rect.Bottom * DpiScale),
            },
        });
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString(Settings.Current.ShowSeconds ? "T" : "t");
        DateText.Text = now.ToString("d");
        ClockButton.ToolTip = now.ToString("D");
    }

    private void UpdateStatus()
    {
        var network = SystemStatus.GetNetwork();
        NetworkGlyph.Text = network.Glyph.ToString();
        NetworkGlyph.Opacity = network.Connected ? 1 : 0.4;
        NetworkButton.ToolTip = network.Description;

        var battery = Settings.Current.ShowBatteryModule ? SystemStatus.GetBattery() : null;
        BatteryButton.Visibility = battery is null ? Visibility.Collapsed : Visibility.Visible;
        if (battery != null)
        {
            BatteryGlyph.Text = battery.Glyph.ToString();
            BatteryButton.ToolTip = $"Batería: {battery.Percent}%{(battery.Charging ? " (cargando)" : "")}";
        }
    }

    protected override void CustomClosing()
    {
        Settings.Changed -= ApplySettings;
        App.Current.Audio.Changed -= UpdateVolume;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _clockTimer.Stop();
        _statusTimer.Stop();
        base.CustomClosing();
    }

    private static void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    // Mientras no existan los paneles propios (próximas versiones), se abren los de Windows.

    /// <summary>Botón Inicio (lo usa también la tecla Windows para anclar el menú).</summary>
    public FrameworkElement StartAnchor => StartButton;

    public bool IsPrimary => Screen.Primary;

    private void StartButton_Click(object sender, RoutedEventArgs e) =>
        StartMenu.StartMenuLauncher.Toggle(StartButton);

    private void ClockButton_Click(object sender, RoutedEventArgs e) => ShellHelper.ShowNotificationCenter();

    private void NetworkButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.NetworkFlyout>(NetworkButton);

    private void BluetoothButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.BluetoothFlyout>(BluetoothButton);

    private void VolumeButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.VolumeFlyout>(VolumeButton);

    private void TaskScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TaskScroller.ScrollableWidth <= 0)
            return;
        TaskScroller.ScrollToHorizontalOffset(TaskScroller.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private void VolumeButton_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var audio = App.Current.Audio;
        audio.Volume += e.Delta > 0 ? 0.02f : -0.02f;
        if (audio.Mute && e.Delta > 0)
            audio.Mute = false;
        e.Handled = true;
    }

    private void UpdateVolume()
    {
        var audio = App.Current.Audio;
        int percent = (int)Math.Round(audio.Volume * 100);
        VolumeGlyph.Text = !audio.HasDevice || audio.Mute ? ""   // silenciado
            : percent == 0 ? ""
            : percent < 33 ? ""
            : percent < 66 ? ""
            : "";
        VolumeButton.ToolTip = !audio.HasDevice ? "Sin dispositivo de audio"
            : audio.Mute ? $"Volumen: silenciado ({percent}%)"
            : $"Volumen: {percent}%";
    }

    private void BatteryButton_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:batterysaver");

    private void ShowDesktop_Click(object sender, MouseButtonEventArgs e)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null)
            return;
        dynamic shellApp = Activator.CreateInstance(type)!;
        shellApp.ToggleDesktop();
    }

    private void OpenHub_Click(object sender, RoutedEventArgs e) => App.Current.OpenHub();

    private void TaskManager_Click(object sender, RoutedEventArgs e) => ShellHelper.StartTaskManager();

    private void WindowsSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:");

    private void RestartWinx_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell(restart: true);

    private void ExitWinx_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell();
}
