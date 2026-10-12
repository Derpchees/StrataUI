using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using Winx.Shell.Services.Localization;

namespace Winx.Shell;

public partial class Taskbar : AppBarWindow
{
    private readonly ShellManager _shell;
    private readonly bool _multiMonitor;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _statusTimer;

    private readonly ICollectionView _taskView;
    private bool _windowsPerMonitor;

    public Taskbar(ShellManager shell, AppBarScreen screen, bool multiMonitor, TaskbarPosition position)
        : base(shell.AppBarManager, shell.ExplorerHelper, shell.FullScreenHelper,
               screen, EdgeOf(position), AppBarMode.Normal, ReservedSize(IsVerticalPosition(position)))
    {
        _shell = shell;
        _multiMonitor = multiMonitor;
        Position = position;
        InitializeComponent();
        if (IsVertical)
        {
            DesiredWidth = ReservedSize(vertical: true);
            Width = DesiredWidth;
        }
        ApplyOrientation();

        VersionItem.Header = $"WINX v{App.Version}";
        _windowsPerMonitor = Settings.Current.TaskbarWindowsPerMonitor;
        _taskView = CreateTaskView();
        TaskList.ItemsSource = _taskView;

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

    /// <summary>Borde en el que está la barra (cambiarlo recrea la barra).</summary>
    public TaskbarPosition Position { get; }

    public bool IsVertical => IsVerticalPosition(Position);

    private static bool IsVerticalPosition(TaskbarPosition position) =>
        position is TaskbarPosition.Left or TaskbarPosition.Right;

    private static AppBarEdge EdgeOf(TaskbarPosition position) => position switch
    {
        TaskbarPosition.Top => AppBarEdge.Top,
        TaskbarPosition.Left => AppBarEdge.Left,
        TaskbarPosition.Right => AppBarEdge.Right,
        _ => AppBarEdge.Bottom,
    };

    /// <summary>
    /// Cambia el grosor reservado sin destruir la barra (recrearla obligaba a Windows a
    /// reacomodar todas las ventanas maximizadas dos veces).
    /// </summary>
    public void Resize()
    {
        double size = ReservedSize(IsVertical);
        if (IsVertical)
        {
            DesiredWidth = size;
            Width = size;
        }
        else
        {
            DesiredHeight = size;
            Height = size;
        }
        UpdatePosition();
    }

    /// <summary>
    /// Grosor reservado en pantalla: la barra más el margen si flota. Las verticales son algo más
    /// anchas que el alto elegido, para que quepan el reloj y la fecha.
    /// </summary>
    public static double ReservedSize(bool vertical) =>
        Settings.Current.TaskbarHeight + (vertical ? VerticalExtra : 0)
        + (ThemeManager.Theme.Floating ? ThemeCatalog.FloatingMargin : 0);

    private const double VerticalExtra = 16;

    /// <summary>
    /// La barra se diseña horizontal; en los bordes izquierdo y derecho sus partes se apilan:
    /// Inicio y ventanas arriba, bandeja, estado y reloj abajo.
    /// </summary>
    private void ApplyOrientation()
    {
        HiddenTrayPopup.Placement = Position switch
        {
            TaskbarPosition.Top => PlacementMode.Bottom,
            TaskbarPosition.Left => PlacementMode.Right,
            TaskbarPosition.Right => PlacementMode.Left,
            _ => PlacementMode.Top,
        };
        HiddenTrayPopup.VerticalOffset = Position switch { TaskbarPosition.Top => 8, TaskbarPosition.Bottom => -8, _ => 0 };
        HiddenTrayPopup.HorizontalOffset = Position switch { TaskbarPosition.Left => 8, TaskbarPosition.Right => -8, _ => 0 };
        if (!IsVertical)
            return;

        MainGrid.ColumnDefinitions.Clear();
        MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        MainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(StatusPanel, 0);
        Grid.SetRow(StatusPanel, 1);

        TaskArea.HorizontalAlignment = HorizontalAlignment.Stretch;
        DockPanel.SetDock(StartButton, Dock.Top);
        StartButton.Width = double.NaN;
        StartButton.Height = 44;
        TaskScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        TaskScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        TaskList.ItemsPanel = Panel(typeof(StackPanel));

        StatusPanel.Orientation = Orientation.Vertical;
        TrayArea.Orientation = Orientation.Vertical;
        PinnedTray.ItemsPanel = Panel(typeof(WrapPanel));
        PinnedTray.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var button in new FrameworkElement[] { ChevronButton, NetworkButton, BluetoothButton, VolumeButton, BatteryButton })
        {
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Height = 36;
        }
        ClockButton.Padding = new Thickness(2, 6, 2, 6);
        TimeText.HorizontalAlignment = DateText.HorizontalAlignment = HorizontalAlignment.Center;
        TimeText.FontSize = DateText.FontSize = 11;

        ShowDesktopArea.Width = double.NaN;
        ShowDesktopArea.Height = 12;
        ShowDesktopLine.Width = 16;
        ShowDesktopLine.Height = 1;
        ShowDesktopLine.HorizontalAlignment = HorizontalAlignment.Center;
        ShowDesktopLine.VerticalAlignment = VerticalAlignment.Bottom;

        static ItemsPanelTemplate Panel(Type type)
        {
            var factory = new FrameworkElementFactory(type);
            if (type == typeof(StackPanel))
                factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            return new ItemsPanelTemplate(factory);
        }
    }

    /// <summary>En temas flotantes, separación de los tres lados que no tocan el borde de la pantalla.</summary>
    private Thickness FloatingMargin()
    {
        if (!ThemeManager.Theme.Floating)
            return new Thickness(0);
        double m = ThemeCatalog.FloatingMargin;
        return Position switch
        {
            TaskbarPosition.Top => new Thickness(m, m, m, 0),
            TaskbarPosition.Left => new Thickness(m, m, 0, m),
            TaskbarPosition.Right => new Thickness(0, m, m, m),
            _ => new Thickness(m, 0, m, m),
        };
    }

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
        var margin = FloatingMargin();
        BarBorder.Margin = margin;
        // Línea hacia el interior de la pantalla, o cápsula con radio de la mitad del grosor.
        double thickness = IsVertical ? ActualWidth - margin.Left - margin.Right : ActualHeight - margin.Top - margin.Bottom;
        Resources["WinxTaskbarCornerRadius"] = new CornerRadius(theme.Floating && thickness > 0 ? thickness / 2 : 0);
        Resources["WinxTaskbarBorderThickness"] = theme.Floating ? new Thickness(1) : Position switch
        {
            TaskbarPosition.Top => new Thickness(0, 0, 0, 1),
            TaskbarPosition.Left => new Thickness(0, 0, 1, 0),
            TaskbarPosition.Right => new Thickness(1, 0, 0, 0),
            _ => new Thickness(0, 1, 0, 0),
        };
        UpdateGlassSurface();
        if (!theme.Floating || ActualWidth <= 0)
        {
            WindowEffects.SetRoundedRegion(this, null, 0);
            return;
        }
        double scale = DpiScale;
        int left = (int)Math.Round(margin.Left * scale), top = (int)Math.Round(margin.Top * scale);
        int width = (int)Math.Round((ActualWidth - margin.Left - margin.Right) * scale);
        int height = (int)Math.Round((ActualHeight - margin.Top - margin.Bottom) * scale);
        WindowEffects.SetRoundedRegion(this, new Int32Rect(left, top, width, height), Math.Min(width, height) / 2);
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
            _glass.CornerRadius = ((CornerRadius)Resources["WinxTaskbarCornerRadius"]).TopLeft;
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
        if (StartButton.Tag is not StartIconKind current || current != s.StartIcon)
        {
            StartButton.Content = StartIcons.Create(s.StartIcon);
            StartButton.Tag = s.StartIcon;
        }
        // "Izquierda" en una barra vertical es arriba.
        if (IsVertical)
            TaskArea.VerticalAlignment = s.TaskbarAlignment == TaskbarAlignment.Left ? VerticalAlignment.Top : VerticalAlignment.Center;
        else
            TaskArea.HorizontalAlignment = s.TaskbarAlignment == TaskbarAlignment.Left
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
        if (s.TaskbarWindowsPerMonitor != _windowsPerMonitor)
        {
            _windowsPerMonitor = s.TaskbarWindowsPerMonitor;
            _taskView.Refresh();
        }
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
                              && (!_multiMonitor || !_windowsPerMonitor || window.HMonitor == Screen.HMonitor
                                  || (Screen.Primary && !App.Current.HasTaskbarOn(window.HMonitor)));

        if (view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.ShowInTaskbar));
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.HMonitor));
            live.IsLiveFiltering = true;
        }
        return view;
    }

    public void RefreshTasks() => _taskView.Refresh();

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
            edge = Position switch
            {
                TaskbarPosition.Top => NativeMethods.ABEdge.ABE_TOP,
                TaskbarPosition.Left => NativeMethods.ABEdge.ABE_LEFT,
                TaskbarPosition.Right => NativeMethods.ABEdge.ABE_RIGHT,
                _ => NativeMethods.ABEdge.ABE_BOTTOM,
            },
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
        DateText.Text = IsVertical ? now.ToString(ShortDateWithoutYear()) : now.ToString("d");
        ClockButton.ToolTip = now.ToString("D");
    }

    /// <summary>
    /// Fecha corta del sistema sin el año ("10/11", "11/10"…): en una barra vertical no cabe entera.
    /// </summary>
    internal static string ShortDateWithoutYear(System.Globalization.CultureInfo? culture = null)
    {
        string pattern = (culture ?? System.Globalization.CultureInfo.CurrentCulture).DateTimeFormat.ShortDatePattern;
        // Quita el año y el separador que lo une al resto ("dd/MM/yyyy", "yyyy-MM-dd", "M/d/yyyy").
        string withoutYear = System.Text.RegularExpressions.Regex.Replace(pattern, @"[^dMy]*y+[^dMy]*", "");
        return withoutYear.Length > 0 ? withoutYear : pattern;
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
            BatteryButton.ToolTip = (battery.Charging ? Loc.F("Batería: {0}% (cargando)", battery.Percent) : Loc.F("Batería: {0}%", battery.Percent));
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
        if (IsVertical)
        {
            if (TaskScroller.ScrollableHeight <= 0)
                return;
            TaskScroller.ScrollToVerticalOffset(TaskScroller.VerticalOffset - e.Delta / 2.0);
        }
        else
        {
            if (TaskScroller.ScrollableWidth <= 0)
                return;
            TaskScroller.ScrollToHorizontalOffset(TaskScroller.HorizontalOffset - e.Delta / 2.0);
        }
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
        VolumeButton.ToolTip = !audio.HasDevice ? Loc.T("Sin dispositivo de audio")
            : audio.Mute ? Loc.F("Volumen: silenciado ({0}%)", percent)
            : Loc.F("Volumen: {0}%", percent);
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
