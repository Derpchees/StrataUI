using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using Winx.Shell.Services;
using Winx.Shell.Services.Themes;

namespace Winx.Shell.Controls;

/// <summary>
/// Vista previa en vivo de una ventana al pasar el mouse por su botón en la barra, como en Windows:
/// título, cerrar y la miniatura que dibuja DWM (se actualiza sola, sin capturas). Clic: la trae al
/// frente. Una sola ventana para todas las barras.
/// </summary>
public partial class TaskPreviewWindow : Window
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(250);
    private const double MaxThumbWidth = 240, MaxThumbHeight = 150;

    private static TaskPreviewWindow? _instance;
    private static readonly DispatcherTimer ShowTimer = new() { Interval = ShowDelay };
    private static readonly DispatcherTimer HideTimer = new() { Interval = HideDelay };
    private static FrameworkElement? _pendingAnchor;
    private static ApplicationWindow? _pendingWindow;

    private IntPtr _thumb;
    private ApplicationWindow? _window;
    private FrameworkElement? _anchor;

    static TaskPreviewWindow()
    {
        ShowTimer.Tick += (_, _) =>
        {
            ShowTimer.Stop();
            if (_pendingAnchor is { IsMouseOver: true } anchor && _pendingWindow is { } window)
                Show(anchor, window);
        };
        HideTimer.Tick += (_, _) =>
        {
            HideTimer.Stop();
            if (_instance is { IsMouseOver: false } preview && _pendingAnchor is not { IsMouseOver: true })
                preview.HidePreview();
        };
    }

    private TaskPreviewWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            // Fondo de vidrio del sistema detrás del tinte del tema y esquinas redondeadas de Windows 11.
            WindowEffects.SetBlur(this, Backdrop.Acrylic);
            int round = 2;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        };
        MouseEnter += (_, _) => HideTimer.Stop();
        MouseLeave += (_, _) => HideTimer.Start();
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Render);
    }

    // ── Desde los botones de la barra ───────────────────────────────────────

    /// <summary>El mouse entró en un botón: la vista previa aparece tras una pausa (o enseguida si ya se ve otra).</summary>
    public static void HoverStart(FrameworkElement anchor, ApplicationWindow window)
    {
        if (!Settings.Current.ShowTaskPreviews)
            return;
        HideTimer.Stop();
        _pendingAnchor = anchor;
        _pendingWindow = window;
        if (_instance is { IsVisible: true })
        {
            Show(anchor, window);
            return;
        }
        ShowTimer.Stop();
        ShowTimer.Start();
    }

    /// <summary>El mouse salió del botón: se oculta si tampoco está sobre la vista previa.</summary>
    public static void HoverEnd()
    {
        ShowTimer.Stop();
        HideTimer.Stop();
        HideTimer.Start();
    }

    /// <summary>Ocultar ya (clic en el botón, arrastre).</summary>
    public static void Dismiss()
    {
        ShowTimer.Stop();
        HideTimer.Stop();
        _pendingAnchor = null;
        _instance?.HidePreview();
    }

    public static void CloseAll()
    {
        Dismiss();
        _instance?.Close();
        _instance = null;
    }

    private static void Show(FrameworkElement anchor, ApplicationWindow window)
    {
        _instance ??= new TaskPreviewWindow();
        _instance.Present(anchor, window);
    }

    // ── Contenido ───────────────────────────────────────────────────────────

    private void Present(FrameworkElement anchor, ApplicationWindow window)
    {
        bool appearing = !IsVisible;
        _anchor = anchor;
        if (!ReferenceEquals(window, _window))
        {
            _window = window;
            TitleText.Text = window.Title;
            AppIcon.Source = window.Icon;
            Register(window.Handle);
        }
        if (appearing)
        {
            Opacity = 0;
            Show();
        }
        Place();
        Dispatcher.BeginInvoke(() =>
        {
            UpdateThumb();
            if (appearing)
                Motion.In(this, Card, Edge);
        }, DispatcherPriority.Loaded);
    }

    private void Register(IntPtr source)
    {
        Unregister();
        if (DwmRegisterThumbnail(Handle == IntPtr.Zero ? new WindowInteropHelper(this).EnsureHandle() : Handle, source, out _thumb) != 0)
        {
            _thumb = IntPtr.Zero;
            return;
        }
        // Miniatura del tamaño de la ventana, sin pasar del máximo.
        double width = MaxThumbWidth, height = MaxThumbHeight;
        if (DwmQueryThumbnailSourceSize(_thumb, out var size) == 0 && size.X > 0 && size.Y > 0)
        {
            // Las ventanas en píxeles físicos: se pasa a DIP para comparar con el máximo.
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double scale = Math.Min(MaxThumbWidth / (size.X / dpi), MaxThumbHeight / (size.Y / dpi));
            width = Math.Max(80, size.X / dpi * scale);
            height = Math.Max(48, size.Y / dpi * scale);
        }
        ThumbHost.Width = width;
        ThumbHost.Height = height;
    }

    private void UpdateThumb()
    {
        if (_thumb == IntPtr.Zero || !IsVisible)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var origin = ThumbHost.TransformToAncestor(this).Transform(new Point(0, 0));
        var props = new DwmThumbnailProperties
        {
            Flags = DwmTnpVisible | DwmTnpRectDestination | DwmTnpSourceClientAreaOnly,
            Visible = true,
            SourceClientAreaOnly = false,
            Destination = new NativeRect
            {
                Left = (int)Math.Round(origin.X * dpi),
                Top = (int)Math.Round(origin.Y * dpi),
                Right = (int)Math.Round((origin.X + ThumbHost.ActualWidth) * dpi),
                Bottom = (int)Math.Round((origin.Y + ThumbHost.ActualHeight) * dpi),
            },
        };
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    private void Unregister()
    {
        if (_thumb != IntPtr.Zero)
            DwmUnregisterThumbnail(_thumb);
        _thumb = IntPtr.Zero;
    }

    private void HidePreview()
    {
        if (!IsVisible)
            return;
        Unregister();
        _window = null;
        Hide();
    }

    // ── Posición: junto al botón, del lado de la pantalla ───────────────────

    private TaskbarPosition Edge => _anchor is not null && GetWindow(_anchor) is Taskbar bar ? bar.Position : TaskbarPosition.Bottom;

    private void Place()
    {
        if (_anchor is null || !IsVisible || PresentationSource.FromVisual(_anchor) is null)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = (int)Math.Ceiling(ActualWidth * dpi), height = (int)Math.Ceiling(ActualHeight * dpi);
        var a = _anchor.PointToScreen(new Point(0, 0));
        var b = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
        var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((a.X + b.X) / 2), (int)((a.Y + b.Y) / 2))).WorkingArea;
        int gap = (int)(8 * dpi);
        int x, y;
        switch (Edge)
        {
            case TaskbarPosition.Left:
                x = work.Left + gap;
                y = (int)((a.Y + b.Y) / 2) - height / 2;
                break;
            case TaskbarPosition.Right:
                x = work.Right - width - gap;
                y = (int)((a.Y + b.Y) / 2) - height / 2;
                break;
            case TaskbarPosition.Top:
                x = (int)((a.X + b.X) / 2) - width / 2;
                y = work.Top + gap;
                break;
            default:
                x = (int)((a.X + b.X) / 2) - width / 2;
                y = work.Bottom - height - gap;
                break;
        }
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - width - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - height - gap));
        SetWindowPos(Handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
        Dispatcher.BeginInvoke(UpdateThumb, DispatcherPriority.Render);
    }

    // ── Clics ───────────────────────────────────────────────────────────────

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && CloseButton.IsAncestorOf(source))
            return;
        var window = _window;
        Dismiss();
        window?.BringToFront();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        var window = _window;
        Dismiss();
        window?.Close();
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    // ── Win32 / DWM ─────────────────────────────────────────────────────────

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmTnpRectDestination = 0x1, DwmTnpVisible = 0x8, DwmTnpSourceClientAreaOnly = 0x10;

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
