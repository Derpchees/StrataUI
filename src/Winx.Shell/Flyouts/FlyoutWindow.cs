using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Winx.Shell.Services;
using Winx.Shell.Services.Themes;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Flyouts;

/// <summary>
/// Panel flotante que aparece sobre un botón de la barra (volumen, red, Bluetooth…).
/// Usa el material Acrylic de Windows 11 y se cierra al perder el foco o con Esc.
/// </summary>
public abstract class FlyoutWindow : FluentWindow
{
    private const int GapPx = 12;
    private bool _closing;

    protected FlyoutWindow()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        MinHeight = 0; // el estilo de FluentWindow impone un mínimo pensado para ventanas normales
        ExtendsContentIntoTitleBar = true;
        // Paneles: Acrylic como los de Windows; sólido en Material; en Liquid Glass, desenfoque
        // limpio propio (sin el ruido ni la luminosidad del Acrylic) con esquinas grandes.
        var theme = ThemeManager.Theme;
        // Se decide aquí: cambiar WindowBackdropType más tarde hace que WPF-UI deshaga el efecto.
        WindowBackdropType = theme.Backdrop == Backdrop.Acrylic ? WindowBackdropType.Acrylic : WindowBackdropType.None;
        if (theme.Backdrop == Backdrop.Blur)
        {
            // WPF-UI pone el fondo sólido del tema al inicializar la ventana: se vuelve a
            // dejar transparente después, o taparía el desenfoque.
            SourceInitialized += (_, _) =>
            {
                Background = Brushes.Transparent;
                WindowEffects.SetBlur(this, Backdrop.Blur);
            };
            Loaded += (_, _) => Background = Brushes.Transparent;
        }
        // WindowChrome (lo usa WPF-UI) borra la región de la ventana al cambiar de tamaño:
        // se reaplica después de que termine de procesar el cambio.
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(ApplyRegion, System.Windows.Threading.DispatcherPriority.Background);
        ContentRendered += (_, _) => ApplyRegion();
        WindowCornerPreference = WindowCornerPreference.Round;
        Opacity = 0; // se muestra al terminar de posicionarse, sin parpadeo

        Deactivated += (_, _) => CloseFlyout();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !HandleEscape())
                CloseFlyout();
        };
    }

    /// <summary>
    /// El contenido de cada panel se coloca sobre las capas del tema y dentro de un único
    /// ScrollViewer: si no cabe en la pantalla aparece la barra y la rueda funciona en cualquier
    /// parte. (Las subclases no deben anidar otros ScrollViewer, que "atraparían" la rueda.)
    /// </summary>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        if (newContent is UIElement element && !ReferenceEquals(newContent, _root))
        {
            Content = BuildRoot(element);
            return;
        }
        base.OnContentChanged(oldContent, newContent);
    }

    private Grid? _root;

    /// <summary>Capas del panel: tinte del tema, brillo del vidrio, borde de luz y contenido con scroll.</summary>
    private Grid BuildRoot(UIElement content)
    {
        _root = new Grid();
        if (ThemeManager.UseLiveGlass && !FillWorkArea)
        {
            // Vidrio líquido real: más esmerilado que la barra para que el texto se lea bien.
            var glass = new Controls.LiquidGlassSurface
            {
                Refraction = 20,
                Frost = 5,
                CornerRadius = ThemeManager.Theme.PanelRadius,
            };
            glass.SetLook(ThemeManager.IsLight);
            _root.Children.Add(glass);
        }
        _root.Children.Add(Layer("WinxFlyoutBackgroundBrush", null));
        var highlight = Layer("WinxGlassHighlightBrush", null);
        highlight.SetResourceReference(VisibilityProperty, "WinxGlassVisibility");
        _root.Children.Add(highlight);
        var edge = Layer(null, "WinxGlassEdgeBrush");
        edge.BorderThickness = new Thickness(1);
        _root.Children.Add(edge);
        _root.Children.Add(!ScrollContent ? content : new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
        });
        return _root;
    }

    /// <summary>
    /// False en paneles de alto fijo que desplazan solo una parte (el menú Inicio): así su
    /// ScrollViewer interno recibe un alto limitado y no hay dos scrolls anidados.
    /// </summary>
    protected virtual bool ScrollContent => true;

    /// <summary>El panel ocupa toda el área de trabajo del monitor (menú a pantalla completa).</summary>
    protected virtual bool FillWorkArea => false;

    /// <summary>
    /// Espacio (DIP) que algo del panel ocupa por encima de su borde superior; se descuenta del
    /// alto máximo para que quepa en la pantalla (la foto del menú Perfil).
    /// </summary>
    protected virtual double TopOverflow => 0;

    /// <summary>Recorta la ventana con esquinas redondeadas si el tema las pide más grandes que las de DWM.</summary>
    private void ApplyRegion()
    {
        double radius = ThemeManager.Theme.PanelRadius;
        if (FillWorkArea || radius <= 8)
            return; // a pantalla completa no lleva esquinas
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bounds = new Int32Rect(0, 0, (int)Math.Round(ActualWidth * scale), (int)Math.Round(ActualHeight * scale));
        WindowEffects.SetRoundedRegion(this, bounds, (int)Math.Round(radius * scale));
    }

    /// <summary>Agrega un elemento por encima de todo el contenido (p. ej. asas para redimensionar).</summary>
    protected void AddOverlay(UIElement element) => _root?.Children.Add(element);

    private static Border Layer(string? background, string? border)
    {
        var layer = new Border { IsHitTestVisible = false };
        layer.SetResourceReference(Border.CornerRadiusProperty, "WinxPanelCornerRadius");
        if (background != null)
            layer.SetResourceReference(Border.BackgroundProperty, background);
        if (border != null)
            layer.SetResourceReference(Border.BorderBrushProperty, border);
        return layer;
    }

    /// <summary>Permite a un panel usar Esc para otra cosa (p. ej. borrar la búsqueda) antes de cerrarse.</summary>
    protected virtual bool HandleEscape() => false;

    /// <summary>Alineación horizontal respecto al botón que abre el panel.</summary>
    protected virtual FlyoutPlacement Placement => FlyoutPlacement.Right;

    public void ShowAt(FrameworkElement anchor)
    {
        if (FillWorkArea)
            SizeToContent = SizeToContent.Manual;
        Show();
        if (!FillWorkArea)
            LimitHeightToScreen(anchor);
        PositionAbove(anchor);
        // Si el contenido cambia de tamaño (secciones que se expanden, listas que terminan de cargar),
        // se mantiene pegado a la barra. Se difiere hasta después del render: al disparar SizeChanged
        // la ventana nativa todavía tiene el tamaño anterior.
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(() => PositionAbove(anchor),
                                                        System.Windows.Threading.DispatcherPriority.Render);
        Activate();
        AnimateIn();
    }

    public void CloseFlyout()
    {
        if (_closing)
            return;
        _closing = true;
        Dispatcher.BeginInvoke(Close);
    }

    private void LimitHeightToScreen(FrameworkElement anchor)
    {
        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        MaxHeight = work.Height / scale - 2 * GapPx - TopOverflow;
    }

    private void PositionAbove(FrameworkElement anchor)
    {
        if (_closing || !anchor.IsVisible)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Tamaño según WPF (ya actualizado), convertido a píxeles físicos.
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);

        // Todo en píxeles físicos: PointToScreen ya los devuelve así en modo PerMonitorV2.
        var anchorRight = anchor.PointToScreen(new Point(anchor.ActualWidth, 0));
        var anchorCenter = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)anchorCenter.X, (int)anchorCenter.Y)).WorkingArea;
        int gap = (int)(GapPx * scale);

        if (FillWorkArea)
        {
            SetWindowPos(hwnd, IntPtr.Zero, work.Left, work.Top, work.Width, work.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            return;
        }

        if (Placement == FlyoutPlacement.ScreenCenter)
        {
            // Tipo Spotlight: centrado en el monitor con la parte superior fija (crece hacia abajo).
            int cx = work.Left + (work.Width - width) / 2;
            int cy = work.Top + work.Height / 5;
            SetWindowPos(hwnd, IntPtr.Zero, cx, cy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            return;
        }

        var anchorLeft = anchor.PointToScreen(new Point(0, 0));
        int preferred = Placement switch
        {
            FlyoutPlacement.Center => (int)anchorCenter.X - width / 2,
            FlyoutPlacement.Left => (int)anchorLeft.X,
            _ => (int)anchorRight.X - width,
        };
        int x = Math.Clamp(preferred, work.Left + gap, work.Right - width - gap);
        int y = work.Bottom - height - gap;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void AnimateIn()
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        // Se desliza solo el contenido; las capas de fondo quedan fijas sobre el Acrylic.
        if (_root?.Children[^1] is FrameworkElement content)
        {
            var slide = new TranslateTransform(0, 16);
            content.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, duration) { EasingFunction = ease });
        }
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

public enum FlyoutPlacement { Right, Center, Left, ScreenCenter }

/// <summary>Garantiza un solo panel abierto y que el botón funcione como interruptor.</summary>
public static class FlyoutManager
{
    private static FlyoutWindow? _open;
    private static Type? _lastClosedType;
    private static DateTime _lastClosedAt;

    public static void Toggle<T>(FrameworkElement anchor) where T : FlyoutWindow, new()
    {
        // Un panel que no llegó a mostrarse (falló al abrir) no cuenta como abierto.
        if (_open is { IsVisible: false })
            _open = null;

        if (_open is T current)
        {
            current.CloseFlyout();
            return;
        }

        // El clic sobre el botón ya cerró el panel al quitarle el foco: no reabrirlo.
        if (_lastClosedType == typeof(T) && DateTime.UtcNow - _lastClosedAt < TimeSpan.FromMilliseconds(350))
            return;

        _open?.CloseFlyout();
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var flyout = new T();
            long created = watch.ElapsedMilliseconds;
            flyout.ContentRendered += (_, _) =>
            {
                // Solo se registran las aperturas lentas, para detectar regresiones sin llenar el log.
                if (watch.ElapsedMilliseconds > 300)
                    Log.Info($"Panel {typeof(T).Name} lento: creado en {created} ms, visible en {watch.ElapsedMilliseconds} ms");
            };
            flyout.Closed += (_, _) =>
            {
                _lastClosedType = typeof(T);
                _lastClosedAt = DateTime.UtcNow;
                if (ReferenceEquals(_open, flyout))
                    _open = null;
            };
            _open = flyout;
            flyout.ShowAt(anchor);
        }
        catch (Exception ex)
        {
            _open = null;
            Log.Error($"No se pudo abrir el panel {typeof(T).Name}", ex);
        }
    }

    public static void CloseAll() => _open?.CloseFlyout();
}
