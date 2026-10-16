using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Panel flotante que aparece sobre un botón de la barra (volumen, red, Bluetooth…).
/// Usa el material Acrylic de Windows 11 y se cierra al perder el foco o con Esc.
/// </summary>
public abstract class FlyoutWindow : FluentWindow
{
    private const int GapPx = 12;
    private bool _closing;
    private readonly string _device;

    /// <summary>
    /// Monitor de la barra desde la que se va a abrir el próximo panel (lo pone FlyoutManager
    /// antes de crearlo): el tema se decide al construirlo y es el de ese monitor.
    /// </summary>
    internal static string? NextDevice { get; set; }

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
        _device = NextDevice ?? ThemeManager.MainDevice;
        NextDevice = null;
        var theme = ThemeManager.ThemeFor(_device);
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

        Deactivated += (_, _) =>
        {
            if (!StayOpenWhenDeactivated)
                CloseFlyout();
        };
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
        // Colores del monitor de la barra desde la que se abre, si no son los generales.
        if (ThemeManager.PaletteFor(_device) is { } palette)
            _root.Resources.MergedDictionaries.Add(palette);
        // Color propio de los paneles claro sobre un tema oscuro (o al revés): textos y controles a juego.
        if (ThemeManager.PanelContrastFor(_device) is { } contrast)
            _root.Resources.MergedDictionaries.Add(contrast);
        if (ThemeManager.UseLiveGlassFor(_device) && !FillWorkArea)
        {
            // Vidrio líquido real: más esmerilado que la barra para que el texto se lea bien.
            var glass = new Controls.LiquidGlassSurface
            {
                Refraction = 20,
                Frost = 5,
                CornerRadius = ThemeManager.ThemeFor(_device).PanelRadius,
            };
            glass.SetLook(ThemeManager.IsLightFor(_device));
            _root.Children.Add(glass);
        }
        _root.Children.Add(Layer("StrataFlyoutBackgroundBrush", null));
        var highlight = Layer("StrataGlassHighlightBrush", null);
        highlight.SetResourceReference(VisibilityProperty, "StrataGlassVisibility");
        _root.Children.Add(highlight);
        var edge = Layer(null, "StrataGlassEdgeBrush");
        edge.BorderThickness = new Thickness(1);
        _root.Children.Add(edge);
        _content = !ScrollContent ? (FrameworkElement)content : new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
        };
        _root.Children.Add(_content);
        return _root;
    }

    /// <summary>
    /// El contenido (lo que se desliza al abrir). No se toma "el último hijo": después pueden
    /// agregarse capas encima (las asas para redimensionar del menú Inicio) y se movían ellas solas.
    /// </summary>
    private FrameworkElement? _content;

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
        double radius = ThemeManager.ThemeFor(_device).PanelRadius;
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
        layer.SetResourceReference(Border.CornerRadiusProperty, "StrataPanelCornerRadius");
        if (background != null)
            layer.SetResourceReference(Border.BackgroundProperty, background);
        if (border != null)
            layer.SetResourceReference(Border.BorderBrushProperty, border);
        return layer;
    }

    /// <summary>
    /// Justo antes de mostrarse, con el borde de la barra ya conocido: lo que cambia el tamaño va aquí y
    /// no en Loaded (ahí el panel ya se midió y se mostraba, y se medía y se movía otra vez mientras entraba).
    /// </summary>
    protected virtual void BeforeShow() { }

    /// <summary>
    /// Crea el panel fuera de la pantalla, deja que se dibuje una vez y lo cierra: la primera apertura de
    /// verdad (después de iniciar Windows) ya no paga la carga de estilos, plantillas e íconos.
    /// </summary>
    public void WarmUp()
    {
        ShowActivated = false;
        Left = Top = -32000;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ContentRendered += (_, _) =>
        {
            Log.Info($"{GetType().Name} preparado en {watch.ElapsedMilliseconds} ms");
            Dispatcher.BeginInvoke(Close, System.Windows.Threading.DispatcherPriority.Background);
        };
        Show();
    }

    /// <summary>True mientras el panel espera algo que le quita el foco (p. ej. el diálogo de UAC).</summary>
    protected virtual bool StayOpenWhenDeactivated => false;

    /// <summary>Permite a un panel usar Esc para otra cosa (p. ej. borrar la búsqueda) antes de cerrarse.</summary>
    protected virtual bool HandleEscape() => false;

    /// <summary>Alineación horizontal respecto al botón que abre el panel.</summary>
    protected virtual FlyoutPlacement Placement => FlyoutPlacement.Right;

    /// <summary>Borde de la barra desde la que se abrió (el panel aparece pegado a ella).</summary>
    protected TaskbarPosition BarPosition { get; private set; } = TaskbarPosition.Bottom;

    /// <summary>La barra desde la que se abrió: el panel entra y sale por detrás de ella.</summary>
    private IntPtr _bar;

    /// <summary>Lugar final (píxeles físicos) y desplazamiento de la animación (DIP) respecto de él.</summary>
    private int _baseX, _baseY;
    private double _offsetX, _offsetY;
    private bool _placed;

    public void ShowAt(FrameworkElement anchor)
    {
        if (Window.GetWindow(anchor) is Taskbar bar)
        {
            BarPosition = bar.Position;
            _bar = new WindowInteropHelper(bar).Handle;
        }
        if (FillWorkArea)
            SizeToContent = SizeToContent.Manual;
        BeforeShow();
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
        if (!IsVisible)
        {
            Dispatcher.BeginInvoke(Close);
            return;
        }
        Services.Themes.Motion.Out(this, Moving(), BarPosition, () => Dispatcher.BeginInvoke(Close), MoveBy);
    }

    /// <summary>Corre la ventana entera respecto de su lugar final (lo usa la animación).</summary>
    private void MoveBy(double x, double y)
    {
        _offsetX = x;
        _offsetY = y;
        if (_placed)
            MoveTo(_baseX, _baseY);
    }

    private void MoveTo(int x, int y)
    {
        _baseX = x;
        _baseY = y;
        _placed = true;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                     x + (int)Math.Round(_offsetX * scale), y + (int)Math.Round(_offsetY * scale), 0, 0,
                     SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Lo que se mueve al abrir y cerrar: al deslizar, solo el contenido (las capas de fondo quedan
    /// fijas sobre el Acrylic); al acercar o rebotar, el panel entero.
    /// </summary>
    private FrameworkElement Moving() =>
        Settings.Current.PanelAnimation is PanelAnimation.Zoom or PanelAnimation.Pop || _root is null
            ? (FrameworkElement?)_root ?? this
            : _content ?? _root;

    private void LimitHeightToScreen(FrameworkElement anchor)
    {
        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)).WorkingArea;
        // La escala del monitor donde se abre, no la de la ventana: se crea en otro (el principal) y
        // con monitores a distinta escala (100 % y 125 %) el alto máximo salía de más y el panel (el
        // de notificaciones, que crece con ellas) se salía de la pantalla.
        double scale = MonitorScale((int)center.X, (int)center.Y) ?? VisualTreeHelper.GetDpi(this).DpiScaleY;
        MaxHeight = Math.Max(120, work.Height / scale - 2 * GapPx - TopOverflow);
    }

    /// <summary>Escala (1 = 96 ppp) del monitor que contiene ese punto de la pantalla.</summary>
    private static double? MonitorScale(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
        return monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : null;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

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
            MoveTo(work.Left + (work.Width - width) / 2, work.Top + work.Height / 5);
            return;
        }

        var anchorLeft = anchor.PointToScreen(new Point(0, 0));
        // Lo que sobresale por arriba (la foto del menú Perfil) también tiene que caber.
        int top = work.Top + gap + (int)Math.Round(TopOverflow * scale);
        int x, y;
        if (BarPosition is TaskbarPosition.Left or TaskbarPosition.Right)
        {
            // Barra vertical: el panel va a su lado. "Left" (Inicio) se alinea con el borde de
            // arriba del botón, "Right" (bandeja) con el de abajo, "Center" con su centro.
            var anchorBottom = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
            int preferredY = Placement switch
            {
                FlyoutPlacement.Center => (int)((anchorLeft.Y + anchorBottom.Y) / 2) - height / 2,
                FlyoutPlacement.Left => (int)anchorLeft.Y,
                _ => (int)anchorBottom.Y - height,
            };
            y = Math.Clamp(preferredY, top, Math.Max(top, work.Bottom - height - gap));
            x = BarPosition == TaskbarPosition.Left ? work.Left + gap : work.Right - width - gap;
        }
        else
        {
            int preferred = Placement switch
            {
                FlyoutPlacement.Center => (int)anchorCenter.X - width / 2,
                FlyoutPlacement.Left => (int)anchorLeft.X,
                _ => (int)anchorRight.X - width,
            };
            x = Math.Clamp(preferred, work.Left + gap, work.Right - width - gap);
            y = BarPosition == TaskbarPosition.Top ? top : work.Bottom - height - gap;
        }
        MoveTo(x, y);
    }

    /// <summary>
    /// Entra desde la barra: desde abajo si está abajo, desde la izquierda si está a la izquierda…
    /// La ventana entera (con su fondo de DWM) sale de detrás de la barra, como en Windows: se
    /// coloca justo debajo de ella en el orden de las ventanas (las dos siguen siempre encima).
    /// </summary>
    private void AnimateIn()
    {
        if (_bar != IntPtr.Zero)
            SetWindowPos(new WindowInteropHelper(this).Handle, _bar, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        // Si se cierra mientras entra, la salida manda: la entrada deja de mover la ventana.
        Services.Themes.Motion.In(this, Moving(), BarPosition, window: (x, y) =>
        {
            if (!_closing)
                MoveBy(x, y);
        });
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
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
            FlyoutWindow.NextDevice = (Window.GetWindow(anchor) as Taskbar)?.Device;
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
                MemoryTrim.Schedule(TimeSpan.FromSeconds(5));
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

    /// <summary>Hay un panel de ese tipo abierto (p. ej. para no mostrar el OSD encima del de volumen).</summary>
    public static bool IsOpen<T>() where T : FlyoutWindow => _open is T { IsVisible: true };
}
