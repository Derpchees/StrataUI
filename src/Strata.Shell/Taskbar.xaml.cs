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
using Strata.Shell.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using Strata.Shell.Services.Localization;
using Media = Strata.Shell.Services.Media.MediaService;

namespace Strata.Shell;

/// <summary>
/// Qué lleva una barra. Un monitor puede tener dos (bordes opuestos) que se reparten el contenido.
/// </summary>
/// <param name="HasTray">Lleva la bandeja del sistema (una sola barra: la del área de estado del monitor principal de Strata UI).</param>
/// <param name="IsMainScreen">Está en el monitor principal de Strata UI.</param>
/// <param name="ShowsApps">Inicio y ventanas abiertas.</param>
/// <param name="ShowsStatus">Área de estado y reloj.</param>
public sealed record BarRole(bool HasTray, bool IsMainScreen, bool ShowsApps = true, bool ShowsStatus = true);

public partial class Taskbar : AppBarWindow
{
    private readonly ShellManager _shell;
    private readonly bool _multiMonitor;

    private readonly ICollectionView _taskView;
    private readonly ICollectionView? _hiddenTrayView;
    private TaskbarAppsMode _appsMode;

    /// <param name="role">Lo que lleva (la bandeja ya asignada a la barra que la tiene).</param>
    /// <param name="barId">Identificador para los elementos elegidos en el Hub (<see cref="BarLayout.Id"/>).</param>
    /// <param name="defaultsRole">El papel de siempre, para los elementos por defecto.</param>
    public Taskbar(ShellManager shell, AppBarScreen screen, bool multiMonitor, TaskbarPosition position, BarRole role,
                   string barId, BarRole defaultsRole)
        : base(shell.AppBarManager, shell.ExplorerHelper, shell.FullScreenHelper,
               screen, EdgeOf(position), AppBarMode.Normal, ReservedSize(IsVerticalPosition(position), screen.DeviceName))
    {
        _shell = shell;
        _multiMonitor = multiMonitor;
        Position = position;
        Role = role;
        BarId = barId;
        _defaultsRole = defaultsRole;
        IsMain = role.HasTray;
        InitializeComponent();
        if (IsVertical)
        {
            DesiredWidth = ReservedSize(vertical: true, Device);
            Width = DesiredWidth;
        }
        ApplyOrientation();
        Services.Themes.Motion.Attach(HiddenTrayPopup, () => Position);
        WatchItemDrag();
        WatchItemMenu();
        StartButton.ContextMenu = StartMenu.WinXMenu.Create(StartButton);

        VersionItem.Header = $"Strata UI v{App.Version}";
        _appsMode = WindowsTaskbarPrefs.AppsMode(Settings.Current);
        _combine = S.CombineTaskWindows;
        _taskView = CreateTaskView();
        WatchWindows();
        TaskList.ItemsSource = _taskView;
        WatchPins();

        // El texto del tooltip del reloj se arma al abrirlo (fecha y notificaciones pendientes).
        ClockButton.ToolTip = " ";
        ClockButton.ToolTipOpening += (_, _) => ClockButton.ToolTip = ClockToolTip();
        Services.Notifications.NotificationService.Changed += UpdateNotificationBadge;

        if (IsMain)
        {
            var tray = shell.NotificationArea;
            PinnedTray.ItemsSource = tray.PinnedIcons;
            _hiddenTrayView = TrayPromotion.CreateHiddenView(tray);
            HiddenTray.ItemsSource = _hiddenTrayView;
            TrayIcon.DragStarted += OnTrayDragStarted;
            ((INotifyCollectionChanged)tray.UnpinnedIcons).CollectionChanged += (_, _) => UpdateChevron();
            UpdateChevron();
            LocationChanged += (_, _) => ReportTrayPosition();
            SizeChanged += (_, _) => ReportTrayPosition();
        }
        else
        {
            TrayArea.Visibility = Visibility.Collapsed;
        }

        ClockTick += UpdateClock;
        StartSharedClock();
        SystemStatus.Changed += UpdateStatus;

        Settings.Changed += ApplySettings;
        ApplySettings();

        App.Current.Audio.Changed += UpdateVolume;
        UpdateVolume();
        Services.Brightness.BrightnessService.Changed += ApplySettings;
        Media.Changed += UpdateMedia;
        TaskButton.DragStarted += OnTaskDragStarted;
        TaskOrder.Changed += OnTaskOrderChanged;

        SourceInitialized += (_, _) => ApplyBackdrop();
        SizeChanged += (_, _) => ApplyBackdrop();
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Borde en el que está la barra (cambiarlo recrea la barra).</summary>
    public TaskbarPosition Position { get; }

    /// <summary>Monitor de la barra ("\\.\DISPLAY1").</summary>
    public string Device => Screen.DeviceName;

    /// <summary>Ajustes de su monitor (Hub › Personalización por monitor).</summary>
    private ScreenSettings S => Settings.Current.For(Device);

    /// <summary>Tema de su monitor.</summary>
    private ThemeInfo BarTheme => ThemeManager.ThemeFor(Device);

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
        double size = ReservedSize(IsVertical, Device);
        if ((IsVertical ? DesiredWidth : DesiredHeight) == size)
            return;
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
    /// Grosor reservado en pantalla: la barra (tamaño elegido para su monitor) más el margen si
    /// flota. Las verticales son algo más anchas, para que quepan el reloj y la fecha.
    /// </summary>
    public static double ReservedSize(bool vertical, string? device) =>
        Settings.Current.For(device).TaskbarHeight + (vertical ? VerticalExtra : 0)
        + (ThemeManager.ThemeFor(device).Floating ? ThemeCatalog.FloatingMargin : 0);

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

        MainGrid.Orientation = Orientation.Vertical;
        StartZonePanel.Orientation = Orientation.Vertical;
        CenterZonePanel.Orientation = Orientation.Vertical;

        TaskArea.HorizontalAlignment = HorizontalAlignment.Stretch;
        DockPanel.SetDock(StartButton, Dock.Top);
        StartButton.Width = double.NaN;
        StartButton.Height = 44;
        TaskScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        TaskScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        TaskList.ItemsPanel = Panel(typeof(StackPanel));
        PinnedList.ItemsPanel = Panel(typeof(StackPanel));
        AppsStack.Orientation = Orientation.Vertical;

        StatusPanel.Orientation = Orientation.Vertical;
        TrayArea.Orientation = Orientation.Vertical;
        PinnedTray.ItemsPanel = Panel(typeof(WrapPanel));
        PinnedTray.HorizontalAlignment = HorizontalAlignment.Center;
        // Multimedia en vertical: la carátula y debajo los controles, en columna.
        MediaText.Visibility = Visibility.Collapsed;
        MediaButton.Padding = new Thickness(0, 4, 0, 4);
        MediaModule.Orientation = Orientation.Vertical;
        MediaControls.Orientation = Orientation.Vertical;
        MediaControls.Margin = new Thickness(0, 0, 0, 4);
        MediaControls.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var control in new FrameworkElement[] { MediaPrevious, MediaPlay, MediaNext })
            control.Height = 28;
        MediaButton.HorizontalAlignment = HorizontalAlignment.Center;
        MediaButton.Height = 36;
        // Los módulos de estado van juntos también en vertical: más anchos que altos y sin separación.
        BatteryContent.Orientation = Orientation.Vertical;
        foreach (var button in new FrameworkElement[] { ChevronButton, NetworkButton, BluetoothButton, BrightnessButton, VolumeButton, BatteryButton, FreeRamButton })
        {
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Width = 36;
            button.Height = 30;
            button.Margin = new Thickness(4, 0, 4, 0);
        }
        // Medidores: dos líneas centradas, del ancho de la barra.
        foreach (var meter in new Control[] { CpuButton, GpuButton, RamButton, FpsButton })
        {
            meter.HorizontalAlignment = HorizontalAlignment.Stretch;
            meter.Margin = new Thickness(4, 1, 4, 1);
            meter.Padding = new Thickness(2, 4, 2, 4);
        }
        ClockButton.Padding = new Thickness(2, 6, 2, 6);
        ClockContent.Orientation = Orientation.Vertical;
        NotificationBadge.Margin = new Thickness(0, 4, 0, 0);
        NotificationBadge.HorizontalAlignment = HorizontalAlignment.Center;
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
        if (!BarTheme.Floating)
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
        if (ApplyTransparency())
            return;
        var theme = BarTheme;
        // Con refracción en vivo el shader pinta el vidrio; el desenfoque de DWM queda debajo
        // solo para suavizar el borde antialiasado.
        WindowEffects.SetBlur(this, theme.Backdrop);
        var margin = FloatingMargin();
        BarBorder.Margin = margin;
        // Línea hacia el interior de la pantalla, o cápsula con radio de la mitad del grosor.
        double thickness = IsVertical ? ActualWidth - margin.Left - margin.Right : ActualHeight - margin.Top - margin.Bottom;
        Resources["StrataTaskbarCornerRadius"] = new CornerRadius(theme.Floating && thickness > 0 ? thickness / 2 : 0);
        Resources["StrataTaskbarBorderThickness"] = theme.Floating ? new Thickness(1) : Position switch
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
        bool wanted = ThemeManager.UseLiveGlassFor(Device) && !_transparent;
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
            _glass.CornerRadius = ((CornerRadius)Resources["StrataTaskbarCornerRadius"]).TopLeft;
            _glass.SetLook(ThemeManager.IsLightFor(Device));
        }
    }

    // WPF-UI termina de aplicar su tema de forma asíncrona sobre la ventana principal (la barra)
    // y deshace el efecto: se reaplica cuando la app queda inactiva.
    private void OnThemeChanged()
    {
        ApplyScreenPalette();
        Dispatcher.BeginInvoke(ApplyBackdrop, DispatcherPriority.ApplicationIdle);
    }

    private ResourceDictionary? _screenPalette;
    private (StrataTheme, bool, int) _appliedLook;

    /// <summary>
    /// Si su monitor tiene otro tema (o tamaño) que el general, la barra usa sus propios colores;
    /// si cambió el tema, también su material de fondo.
    /// </summary>
    private void ApplyScreenPalette()
    {
        var look = (BarTheme.Kind, ThemeManager.IsLightFor(Device), S.TaskbarHeight);
        var palette = ThemeManager.PaletteFor(Device);
        if (_screenPalette != null)
            Resources.MergedDictionaries.Remove(_screenPalette);
        _screenPalette = palette;
        if (palette != null)
            Resources.MergedDictionaries.Add(palette);
        if (look != _appliedLook && _appliedLook != default)
            Dispatcher.BeginInvoke(ApplyBackdrop, DispatcherPriority.ApplicationIdle);
        _appliedLook = look;
    }

    private void ApplySettings()
    {
        var s = Settings.Current;
        if (StartButton.Tag is not StartIconKind current || current != s.StartIcon)
        {
            StartButton.Content = StartIcons.Create(s.StartIcon);
            StartButton.Tag = s.StartIcon;
        }
        ApplyAlignment(WindowsTaskbarPrefs.Alignment(S) == TaskbarAlignment.Left, S.TaskbarCenterApps);
        var appsMode = WindowsTaskbarPrefs.AppsMode(s);
        if (appsMode != _appsMode)
        {
            _appsMode = appsMode;
            _taskView.Refresh();
            RefreshPinLeaders();
        }
        ApplyCombine(S.CombineTaskWindows);
        ApplyScreenPalette();
        Resize();
        ApplyLayout();
        DateText.Visibility = S.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        BrightnessButton.Visibility = Services.Brightness.BrightnessService.Available ? Visibility.Visible : Visibility.Collapsed;
        RescheduleClock();
        UpdateClock();
        UpdateStatus();
        UpdateNotificationBadge();
        UpdateMedia();
    }

    /// <summary>
    /// "Izquierda" pone Inicio y las apps juntos al principio de la barra (arriba si es vertical);
    /// "Centro", juntos al centro. Con "centrar apps", Inicio queda al principio y las apps al
    /// centro del espacio que sobra.
    /// </summary>
    private void ApplyAlignment(bool left, bool centerApps)
    {
        bool split = left && centerApps;
        // Lo del centro se agrupa con las ventanas cuando van centradas (ver BarZonesPanel).
        MainGrid.AppsCentered = !left;
        if (IsVertical)
        {
            TaskArea.VerticalAlignment = split ? VerticalAlignment.Stretch : left ? VerticalAlignment.Top : VerticalAlignment.Center;
            AppsStack.VerticalAlignment = split ? VerticalAlignment.Center : VerticalAlignment.Top;
        }
        else
        {
            TaskArea.HorizontalAlignment = split ? HorizontalAlignment.Stretch : left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            AppsStack.HorizontalAlignment = split ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
    }

    /// <summary>Re-lee las preferencias de Windows que se siguen (alineación, apps por monitor).</summary>
    public void RefreshFromWindows()
    {
        ApplySettings();
        ScheduleContrast(); // puede haber cambiado el fondo de pantalla
    }

    private ICollectionView CreateTaskView()
    {
        var view = _shell.Tasks.CreateGroupedWindowsCollection();
        // Orden elegido arrastrando: las que ya estaban conservan el orden en que llegaron.
        if (view is ListCollectionView list)
        {
            TaskOrder.Instance.Seed(_shell.Tasks.GroupedWindows.OfType<ApplicationWindow>());
            list.CustomSort = TaskOrder.Instance;
        }
        view.Filter = item => item is ApplicationWindow window && ShowsWindow(window) && !InPinnedButton(window) && LeadsGroup(window);

        if (view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.ShowInTaskbar));
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.HMonitor));
            live.IsLiveFiltering = true;
        }
        return view;
    }

    public void RefreshTasks()
    {
        _taskView.Refresh();
        RefreshPinLeaders();
    }

    /// <summary>
    /// Si esta barra muestra la ventana, según el modo (como en Windows): en todas, en la principal y
    /// en la de su monitor, o solo en la de su monitor. Las ventanas de un monitor sin barra van a la principal.
    /// </summary>
    private bool Shows(ApplicationWindow window)
    {
        if (!_multiMonitor || _appsMode == TaskbarAppsMode.AllTaskbars || window.HMonitor == Screen.HMonitor)
            return true;
        return Role.IsMainScreen && (_appsMode == TaskbarAppsMode.MainAndOwn || !App.Current.HasTaskbarOn(window.HMonitor));
    }

    private void UpdateChevron() =>
        ChevronButton.Visibility = _shell.NotificationArea.UnpinnedIcons
            .OfType<ShellNotifyIcon>().Any(icon => !TrayIcon.IsReplacedByStrata(icon))
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

    // ── Reloj compartido ────────────────────────────────────────────────────
    // Un solo temporizador para todas las barras, alineado al cambio de minuto (o de segundo si se
    // muestran): sin segundos, Strata UI despierta una vez por minuto en lugar de cada segundo.

    private static DispatcherTimer? _sharedClock;
    private static event Action? ClockTick;

    private static void StartSharedClock()
    {
        if (_sharedClock != null)
            return;
        _sharedClock = new DispatcherTimer(DispatcherPriority.Normal);
        _sharedClock.Tick += (_, _) =>
        {
            ScheduleNextTick();
            ClockTick?.Invoke();
        };
        ScheduleNextTick();
        _sharedClock.Start();
    }

    private static void ScheduleNextTick()
    {
        var now = DateTime.Now;
        var next = AnyShowsSeconds()
            ? now.AddTicks(TimeSpan.TicksPerSecond - now.Ticks % TimeSpan.TicksPerSecond)
            : now.AddTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
        // Unos milisegundos de margen para no caer justo antes del cambio.
        _sharedClock!.Interval = next - now + TimeSpan.FromMilliseconds(30);
    }

    /// <summary>El reloj compartido marca segundos si alguna barra los muestra.</summary>
    private static bool AnyShowsSeconds() =>
        App.Current.Taskbars.Any(bar => Settings.Current.For(bar.Device).ShowSeconds);

    /// <summary>Al mostrar u ocultar los segundos cambia el ritmo del reloj.</summary>
    private static void RescheduleClock()
    {
        if (_sharedClock is null)
            return;
        _sharedClock.Stop();
        ScheduleNextTick();
        _sharedClock.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString(S.ShowSeconds ? "T" : "t");
        DateText.Text = IsVertical ? now.ToString(ShortDateWithoutYear()) : now.ToString("d");
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

        var battery = Has(BarItem.Battery) ? SystemStatus.GetBattery() : null;
        BatteryButton.Visibility = battery is null ? Visibility.Collapsed : Visibility.Visible;
        if (battery != null)
        {
            // Con porcentaje: el número dentro del contorno y un rayo si carga. Sin él: el ícono
            // de Windows con su nivel (y el rayo incluido si carga).
            bool percent = S.ShowBatteryPercent;
            BatteryGlyph.Visibility = percent ? Visibility.Collapsed : Visibility.Visible;
            BatteryPercentIcon.Visibility = percent ? Visibility.Visible : Visibility.Collapsed;
            BatteryBolt.Visibility = percent && battery.Charging ? Visibility.Visible : Visibility.Collapsed;
            BatteryGlyph.Text = battery.Glyph.ToString();
            BatteryPercentText.Text = battery.Percent.ToString();
            if (percent)
                PaintBatteryLevel(battery);
            BatteryButton.ToolTip = BatteryText(battery);
        }
    }

    protected override void CustomClosing()
    {
        StopContrast();
        UnwatchWindows();
        UnwatchPins();
        Settings.Changed -= ApplySettings;
        App.Current.Audio.Changed -= UpdateVolume;
        Services.Brightness.BrightnessService.Changed -= ApplySettings;
        Media.Changed -= UpdateMedia;
        Services.Notifications.NotificationService.Changed -= UpdateNotificationBadge;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        TrayIcon.DragStarted -= OnTrayDragStarted;
        TaskButton.DragStarted -= OnTaskDragStarted;
        TaskOrder.Changed -= OnTaskOrderChanged;
        if (_hiddenTrayView != null)
            TrayPromotion.ReleaseHiddenView(_hiddenTrayView);
        ClockTick -= UpdateClock;
        SystemStatus.Changed -= UpdateStatus;
        if (_metersOn)
        {
            _metersOn = false;
            SystemMonitor.Unsubscribe();
            SystemMonitor.Changed -= UpdateMeters;
            Sensors.Changed -= UpdateMeters;
        }
        base.CustomClosing();
    }

    private static void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    // Mientras no existan los paneles propios (próximas versiones), se abren los de Windows.

    /// <summary>Botón Inicio (lo usa también la tecla Windows para anclar el menú).</summary>
    public FrameworkElement StartAnchor => StartButton;

    /// <summary>Menú del clic derecho en Inicio, abierto con el teclado (Win+X).</summary>
    public void ShowWinXMenu()
    {
        if (StartButton.ContextMenu is { } menu)
            StartMenu.WinXMenu.ShowAt(menu, StartButton, Position);
    }

    /// <summary>Lleva la bandeja del sistema (la del área de estado del monitor principal de Strata UI).</summary>
    public bool IsMain { get; }

    public BarRole Role { get; }

    /// <summary>Identificador de la barra para los elementos elegidos en el Hub.</summary>
    public string BarId { get; }

    private readonly BarRole _defaultsRole;
    private List<BarItem> _items = new();
    private List<BarZone> _zones = new();

    /// <summary>Paneles de las zonas de la barra (inicio, centro y final, donde va "mostrar escritorio").</summary>
    private IEnumerable<StackPanel> ZonePanels => [StartZonePanel, CenterZonePanel, StatusPanel];

    private StackPanel ZonePanel(BarZone zone) => zone switch
    {
        BarZone.Start => StartZonePanel,
        BarZone.Center => CenterZonePanel,
        _ => StatusPanel,
    };

    /// <summary>Zona en que está un elemento de esta barra.</summary>
    public BarZone ZoneOf(BarItem item) => _items.IndexOf(item) is var i and >= 0 ? _zones[i] : BarZone.End;
    private bool _metersOn;

    /// <summary>Elementos que lleva esta barra, en orden.</summary>
    public IReadOnlyList<BarItem> Items => _items;

    public bool Has(BarItem item) => _items.Contains(item);

    /// <summary>
    /// Pone en el área de estado los elementos elegidos para esta barra, en su orden (los demás
    /// salen del panel). Inicio va siempre delante de las ventanas. La bandeja solo puede ir en
    /// la barra que la recibe (<see cref="IsMain"/>).
    /// </summary>
    private void ApplyLayout()
    {
        var items = BarLayout.ItemsFor(BarId, _defaultsRole, Settings.Current);
        if (!IsMain)
            items.Remove(BarItem.Tray);
        else if (!items.Contains(BarItem.Tray))
            items.Insert(items.Contains(BarItem.Start) ? 1 : 0, BarItem.Tray);
        var zones = items.Select(item => BarLayout.ZoneOf(BarId, item, Settings.Current)).ToList();
        if (_items.SequenceEqual(items) && _zones.SequenceEqual(zones) && StatusPanel.Children.Count > 0)
            return;
        _items = items;
        _zones = zones;

        StartButton.Visibility = Has(BarItem.Start) ? Visibility.Visible : Visibility.Collapsed;
        TaskArea.Visibility = Role.ShowsApps || Has(BarItem.Start) ? Visibility.Visible : Visibility.Collapsed;
        TaskScroller.Visibility = Role.ShowsApps ? Visibility.Visible : Visibility.Collapsed;

        // Cada elemento en su zona, en el orden de la lista; "mostrar escritorio" siempre al final.
        foreach (var panel in ZonePanels)
            panel.Children.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            if (Element(items[i]) is { } element)
                ZonePanel(zones[i]).Children.Add(element);
        }
        bool any = ZonePanels.Any(panel => panel.Children.Count > 0);
        if (any)
            StatusPanel.Children.Add(ShowDesktopArea);
        foreach (var panel in ZonePanels)
            panel.Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_showingDropZones)
            ShowDropZones(true); // se está arrastrando un elemento: las zonas vacías siguen a la vista

        bool meters = Has(BarItem.Cpu) || Has(BarItem.Gpu) || Has(BarItem.Ram) || Has(BarItem.Fps);
        // Temperaturas y FPS: con el permiso ya dado, las lecturas empiezan solas (sin preguntar).
        if (Has(BarItem.Cpu) || Has(BarItem.Gpu) || Has(BarItem.Fps))
            Sensors.StartQuietly();
        if (meters != _metersOn)
        {
            _metersOn = meters;
            if (meters)
            {
                SystemMonitor.Subscribe();
                SystemMonitor.Changed += UpdateMeters;
                Sensors.Changed += UpdateMeters;
            }
            else
            {
                SystemMonitor.Unsubscribe();
                SystemMonitor.Changed -= UpdateMeters;
                Sensors.Changed -= UpdateMeters;
            }
        }
        UpdateMeters();
        ScheduleContrast();
    }

    private FrameworkElement? Element(BarItem item) => item switch
    {
        BarItem.Media => MediaModule,
        BarItem.Tray => TrayArea,
        BarItem.Network => NetworkButton,
        BarItem.Bluetooth => BluetoothButton,
        BarItem.Brightness => BrightnessButton,
        BarItem.Volume => VolumeButton,
        BarItem.Battery => BatteryButton,
        BarItem.Cpu => CpuButton,
        BarItem.Gpu => GpuButton,
        BarItem.Ram => RamButton,
        BarItem.Fps => FpsButton,
        BarItem.FreeRam => FreeRamButton,
        BarItem.Clock => ClockButton,
        _ => null, // Inicio va con las ventanas
    };

    // ── Medidores ───────────────────────────────────────────────────────────

    private void UpdateMeters()
    {
        if (!_metersOn)
            return;
        bool temps = S.ShowTemperatures;
        // Sin dato, "-- °C" (con la temperatura activada siempre se ve, también en vertical) y el
        // tooltip dice por qué: lecturas detenidas o, en la CPU, falta PawnIO.
        string why = !Sensors.Running ? "\n" + Loc.T("Clic en el contador de FPS (o Hub › Barra de tareas) para activar temperaturas y FPS.")
            : Sensors.MissingPawnIo ? "\n" + Loc.T("La temperatura de la CPU necesita PawnIO: instálalo desde Hub › Barra de tareas.")
            : "\n" + Loc.T("Este equipo no informa esa temperatura.");
        const string NoTemp = "-- °C";

        // En vertical no cabe "RAM 87%" en una línea: nombre arriba y el número abajo.
        string Value(string name, double? percent) =>
            IsVertical ? percent is { } p ? $"{Math.Round(p)}%" : "—"
            : percent is { } q ? Loc.F("{0} {1}%", name, Math.Round(q)) : name;
        CpuLabel.Text = GpuLabel.Text = RamLabel.Text = "";
        if (IsVertical)
        {
            CpuLabel.Text = "CPU";
            GpuLabel.Text = "GPU";
            RamLabel.Text = "RAM";
        }

        CpuValue.Text = Value("CPU", SystemMonitor.CpuPercent);
        CpuDetail.Text = !temps ? "" : Sensors.CpuTemperature is { } cpuTemp ? $"{cpuTemp:0} °C" : NoTemp;
        // El modelo arriba ("AMD Ryzen 7 7840HS…"); con dos gráficas, las dos (el medidor toma la más ocupada).
        CpuButton.ToolTip = (HardwareInfo.CpuName is { } cpuName ? cpuName + "\n" : "")
                            + Loc.T("Uso de la CPU") + (temps && Sensors.CpuTemperature is null ? why : "");

        GpuValue.Text = Value("GPU", SystemMonitor.GpuPercent);
        GpuDetail.Text = !temps ? "" : Sensors.GpuTemperature is { } gpuTemp ? $"{gpuTemp:0} °C" : NoTemp;
        GpuButton.ToolTip = string.Concat(HardwareInfo.GpuNames.Select(name => name + "\n"))
                            + Loc.T("Uso de la GPU") + (temps && Sensors.GpuTemperature is null ? why : "");

        RamValue.Text = Value("RAM", SystemMonitor.RamTotal > 0 ? SystemMonitor.RamPercent : null);
        // En uso / total ("9.2/32 GB"): el total redondeado, como se anuncia la memoria instalada.
        RamDetail.Text = SystemMonitor.RamTotal > 0
            ? string.Create(System.Globalization.CultureInfo.CurrentCulture,
                $"{SystemMonitor.RamUsed / 1073741824.0:0.0}/{Math.Round(SystemMonitor.RamTotal / 1073741824.0):0} GB")
            : "";
        RamButton.ToolTip = SystemMonitor.RamTotal > 0
            ? Loc.F("Memoria en uso: {0:0.0} de {1:0.0} GB", SystemMonitor.RamUsed / 1073741824.0, SystemMonitor.RamTotal / 1073741824.0)
            : Loc.T("Memoria RAM");

        if (Has(BarItem.Fps))
        {
            var target = Sensors.TargetProcess(Settings.Current.FpsScreen);
            var fps = target is { } pid ? Sensors.FpsOf(pid) : null;
            // Con generación de cuadros: arriba los que llegan a la pantalla y abajo los reales.
            FpsValue.Text = Sensors.Starting ? "…" : fps?.Shown.ToString() ?? "—";
            FpsDetail.Text = fps is { Generated: true } generated ? Loc.F("{0} reales", generated.Real) : "FPS";
            FpsButton.ToolTip = !Sensors.Running ? Loc.T("Clic para empezar a contar los FPS (pide permiso de administrador).")
                : fps is null ? Loc.T("La app de ese monitor no está dibujando con la GPU.")
                : fps.Value.Generated
                    ? Loc.F("FPS de {0}", Sensors.ProcessName(target!.Value)) + "\n"
                      + Loc.F("Con generación de cuadros ({0}): {1} FPS", fps.Value.FrameGeneration ?? Loc.T("tipo desconocido"), fps.Value.Shown) + "\n"
                      + Loc.F("Sin generación de cuadros: {0} FPS", fps.Value.Real)
                    : Loc.F("FPS de {0}", Sensors.ProcessName(target!.Value));
        }
    }

    private void Meter_Click(object sender, RoutedEventArgs e) => ShellHelper.StartTaskManager();

    private async void Fps_Click(object sender, RoutedEventArgs e)
    {
        if (!Sensors.Running)
            await Sensors.StartAsync();
    }

    /// <summary>
    /// Libera la RAM y lo dice junto a la barra (aviso en pantalla, como el volumen): al empezar,
    /// mientras Windows pide permiso, y al terminar con cuánto se liberó.
    /// </summary>
    private async void FreeRam_Click(object sender, RoutedEventArgs e)
    {
        string working = ((char)0xE895).ToString(); // Sync
        string done = ((char)0xE73E).ToString();    // CheckMark
        FreeRamButton.IsEnabled = false;
        FreeRamButton.Opacity = 0.5;
        Notifications.OsdWindow.ShowMessage(working, Loc.T("Liberando RAM…"), Device);
        try
        {
            ulong before = SystemMonitor.AvailableRam();
            bool ok = await Sensors.FreeRamAsync();
            if (!ok)
            {
                Notifications.OsdWindow.ShowMessage(((char)0xE711).ToString(), Loc.T("No se liberó la RAM"), Device); // Cancel
                return;
            }
            ulong after = SystemMonitor.AvailableRam();
            double freed = after > before ? (after - before) / 1073741824.0 : 0;
            Log.Info($"RAM liberada: {freed:0.00} GB");
            Notifications.OsdWindow.ShowMessage(done,
                freed >= 0.05 ? Loc.F("RAM liberada: {0:0.0} GB", freed) : Loc.T("La memoria ya estaba libre"), Device);
            FreeRamButton.ToolTip = Loc.F("Última vez: {0:0.0} GB liberados", freed);
        }
        finally
        {
            FreeRamButton.IsEnabled = true;
            FreeRamButton.Opacity = 1;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) =>
        StartMenu.StartMenuLauncher.Toggle(StartButton);

    private void ClockButton_Click(object sender, RoutedEventArgs e) => ToggleNotificationCenter();

    /// <summary>Centro de notificaciones de Strata UI junto al reloj (o el de Windows si está desactivado).</summary>
    public void ToggleNotificationCenter()
    {
        if (Settings.Current.NotificationsEnabled)
            Flyouts.FlyoutManager.Toggle<Flyouts.NotificationFlyout>(ClockButton);
        else
            ShellHelper.ShowNotificationCenter();
    }

    private void UpdateNotificationBadge()
    {
        int count = Services.Notifications.NotificationService.Items.Count;
        NotificationBadge.Visibility = count > 0 && Settings.Current.NotificationsEnabled ? Visibility.Visible : Visibility.Collapsed;
        NotificationCount.Text = count > 99 ? "99+" : count.ToString();
    }

    private static string ClockToolTip()
    {
        int count = Services.Notifications.NotificationService.Items.Count;
        string date = DateTime.Now.ToString("D");
        return count > 0 ? date + "\n" + Loc.F("{0} notificaciones", count) : date;
    }

    private void NetworkButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.NetworkFlyout>(NetworkButton);

    private void BluetoothButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.BluetoothFlyout>(BluetoothButton);

    private void VolumeButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.VolumeFlyout>(VolumeButton);

    // ── Multimedia ──────────────────────────────────────────────────────────

    private void UpdateMedia()
    {
        var s = Settings.Current;
        bool show = Has(BarItem.Media) && Media.HasSession;
        MediaModule.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;
        MediaCover.ImageSource = Media.Cover;
        MediaAppBadge.Source = Media.AppIcon;
        MediaTitle.Text = Media.Title;
        // Segunda línea: artista y álbum (lo que haya).
        string details = string.Join(" · ", new[] { Media.Artist, Media.Album }.Where(t => t.Length > 0).Distinct());
        MediaArtist.Text = details;
        MediaArtist.Visibility = details.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MediaButton.ToolTip = string.Join("\n", new[] { Media.Title, Media.Artist, Media.Album, Media.AppName }.Where(t => t.Length > 0));
        MediaButton.Opacity = Media.IsPlaying ? 1 : 0.6;
        MediaControls.Visibility = S.ShowMediaControls ? Visibility.Visible : Visibility.Collapsed;
        MediaPlayGlyph.Text = Media.IsPlaying ? "" : ""; // pausa / reproducir
        MediaPlay.ToolTip = Media.IsPlaying ? Loc.T("Pausar") : Loc.T("Reproducir");
        MediaPrevious.IsEnabled = Media.CanPrevious;
        MediaNext.IsEnabled = Media.CanNext;
    }

    private void MediaPrevious_Click(object sender, RoutedEventArgs e) => Media.Previous();

    private void MediaPlay_Click(object sender, RoutedEventArgs e) => Media.PlayPause();

    private void MediaNext_Click(object sender, RoutedEventArgs e) => Media.Next();

    private void MediaButton_Click(object sender, RoutedEventArgs e) => Media.ShowSourceApp();

    private void MediaButton_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            Media.PlayPause();
            e.Handled = true;
        }
    }

    private void BrightnessButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.BrightnessFlyout>(BrightnessButton);

    /// <summary>La rueda sobre el botón cambia el brillo del monitor de esta barra.</summary>
    private void BrightnessButton_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var center = BrightnessButton.PointToScreen(new Point(BrightnessButton.ActualWidth / 2, BrightnessButton.ActualHeight / 2));
        if (Services.Brightness.BrightnessService.MonitorAt(new System.Drawing.Point((int)center.X, (int)center.Y)) is { } monitor)
        {
            monitor.Percent += e.Delta > 0 ? 5 : -5;
            BrightnessButton.ToolTip = Loc.F("Brillo: {0}%", monitor.Percent);
        }
        e.Handled = true;
    }

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

    /// <summary>Relleno tenue del nivel, del color del texto como el resto de los íconos.</summary>
    private void PaintBatteryLevel(SystemStatus.BatteryInfo battery)
    {
        const double inner = 25 - 4; // ancho del cuerpo menos el margen del relleno
        BatteryLevelFill.Width = Math.Max(1, inner * Math.Clamp(battery.Percent, 0, 100) / 100.0);
    }

    private void BatteryButton_Click(object sender, RoutedEventArgs e) =>
        Flyouts.FlyoutManager.Toggle<Flyouts.BatteryFlyout>(BatteryButton);

    /// <summary>"Batería: 80 % (cargando)", "Batería: 45 %, quedan 2 h 10 min"…</summary>
    internal static string BatteryText(SystemStatus.BatteryInfo battery) =>
        battery.Charging ? Loc.F("Batería: {0}% (cargando)", battery.Percent)
        : battery.PluggedIn ? Loc.F("Batería: {0}% (conectada)", battery.Percent)
        : battery.Remaining is { } left ? Loc.F("Batería: {0}%, quedan {1}", battery.Percent, Duration(left))
        : Loc.F("Batería: {0}%", battery.Percent);

    internal static string Duration(TimeSpan time) =>
        time.TotalHours >= 1 ? Loc.F("{0} h {1} min", (int)time.TotalHours, time.Minutes) : Loc.F("{0} min", Math.Max(1, time.Minutes));

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

    private void RestartStrata_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell(restart: true);

    private void ExitStrata_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell();
}
