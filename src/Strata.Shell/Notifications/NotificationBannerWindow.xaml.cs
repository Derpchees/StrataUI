using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Notifications;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Notifications;

/// <summary>
/// Avisos emergentes de notificaciones en el monitor y la esquina elegidos en el Hub. Una sola
/// ventana sin foco (no interrumpe lo que se escribe) con hasta tres avisos apilados; cada uno se
/// cierra solo pasados unos segundos, salvo mientras el mouse está encima.
/// </summary>
public partial class NotificationBannerWindow : Window
{
    private const int MaxBanners = 3;
    private const int GapPx = 12;

    private readonly ObservableCollection<StrataNotification> _banners = new();
    private readonly Dictionary<StrataNotification, DispatcherTimer> _timers = new();

    public NotificationBannerWindow()
    {
        InitializeComponent();
        BannerList.ItemsSource = _banners;
        SourceInitialized += (_, _) =>
        {
            // Sin activar y fuera de Alt+Tab.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(PlaceInCorner, DispatcherPriority.Render);
        // Vidrio detrás de cada aviso con los temas translúcidos (también al cambiar de tema).
        SourceInitialized += (_, _) => ApplyGlass();
        // Cada aviso nuevo toma el aspecto (con o sin vidrio) al aparecer su tarjeta.
        BannerList.LayoutUpdated += (_, _) =>
        {
            int count = Cards().Count();
            if (count == _styledCards)
                return;
            _styledCards = count;
            StyleCards();
        };
        Services.ThemeManager.ThemeChanged += ApplyGlass;
        Closed += (_, _) => Services.ThemeManager.ThemeChanged -= ApplyGlass;
        // Si se cambia el monitor o la esquina con avisos en pantalla, se mueven ahí.
        Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => Settings.Changed -= OnSettingsChanged;
        MouseEnter += (_, _) => { foreach (var t in _timers.Values) t.Stop(); };
        MouseLeave += (_, _) => { foreach (var t in _timers.Values) t.Start(); };
    }

    private static NotificationBannerWindow? _instance;

    /// <summary>Muestra un aviso (se crea la ventana la primera vez).</summary>
    public static void ShowBanner(StrataNotification item)
    {
        _instance ??= new NotificationBannerWindow();
        _instance.Add(item);
        if (Settings.Current.NotificationSounds)
            Services.Notifications.NotificationSound.Play();
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

    private static bool IsTop => Corner.IsTop();

    /// <summary>Borde desde el que entran y hacia el que salen: el lateral más cercano, o arriba/abajo en el centro.</summary>
    private static TaskbarPosition Edge => Corner switch
    {
        NotificationCorner.TopCenter => TaskbarPosition.Top,
        NotificationCorner.BottomCenter => TaskbarPosition.Bottom,
        var corner => corner.IsLeft() ? TaskbarPosition.Left : TaskbarPosition.Right,
    };

    /// <summary>Esquina elegida para el monitor de los avisos.</summary>
    private static NotificationCorner Corner => Settings.Current.For(TargetScreen().DeviceName).NotificationCorner;

    private int _styledCards;

    /// <summary>
    /// Vidrio con los temas translúcidos (Themes/GlassSurface): la ventana mide lo que los avisos,
    /// apilados sin márgenes como un solo panel; Windows redondea sus esquinas. Si no, tarjetas
    /// separadas con su margen.
    /// </summary>
    private void ApplyGlass()
    {
        StyleCards();
        Services.Themes.GlassSurface.Apply(this);
    }

    private void StyleCards()
    {
        bool glass = Services.Themes.GlassSurface.Active;
        BannerList.Margin = new Thickness(glass ? 0 : 8);
        foreach (var card in Cards().OfType<System.Windows.Controls.Border>())
        {
            card.Margin = glass ? new Thickness(0, 0, 0, 1) : new Thickness(0, 4, 0, 4);
            card.BorderThickness = new Thickness(glass ? 0 : 1);
            if (glass)
                card.CornerRadius = new CornerRadius(0);
            else
                card.SetResourceReference(System.Windows.Controls.Border.CornerRadiusProperty, "StrataLargeCornerRadius");
        }
    }

    /// <summary>La tarjeta de cada aviso en pantalla.</summary>
    private IEnumerable<FrameworkElement> Cards()
    {
        var stack = new Stack<DependencyObject>([BannerList]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is System.Windows.Controls.Border { Name: "Card" } card)
            {
                yield return card;
                continue;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                stack.Push(VisualTreeHelper.GetChild(current, i));
        }
    }

    private void Add(StrataNotification item)
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

    private void Remove(StrataNotification item)
    {
        if (!_timers.Remove(item, out var timer))
            return; // ya se está yendo
        timer.Stop();
        // Sale hacia el borde con la animación elegida; después su hueco se cierra poco a poco (los
        // demás se acomodan deslizándose, no de golpe) y se quita de la lista.
        if (IsVisible && BannerList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement banner)
            Services.Themes.Motion.Out(banner, banner, Edge, () => Collapse(banner, () => Discard(item)), travel: 4);
        else
            Discard(item);
    }

    private static bool Animated => Settings.Current.PanelAnimation != PanelAnimation.None || Settings.Current.AnimationModule != null;

    /// <summary>Alto de un aviso de <paramref name="from"/> a <paramref name="to"/> (la ventana crece o se encoge con él).</summary>
    private static void AnimateHeight(FrameworkElement banner, double from, double to, Action done)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => done();
        banner.BeginAnimation(HeightProperty, animation);
    }

    private static void Collapse(FrameworkElement banner, Action done)
    {
        if (!Animated || banner.ActualHeight <= 0)
        {
            done();
            return;
        }
        AnimateHeight(banner, banner.ActualHeight, 0, done);
    }

    private void Discard(StrataNotification item)
    {
        _banners.Remove(item);
        if (_banners.Count == 0)
            Hide();
    }

    private void AnimateIn(StrataNotification item)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (BannerList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement banner)
            return;
        // Abre su hueco poco a poco (si no, los demás avisos saltaban de golpe) y entra desde el borde
        // de la pantalla más cercano, con la animación elegida en el Hub y un recorrido largo (como
        // los de Windows; con el de un panel casi no se notaba).
        if (Animated && _banners.Count > 1 && banner.ActualHeight > 0)
            AnimateHeight(banner, 0, banner.ActualHeight, () => banner.BeginAnimation(HeightProperty, null));
        Services.Themes.Motion.In(banner, banner, Edge, travel: 4);
    }

    // ── Posición ────────────────────────────────────────────────────────────

    /// <summary>Monitor elegido; si ya no está conectado, el principal de Strata UI.</summary>
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
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (x, y) = CornerPosition((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale));
        SetWindowPos(Handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    /// <summary>Dónde va la ventana (en píxeles) con ese tamaño para quedar pegada a su esquina.</summary>
    private (int X, int Y) CornerPosition(int width, int height)
    {
        var work = TargetScreen().WorkingArea;
        var corner = Corner;
        // El margen interno de la lista ya separa del borde; se suma un poco más.
        int gap = (int)(4 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        int x = corner.IsCenter() ? work.Left + (work.Width - width) / 2
              : corner.IsLeft() ? work.Left + gap
              : work.Right - width - gap;
        int y = corner.IsTop() ? work.Top + gap : work.Bottom - height - gap;
        return (x, y);
    }

    /// <summary>
    /// Cada vez que la ventana cambia de alto (llega o se va un aviso) se coloca en el mismo paso.
    /// Antes WPF la agrandaba o encogía con el borde de arriba fijo y se recolocaba un cuadro después:
    /// en las esquinas de abajo los avisos brincaban y se veían encimados un momento.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging || !IsVisible)
            return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((pos.Flags & SwpNoSize) != 0)
            return IntPtr.Zero; // solo se mueve: quien la mueve ya calculó dónde
        (pos.X, pos.Y) = CornerPosition(pos.Cx, pos.Cy);
        pos.Flags &= ~SwpNoMove;
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    // ── Clics ───────────────────────────────────────────────────────────────

    private void Banner_Click(object sender, MouseButtonEventArgs e)
    {
        // El clic fue en la X o en un link (los links se abren solos).
        if (e.OriginalSource is DependencyObject source
            && (Controls.RichText.IsInsideLink(source) || source is Visual && FindParent<System.Windows.Controls.Button>(source) != null))
            return;
        if (sender is FrameworkElement { DataContext: StrataNotification item })
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
        if (sender is FrameworkElement { DataContext: StrataNotification item })
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
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private const int WmWindowPosChanging = 0x0046;
    private static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
