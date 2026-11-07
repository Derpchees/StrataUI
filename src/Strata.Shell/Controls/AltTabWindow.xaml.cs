using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;

namespace Strata.Shell.Controls;

/// <summary>
/// Alt+Tab de Strata UI: las ventanas abiertas en el orden en que se usaron, con su miniatura en vivo
/// (DWM), su ícono y su título. Las teclas llegan del gancho de teclado (la ventana no se activa,
/// así la app del frente no pierde el foco mientras se elige): Tab y Shift+Tab avanzan y retroceden,
/// las flechas mueven la selección, Supr cierra la ventana elegida, Esc cancela y al soltar Alt se
/// cambia. Con un toque rápido de Alt+Tab no se muestra nada y se pasa a la ventana anterior.
/// </summary>
public partial class AltTabWindow : Window
{
    /// <summary>Lo que tarda en aparecer: un Alt+Tab rápido cambia de ventana sin mostrarla.</summary>
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(110);

    private static AltTabWindow? _instance;
    private static readonly DispatcherTimer ShowTimer = new() { Interval = ShowDelay };
    private static List<ApplicationWindow> _windows = [];
    private static int _selected = -1;
    private static bool _active;
    private static System.Windows.Forms.Screen? _screen;

    private sealed class Entry
    {
        public required ApplicationWindow Window { get; init; }
        public required Border Root { get; init; }
        public required Border ThumbHost { get; init; }
        public IntPtr Thumb { get; set; }
    }

    private readonly List<Entry> _entries = [];

    static AltTabWindow()
    {
        ShowTimer.Tick += (_, _) =>
        {
            ShowTimer.Stop();
            if (_active)
                (_instance ??= new AltTabWindow()).Present();
        };
    }

    private AltTabWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            WindowEffects.SetBlur(this, Backdrop.Acrylic);
            int round = 2;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        };
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Render);
    }

    public static bool IsOpen => _active;

    /// <summary>Crea la ventana al iniciar: así el primer Alt+Tab no espera a armarla.</summary>
    public static void Preload()
    {
        _instance ??= new AltTabWindow();
        new WindowInteropHelper(_instance).EnsureHandle();
    }

    // ── Desde el gancho de teclado ──────────────────────────────────────────

    /// <summary>
    /// Alt+Tab (o Alt+Shift+Tab): abre el selector o, si ya está, mueve la selección. Con Alt+Tab por
    /// monitor, solo las ventanas del monitor de la ventana activa, salvo con Ctrl (<paramref name="all"/>).
    /// </summary>
    public static void Step(bool backward, bool all)
    {
        if (!_active)
        {
            var foreground = GetForegroundWindow();
            _screen = foreground != IntPtr.Zero && !IsShellWindow(foreground)
                ? System.Windows.Forms.Screen.FromHandle(foreground)
                : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            _windows = Collect(Settings.Current.AltTabPerMonitor && !all ? _screen : null);
            if (_windows.Count == 0)
                return;
            _active = true;
            // La primera es la del frente: se empieza por la anterior (o por la última hacia atrás).
            bool firstIsCurrent = _windows[0].Handle == GetForegroundWindow();
            _selected = backward ? _windows.Count - 1 : Math.Min(firstIsCurrent ? 1 : 0, _windows.Count - 1);
            ShowTimer.Stop();
            ShowTimer.Start();
            return;
        }
        Select(_selected + (backward ? -1 : 1));
    }

    /// <summary>Flechas: a los lados, la anterior o la siguiente; arriba y abajo, la de la otra fila.</summary>
    public static void Arrow(int vk)
    {
        if (!_active)
            return;
        switch (vk)
        {
            case 0x25: Select(_selected - 1); break;
            case 0x27: Select(_selected + 1); break;
            case 0x26: Select(_instance?.RowNeighbor(-1) ?? _selected - 1); break;
            case 0x28: Select(_instance?.RowNeighbor(1) ?? _selected + 1); break;
        }
    }

    /// <summary>Se soltó Alt (o Enter): se cambia a la ventana elegida.</summary>
    public static void Commit()
    {
        if (!_active)
            return;
        var target = _selected >= 0 && _selected < _windows.Count ? _windows[_selected] : null;
        Close();
        if (target is null)
            return;
        try
        {
            target.BringToFront();
        }
        catch (Exception ex)
        {
            Log.Error($"Alt+Tab: no se pudo pasar a «{target.Title}»", ex);
        }
    }

    /// <summary>Esc: se cierra sin cambiar de ventana.</summary>
    public static void Cancel()
    {
        if (_active)
            Close();
    }

    /// <summary>Supr: cierra la ventana elegida y el selector sigue abierto con las demás.</summary>
    public static void CloseSelected()
    {
        if (!_active || _selected < 0 || _selected >= _windows.Count)
            return;
        CloseWindow(_windows[_selected]);
    }

    private static void CloseWindow(ApplicationWindow window)
    {
        int index = _windows.IndexOf(window);
        if (index < 0)
            return;
        window.Close();
        _windows.RemoveAt(index);
        if (_windows.Count == 0)
        {
            Close();
            return;
        }
        if (_selected >= _windows.Count)
            _selected = _windows.Count - 1;
        if (_instance is { IsVisible: true } switcher)
            switcher.Present();
    }

    private static void Select(int index)
    {
        if (_windows.Count == 0)
            return;
        _selected = (index % _windows.Count + _windows.Count) % _windows.Count;
        _instance?.Highlight();
    }

    private static new void Close()
    {
        _active = false;
        ShowTimer.Stop();
        _windows = [];
        _selected = -1;
        _instance?.HideSwitcher();
    }

    /// <summary>
    /// Las ventanas de la barra (las mismas reglas: escritorio virtual actual, sin herramientas ni
    /// ventanas ocultas), de la usada más reciente a la más vieja, según el orden de Windows (Z).
    /// </summary>
    private static List<ApplicationWindow> Collect(System.Windows.Forms.Screen? only)
    {
        List<ApplicationWindow> windows;
        try
        {
            windows = App.Current.Shell.Tasks.GroupedWindows.OfType<ApplicationWindow>()
                .Where(w => w.ShowInTaskbar && IsWindow(w.Handle)).ToList();
        }
        catch (InvalidOperationException)
        {
            return []; // el shell aún no arrancó
        }
        var order = new Dictionary<IntPtr, int>();
        EnumWindows((hwnd, _) =>
        {
            order[hwnd] = order.Count;
            return true;
        }, IntPtr.Zero);
        if (only is not null)
            windows = windows.Where(w => ScreenOf(w.Handle).DeviceName == only.DeviceName).ToList();
        return windows.OrderBy(w => order.TryGetValue(w.Handle, out int z) ? z : int.MaxValue).ToList();
    }

    /// <summary>El monitor de una ventana; minimizada, el de donde vuelve a abrirse.</summary>
    private static System.Windows.Forms.Screen ScreenOf(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (GetWindowPlacement(hwnd, ref placement))
            {
                var r = placement.Normal;
                return System.Windows.Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            }
        }
        return System.Windows.Forms.Screen.FromHandle(hwnd);
    }

    /// <summary>El escritorio o una barra de Strata UI: no dicen en qué monitor se está trabajando.</summary>
    private static bool IsShellWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == Environment.ProcessId;
    }

    // ── Contenido ───────────────────────────────────────────────────────────

    private void Present()
    {
        new WindowInteropHelper(this).EnsureHandle();
        Clear();
        var work = WorkArea();
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Más ventanas, miniaturas más chicas para que entren en la pantalla.
        // Todas a la misma altura (el ancho sigue la proporción de cada ventana): se ve más ordenado.
        int count = _windows.Count;
        double height = count <= 5 ? 150 : count <= 10 ? 124 : count <= 18 ? 100 : 80;
        Entries.MaxWidth = Math.Max(height * 2.2, work.Width / dpi * 0.86);
        foreach (var window in _windows)
            AddEntry(window, height * 1.9, height);
        Highlight();
        // Se mide y se coloca antes de mostrarla: si no, aparece en otro lado y salta al centro.
        UpdateLayout();
        Place(force: true);
        bool appearing = !IsVisible;
        if (appearing)
            Show();
        UpdateLayout();
        UpdateThumbs();
        if (appearing)
            AnimateIn();
    }

    // ── Animación de entrada: la elegida en Hub › Tema ──────────────────────

    private int _baseX, _baseY;
    private DateTime _animateUntil;
    private bool _following;

    /// <summary>
    /// Entra con la animación de los paneles. Las miniaturas las dibuja DWM y no siguen a WPF: en
    /// cada cuadro se recolocan (crecen o se deslizan con la tarjeta) y toman su transparencia.
    /// </summary>
    private void AnimateIn()
    {
        Motion.In(Card, Card, TaskbarPosition.Bottom, 0.8, window: MoveBy);
        _animateUntil = DateTime.UtcNow.AddMilliseconds(700);
        if (!_following)
        {
            _following = true;
            CompositionTarget.Rendering += FollowAnimation;
        }
    }

    private void FollowAnimation(object? sender, EventArgs e)
    {
        UpdateThumbs();
        if (DateTime.UtcNow < _animateUntil && IsVisible)
            return;
        _following = false;
        CompositionTarget.Rendering -= FollowAnimation;
    }

    /// <summary>Corre la ventana entera respecto de su lugar (DIP), para el deslizamiento.</summary>
    private void MoveBy(double x, double y)
    {
        if (!IsVisible)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        SetWindowPos(Handle, HwndTopmost, _baseX + (int)Math.Round(x * dpi), _baseY + (int)Math.Round(y * dpi), 0, 0, SwpNoSize | SwpNoActivate);
    }

    private void AddEntry(ApplicationWindow window, double maxWidth, double maxHeight)
    {
        var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), Source = TaskGroups.Icon(window) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var title = new TextBlock
        {
            Text = window.Title,
            FontSize = 12,
            MaxWidth = Math.Max(80, maxHeight * 16 / 10 - 24),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "StrataForegroundBrush");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 6) };
        header.Children.Add(icon);
        header.Children.Add(title);

        // Detrás de la miniatura, el ícono grande: es lo que se ve si la ventana está minimizada.
        var bigIcon = new Image
        {
            Width = 48,
            Height = 48,
            Source = TaskGroups.Icon(window),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.9,
        };
        RenderOptions.SetBitmapScalingMode(bigIcon, BitmapScalingMode.HighQuality);
        var thumbHost = new Border
        {
            Width = maxHeight * 16 / 10,
            Height = maxHeight,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = bigIcon,
        };
        thumbHost.SetResourceReference(Border.BackgroundProperty, "StrataHoverBrush");
        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(thumbHost);
        var root = new Border
        {
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content,
            ToolTip = window.Title,
        };
        var entry = new Entry { Window = window, Root = root, ThumbHost = thumbHost };
        root.MouseEnter += (_, _) => Select(_windows.IndexOf(window));
        root.MouseLeftButtonUp += (_, _) =>
        {
            Select(_windows.IndexOf(window));
            Commit();
        };
        // Clic con la rueda: cierra esa ventana, como en Windows.
        root.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
                CloseWindow(window);
        };
        if (!window.IsMinimized)
            Register(entry, maxWidth, maxHeight);
        _entries.Add(entry);
        Entries.Children.Add(root);
    }

    private void Highlight()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var root = _entries[i].Root;
            if (i == _selected)
            {
                root.SetResourceReference(Border.BorderBrushProperty, "StrataAccentBrush");
                root.SetResourceReference(Border.BackgroundProperty, "StrataHoverBrush");
            }
            else
            {
                root.BorderBrush = Brushes.Transparent;
                root.Background = Brushes.Transparent;
            }
        }
    }

    /// <summary>La tarjeta de la fila de arriba (-1) o de abajo (+1) más cerca en horizontal.</summary>
    private int? RowNeighbor(int direction)
    {
        if (!IsVisible || _selected < 0 || _selected >= _entries.Count)
            return null;
        var from = Center(_entries[_selected].Root);
        int best = -1;
        double bestScore = double.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            var p = Center(_entries[i].Root);
            double dy = (p.Y - from.Y) * direction;
            if (dy < 10)
                continue;
            double score = dy * 1000 + Math.Abs(p.X - from.X);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        // Sin fila en esa dirección: se da la vuelta (de la última fila a la primera y al revés).
        if (best < 0)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var p = Center(_entries[i].Root);
                double dy = (from.Y - p.Y) * direction;
                if (dy < 10)
                    continue;
                double score = -dy * 1000 + Math.Abs(p.X - from.X);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
        }
        return best < 0 ? _selected : best;
    }

    private Point Center(FrameworkElement element) =>
        element.TransformToAncestor(this).Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    /// <summary>Miniatura con la proporción de la ventana, sin pasar del máximo.</summary>
    private void Register(Entry entry, double maxWidth, double maxHeight)
    {
        if (DwmRegisterThumbnail(Handle, entry.Window.Handle, out var thumb) != 0)
            return;
        entry.Thumb = thumb;
        if (DwmQueryThumbnailSourceSize(thumb, out var size) == 0 && size.X > 0 && size.Y > 0)
        {
            double width = Math.Clamp(maxHeight * size.X / size.Y, maxHeight * 0.6, maxWidth);
            entry.ThumbHost.Width = width;
            if (entry.Root.Child is StackPanel { Children: [StackPanel { Children: [_, TextBlock title] }, ..] })
                title.MaxWidth = Math.Max(60, width - 24);
        }
    }

    private void UpdateThumbs()
    {
        if (!IsVisible)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        byte opacity = (byte)Math.Round(255 * Math.Clamp(Card.Opacity, 0, 1));
        foreach (var entry in _entries)
        {
            if (entry.Thumb == IntPtr.Zero || !entry.ThumbHost.IsVisible)
                continue;
            // Con la escala de la tarjeta (zoom): se toma el rectángulo ya transformado.
            var bounds = entry.ThumbHost.TransformToAncestor(this).TransformBounds(new Rect(entry.ThumbHost.RenderSize));
            var props = new DwmThumbnailProperties
            {
                Flags = DwmTnpVisible | DwmTnpRectDestination | DwmTnpSourceClientAreaOnly | DwmTnpOpacity,
                Visible = true,
                SourceClientAreaOnly = false,
                Opacity = opacity,
                Destination = new NativeRect
                {
                    Left = (int)Math.Round(bounds.Left * dpi),
                    Top = (int)Math.Round(bounds.Top * dpi),
                    Right = (int)Math.Round(bounds.Right * dpi),
                    Bottom = (int)Math.Round(bounds.Bottom * dpi),
                },
            };
            DwmUpdateThumbnailProperties(entry.Thumb, ref props);
        }
    }

    private void Clear()
    {
        foreach (var entry in _entries)
        {
            if (entry.Thumb != IntPtr.Zero)
                DwmUnregisterThumbnail(entry.Thumb);
        }
        _entries.Clear();
        Entries.Children.Clear();
    }

    private void HideSwitcher()
    {
        if (!IsVisible)
            return;
        // Las miniaturas las dibuja DWM encima de la ventana: se quitan antes de ocultarla.
        Clear();
        Hide();
    }

    // ── Posición: en el centro del monitor del mouse ────────────────────────

    /// <summary>El monitor de la ventana activa (con Alt+Tab por monitor, el de esas ventanas).</summary>
    private static System.Drawing.Rectangle WorkArea() =>
        (_screen ?? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position)).WorkingArea;

    private void Place() => Place(force: false);

    private void Place(bool force)
    {
        if (!IsVisible && !force)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = IsVisible ? new Size(ActualWidth, ActualHeight) : Measured();
        int width = (int)Math.Ceiling(size.Width * dpi), height = (int)Math.Ceiling(size.Height * dpi);
        var work = WorkArea();
        int x = work.Left + Math.Max(0, (work.Width - width) / 2);
        int y = work.Top + Math.Max(0, (work.Height - height) / 2);
        if (!IsVisible)
        {
            // WPF coloca la ventana en Left/Top al mostrarla: sin esto aparece un instante en una esquina.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x / dpi;
            Top = y / dpi;
        }
        _baseX = x;
        _baseY = y;
        SetWindowPos(Handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
        if (IsVisible)
            Dispatcher.BeginInvoke(UpdateThumbs, DispatcherPriority.Render);
    }

    private Size Measured()
    {
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Card.DesiredSize;
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    // ── Win32 / DWM ─────────────────────────────────────────────────────────

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmTnpRectDestination = 0x1, DwmTnpOpacity = 0x4, DwmTnpVisible = 0x8, DwmTnpSourceClientAreaOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public int Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length, Flags, ShowCmd;
        public int MinX, MinY, MaxX, MaxY;
        public NativeRect Normal;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DwmThumbnailProperties properties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out NativeSize size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
