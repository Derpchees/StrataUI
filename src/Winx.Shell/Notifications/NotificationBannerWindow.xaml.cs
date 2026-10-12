using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Winx.Shell.Services;
using Winx.Shell.Services.Notifications;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Notifications;

/// <summary>
/// Avisos emergentes de notificaciones en el monitor y la esquina elegidos en el Hub. Una sola
/// ventana sin foco (no interrumpe lo que se escribe) con hasta tres avisos apilados; cada uno se
/// cierra solo pasados unos segundos, salvo mientras el mouse está encima.
/// </summary>
public partial class NotificationBannerWindow : Window
{
    private const int MaxBanners = 3;
    private const int GapPx = 12;

    private readonly ObservableCollection<WinxNotification> _banners = new();
    private readonly Dictionary<WinxNotification, DispatcherTimer> _timers = new();

    public NotificationBannerWindow()
    {
        InitializeComponent();
        BannerList.ItemsSource = _banners;
        SourceInitialized += (_, _) =>
        {
            // Sin activar y fuera de Alt+Tab.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        };
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(PlaceInCorner, DispatcherPriority.Render);
        // Si se cambia el monitor o la esquina con avisos en pantalla, se mueven ahí.
        Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => Settings.Changed -= OnSettingsChanged;
        MouseEnter += (_, _) => { foreach (var t in _timers.Values) t.Stop(); };
        MouseLeave += (_, _) => { foreach (var t in _timers.Values) t.Start(); };
    }

    private static NotificationBannerWindow? _instance;

    /// <summary>Muestra un aviso (se crea la ventana la primera vez).</summary>
    public static void ShowBanner(WinxNotification item)
    {
        _instance ??= new NotificationBannerWindow();
        _instance.Add(item);
    }

    public static void CloseAll()
    {
        _instance?.Close();
        _instance = null;
    }

    private void OnSettingsChanged()
    {
        if (IsVisible)
            MoveToScreen();
    }

    private bool IsTop => Corner is NotificationCorner.TopLeft or NotificationCorner.TopRight;
    private bool IsLeft => Corner is NotificationCorner.TopLeft or NotificationCorner.BottomLeft;

    /// <summary>Esquina elegida para el monitor de los avisos.</summary>
    private static NotificationCorner Corner => Settings.Current.For(TargetScreen().DeviceName).NotificationCorner;

    private void Add(WinxNotification item)
    {
        // El más nuevo junto a la esquina: arriba si los avisos van arriba, abajo si van abajo.
        if (IsTop)
            _banners.Insert(0, item);
        else
            _banners.Add(item);
        // Los que sobran se quitan sin animación (Remove la espera y la lista tardaría en bajar).
        while (_banners.Count > MaxBanners)
        {
            var oldest = IsTop ? _banners[^1] : _banners[0];
            if (_timers.Remove(oldest, out var old))
                old.Stop();
            Discard(oldest);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(2, Settings.Current.NotificationSeconds)) };
        timer.Tick += (_, _) => Remove(item);
        _timers[item] = timer;
        if (!IsMouseOver)
            timer.Start();

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            MoveToScreen();
        }
        Dispatcher.BeginInvoke(() => AnimateIn(item), DispatcherPriority.Loaded);
    }

    private void Remove(WinxNotification item)
    {
        if (!_timers.Remove(item, out var timer))
            return; // ya se está yendo
        timer.Stop();
        // Sale hacia el borde con la animación elegida; después se quita de la lista.
        if (IsVisible && BannerList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement banner)
            Services.Themes.Motion.Out(banner, banner, IsLeft ? TaskbarPosition.Left : TaskbarPosition.Right, () => Discard(item));
        else
            Discard(item);
    }

    private void Discard(WinxNotification item)
    {
        _banners.Remove(item);
        if (_banners.Count == 0)
            Hide();
    }

    private void AnimateIn(WinxNotification item)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (BannerList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement banner)
            return;
        // Entra desde el borde de la pantalla más cercano, con la animación elegida en el Hub.
        Services.Themes.Motion.In(banner, banner, IsLeft ? TaskbarPosition.Left : TaskbarPosition.Right);
    }

    // ── Posición ────────────────────────────────────────────────────────────

    /// <summary>Monitor elegido; si ya no está conectado, el principal de WINX.</summary>
    private static Forms.Screen TargetScreen()
    {
        var screens = Forms.Screen.AllScreens;
        var chosen = Settings.Current.NotificationScreen;
        string device = chosen != null && screens.Any(s => s.DeviceName == chosen)
            ? chosen
            : App.MainScreenDevice(screens.Select(s => (s.DeviceName, s.Primary)));
        return screens.FirstOrDefault(s => s.DeviceName == device) ?? Forms.Screen.PrimaryScreen!;
    }

    /// <summary>
    /// Primero se lleva la ventana al monitor (WPF ajusta su escala a la de ese monitor) y luego
    /// se coloca en la esquina con su tamaño ya definitivo.
    /// </summary>
    private void MoveToScreen()
    {
        var work = TargetScreen().WorkingArea;
        SetWindowPos(Handle, IntPtr.Zero, work.Left + GapPx, work.Top + GapPx, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        Dispatcher.BeginInvoke(PlaceInCorner, DispatcherPriority.Render);
    }

    private void PlaceInCorner()
    {
        if (!IsVisible)
            return;
        var work = TargetScreen().WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = (int)Math.Ceiling(ActualWidth * scale), height = (int)Math.Ceiling(ActualHeight * scale);
        // El margen interno de la lista ya separa del borde; se suma un poco más.
        int gap = (int)(4 * scale);
        int x = IsLeft ? work.Left + gap : work.Right - width - gap;
        int y = IsTop ? work.Top + gap : work.Bottom - height - gap;
        SetWindowPos(Handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    // ── Clics ───────────────────────────────────────────────────────────────

    private void Banner_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<System.Windows.Controls.Button>(source) != null)
            return; // el clic fue en la X
        if (sender is FrameworkElement { DataContext: WinxNotification item })
        {
            Remove(item);
            if (item.OnClick != null)
                item.OnClick();
            else if (!item.IsLocal)
                NotificationService.Activate(item);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WinxNotification item })
            Remove(item);
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var current = d; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is T match)
                return match;
        return null;
    }

    // ── Win32 ───────────────────────────────────────────────────────────────

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
