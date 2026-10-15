using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;

namespace Strata.Shell.Controls;

/// <summary>
/// Vista previa en vivo de las ventanas de un botón de la barra, como en Windows: por cada ventana,
/// su título, cerrar y la miniatura que dibuja DWM (se actualiza sola, sin capturas). Clic: la trae
/// al frente. Con las ventanas agrupadas por app muestra todas las del grupo. Una sola ventana de
/// vista previa para todas las barras.
/// </summary>
public partial class TaskPreviewWindow : Window
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(250);

    private static TaskPreviewWindow? _instance;
    private static readonly DispatcherTimer ShowTimer = new() { Interval = ShowDelay };
    private static readonly DispatcherTimer HideTimer = new() { Interval = HideDelay };
    private static FrameworkElement? _pendingAnchor;
    private static Func<IReadOnlyList<ApplicationWindow>>? _pendingWindows;

    /// <summary>Una ventana en la vista previa con su miniatura de DWM.</summary>
    private sealed class Entry
    {
        public required ApplicationWindow Window { get; init; }
        public required Border Root { get; init; }
        public required Border ThumbHost { get; init; }
        public IntPtr Thumb { get; set; }
    }

    private readonly List<Entry> _entries = new();
    private FrameworkElement? _anchor;

    static TaskPreviewWindow()
    {
        ShowTimer.Tick += (_, _) =>
        {
            ShowTimer.Stop();
            if (_pendingAnchor is { IsMouseOver: true } anchor && _pendingWindows is { } windows)
                Show(anchor, windows());
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

    /// <summary>La barra de ese botón muestra vistas previas (opción de su monitor).</summary>
    public static bool Enabled(FrameworkElement anchor) =>
        Settings.Current.For((Window.GetWindow(anchor) as Taskbar)?.Device).ShowTaskPreviews;

    /// <summary>
    /// El mouse entró en un botón: la vista previa aparece tras una pausa (o enseguida si ya se ve
    /// otra). Las ventanas se piden al mostrarla, por si cambiaron mientras tanto.
    /// </summary>
    public static void HoverStart(FrameworkElement anchor, Func<IReadOnlyList<ApplicationWindow>> windows)
    {
        if (!Enabled(anchor))
            return;
        HideTimer.Stop();
        _pendingAnchor = anchor;
        _pendingWindows = windows;
        if (_instance is { IsVisible: true })
        {
            Show(anchor, windows());
            return;
        }
        ShowTimer.Stop();
        ShowTimer.Start();
    }

    /// <summary>Muestra ya las ventanas (clic en un botón con varias ventanas agrupadas).</summary>
    public static void ShowNow(FrameworkElement anchor, IReadOnlyList<ApplicationWindow> windows)
    {
        ShowTimer.Stop();
        HideTimer.Stop();
        _pendingAnchor = anchor;
        Show(anchor, windows);
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

    public static bool IsShowingFor(FrameworkElement anchor) =>
        _instance is { IsVisible: true } preview && ReferenceEquals(preview._anchor, anchor);

    public static void CloseAll()
    {
        Dismiss();
        _instance?.Close();
        _instance = null;
    }

    private static void Show(FrameworkElement anchor, IReadOnlyList<ApplicationWindow> windows)
    {
        if (windows.Count == 0)
            return;
        _instance ??= new TaskPreviewWindow();
        _instance.Present(anchor, windows);
    }

    // ── Contenido ───────────────────────────────────────────────────────────

    private void Present(FrameworkElement anchor, IReadOnlyList<ApplicationWindow> windows)
    {
        bool appearing = !IsVisible;
        _anchor = anchor;
        new WindowInteropHelper(this).EnsureHandle();
        Entries.Orientation = Edge is TaskbarPosition.Left or TaskbarPosition.Right ? Orientation.Vertical : Orientation.Horizontal;
        if (!_entries.Select(e => e.Window).SequenceEqual(windows))
        {
            Clear();
            // Con muchas ventanas, miniaturas más chicas para que quepan.
            double maxWidth = windows.Count > 4 ? 170 : windows.Count > 2 ? 200 : 240;
            foreach (var window in windows)
                AddEntry(window, maxWidth, maxWidth * 0.625);
        }
        if (appearing)
        {
            Opacity = 0;
            Show();
        }
        Place();
        Dispatcher.BeginInvoke(() =>
        {
            UpdateThumbs();
            if (appearing)
                Motion.In(this, Card, Edge);
        }, DispatcherPriority.Loaded);
    }

    private void AddEntry(ApplicationWindow window, double maxWidth, double maxHeight)
    {
        var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), Source = TaskGroups.Icon(window) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var title = new TextBlock
        {
            Text = window.Title,
            FontSize = 12,
            MaxWidth = maxWidth - 40,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "StrataForegroundBrush");
        var close = new Button
        {
            Style = (Style)FindResource("TaskbarButton"),
            Width = 24,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = Loc.T("Cerrar ventana"),
            Content = new TextBlock { Style = (Style)FindResource("IconText"), FontSize = 10, Text = "\uE8BB" },
        };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(icon);
        header.Children.Add(title);
        header.Children.Add(close);

        var thumbHost = new Border { Width = maxWidth, Height = maxHeight, HorizontalAlignment = HorizontalAlignment.Center };
        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(thumbHost);
        var root = new Border
        {
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content,
        };
        root.MouseEnter += (_, _) => root.SetResourceReference(Border.BackgroundProperty, "StrataHoverBrush");
        root.MouseLeave += (_, _) => root.Background = Brushes.Transparent;
        root.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && close.IsAncestorOf(source))
                return;
            Dismiss();
            window.BringToFront();
        };

        var entry = new Entry { Window = window, Root = root, ThumbHost = thumbHost };
        close.Click += (_, _) => CloseEntry(entry);
        Register(entry, maxWidth, maxHeight);
        _entries.Add(entry);
        Entries.Children.Add(root);
    }

    private void CloseEntry(Entry entry)
    {
        entry.Window.Close();
        Unregister(entry);
        _entries.Remove(entry);
        Entries.Children.Remove(entry.Root);
        if (_entries.Count == 0)
            Dismiss();
        else
            Dispatcher.BeginInvoke(UpdateThumbs, DispatcherPriority.Loaded);
    }

    /// <summary>Miniatura con la proporción de la ventana, sin pasar del máximo.</summary>
    private void Register(Entry entry, double maxWidth, double maxHeight)
    {
        if (DwmRegisterThumbnail(Handle, entry.Window.Handle, out var thumb) != 0)
            return;
        entry.Thumb = thumb;
        if (DwmQueryThumbnailSourceSize(thumb, out var size) == 0 && size.X > 0 && size.Y > 0)
        {
            // Las ventanas miden en píxeles físicos: se pasa a DIP para compararlas con el máximo.
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double scale = Math.Min(maxWidth / (size.X / dpi), maxHeight / (size.Y / dpi));
            entry.ThumbHost.Width = Math.Max(80, size.X / dpi * scale);
            entry.ThumbHost.Height = Math.Max(48, size.Y / dpi * scale);
        }
    }

    private void UpdateThumbs()
    {
        if (!IsVisible)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var entry in _entries)
        {
            if (entry.Thumb == IntPtr.Zero || !entry.ThumbHost.IsVisible)
                continue;
            var origin = entry.ThumbHost.TransformToAncestor(this).Transform(new Point(0, 0));
            var props = new DwmThumbnailProperties
            {
                Flags = DwmTnpVisible | DwmTnpRectDestination | DwmTnpSourceClientAreaOnly,
                Visible = true,
                SourceClientAreaOnly = false,
                Destination = new NativeRect
                {
                    Left = (int)Math.Round(origin.X * dpi),
                    Top = (int)Math.Round(origin.Y * dpi),
                    Right = (int)Math.Round((origin.X + entry.ThumbHost.ActualWidth) * dpi),
                    Bottom = (int)Math.Round((origin.Y + entry.ThumbHost.ActualHeight) * dpi),
                },
            };
            DwmUpdateThumbnailProperties(entry.Thumb, ref props);
        }
    }

    private static void Unregister(Entry entry)
    {
        if (entry.Thumb != IntPtr.Zero)
            DwmUnregisterThumbnail(entry.Thumb);
        entry.Thumb = IntPtr.Zero;
    }

    private void Clear()
    {
        foreach (var entry in _entries)
            Unregister(entry);
        _entries.Clear();
        Entries.Children.Clear();
    }

    private void HidePreview()
    {
        if (!IsVisible)
            return;
        Clear();
        _anchor = null;
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
        Dispatcher.BeginInvoke(UpdateThumbs, DispatcherPriority.Render);
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
