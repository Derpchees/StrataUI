using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Animaciones de entrada y salida de paneles, menú Inicio, avisos, OSD, menús, tooltips y el Hub, según lo elegido en
/// Hub › Tema (<see cref="PanelAnimation"/> y <see cref="AnimationSpeed"/>), o una animación
/// importada (<see cref="AnimationModule"/>).
/// </summary>
public static class Motion
{
    /// <summary>
    /// Toda la app usa las animaciones elegidas: los menús contextuales y los tooltips de cualquier
    /// ventana de Strata UI se animan solos al abrirse (más cortas que las de un panel).
    /// </summary>
    public static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnMenuClosed));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
        // Los popups de WPF traen su propia animación (la de Windows: fundido o deslizamiento) y
        // corría a la vez que la nuestra: el menú se animaba dos veces y a tirones.
        if (Application.Current is { } app)
        {
            app.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
            app.Resources[SystemParameters.ToolTipPopupAnimationKey] = PopupAnimation.None;
        }
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !ReferenceEquals(e.OriginalSource, sender))
            return;
        var from = FromPointer(element);
        AnimatePopup(element, from, element is ToolTip ? 0.5 : 0.85, remember: element is ContextMenu);
    }

    /// <summary>
    /// Entrada de lo que vive en un popup (menú, tooltip, bandeja oculta). Se mueve la ventana del
    /// popup, no su contenido: el popup mide justo lo que el menú y, al desplazar el contenido
    /// dentro, se veía recortado por el borde (como si saliera de una ranura).
    /// </summary>
    private static void AnimatePopup(FrameworkElement element, TaskbarPosition from, double size, bool remember)
    {
        var mover = PopupMover.For(element);
        // Recorrido corto: el de una ventana entera (un panel) es demasiado para un menú.
        In(element, element, from, size, mover is null ? null : mover.Move, mover is null ? 1 : 0.3);
        if (remember)
            Remember(element, from, mover);
    }

    /// <summary>Corre la ventana nativa de un popup respecto de donde la puso WPF.</summary>
    private sealed class PopupMover
    {
        private readonly IntPtr _hwnd;
        private readonly double _scale;
        private int _dx, _dy;

        private PopupMover(IntPtr hwnd, double scale) => (_hwnd, _scale) = (hwnd, scale);

        /// <summary>Desplazamiento aplicado ahora (DIP), para saber dónde queda al final.</summary>
        public double X => _dx / _scale;
        public double Y => _dy / _scale;

        public static PopupMover? For(FrameworkElement element) =>
            PresentationSource.FromVisual(element) is System.Windows.Interop.HwndSource { Handle: var hwnd } && hwnd != IntPtr.Zero
                ? new PopupMover(hwnd, VisualTreeHelper.GetDpi(element).DpiScaleX)
                : null;

        public void Move(double x, double y)
        {
            // Relativo a donde está ahora menos lo ya aplicado: si WPF lo recoloca a media
            // animación (cambió de tamaño), se sigue su nuevo lugar.
            if (!GetWindowRect(_hwnd, out var rect))
                return;
            int dx = (int)Math.Round(x * _scale), dy = (int)Math.Round(y * _scale);
            SetWindowPos(_hwnd, IntPtr.Zero, rect.Left - _dx + dx, rect.Top - _dy + dy, 0, 0, NoSize | NoZOrder | NoActivate);
            (_dx, _dy) = (dx, dy);
        }

        private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && ReferenceEquals(e.OriginalSource, sender))
            LeaveAsGhost(element);
    }

    // ── Salida de lo que WPF cierra de golpe (menús y popups) ────────────────

    private sealed record Shown(Rect Bounds, TaskbarPosition From);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, Shown> Opened = new();

    /// <summary>
    /// Dónde quedó en pantalla (en DIP) al abrirse, para su salida. Sin el desplazamiento de la
    /// entrada que todavía corre (si no, la salida salía corrida unos píxeles).
    /// </summary>
    private static void Remember(FrameworkElement element, TaskbarPosition from, PopupMover? mover = null) =>
        element.Dispatcher.BeginInvoke(() =>
        {
            if (PresentationSource.FromVisual(element) is { CompositionTarget: { } target } && element.ActualWidth > 0)
            {
                var corner = target.TransformFromDevice.Transform(element.PointToScreen(new Point(0, 0)));
                if (mover != null)
                    corner.Offset(-mover.X, -mover.Y);
                Opened.AddOrUpdate(element, new Shown(new Rect(corner, new Size(element.ActualWidth, element.ActualHeight)), from));
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// Un menú o popup se cierra sin esperar (WPF no deja retrasarlo): queda una imagen de cómo se
    /// veía, en una ventana que no recibe clics ni el foco, y esa imagen sale con la animación elegida.
    /// </summary>
    private static void LeaveAsGhost(FrameworkElement element)
    {
        if (!Opened.TryGetValue(element, out var shown))
            return;
        Opened.Remove(element);
        if (Settings.Current.PanelAnimation == PanelAnimation.None && Imported is null)
            return;
        try
        {
            var size = shown.Bounds.Size;
            var dpi = VisualTreeHelper.GetDpi(element);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None }, null, new Rect(size));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            bitmap.Freeze();

            var image = new System.Windows.Controls.Image { Source = bitmap, Width = size.Width, Height = size.Height, IsHitTestVisible = false };
            var ghost = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Left = shown.Bounds.X,
                Top = shown.Bounds.Y,
                Width = size.Width,
                Height = size.Height,
                Content = image,
            };
            ghost.SourceInitialized += (_, _) => ClickThrough(ghost);
            ghost.Show();
            // Se mueve la ventana de la imagen (como en la entrada), no la imagen dentro: se recortaba.
            Out(image, image, shown.From, () => ghost.Close(),
                (x, y) => (ghost.Left, ghost.Top) = (shown.Bounds.X + x, shown.Bounds.Y + y), travel: 0.3);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo animar el cierre del menú", ex);
        }
    }

    /// <summary>La ventana de la salida no recibe clics, ni el foco, ni aparece en Alt+Tab.</summary>
    private static void ClickThrough(Window window)
    {
        const int ExStyle = -20, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, ExStyle, GetWindowLong(hwnd, ExStyle) | Transparent | ToolWindow | NoActivate);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// <summary>
    /// Gira un ícono (la flecha de la bandeja) hasta <paramref name="angle"/> con la velocidad elegida;
    /// sin animación (o con las animaciones apagadas), de una vez.
    /// </summary>
    public static void Turn(RotateTransform rotate, double angle, bool animate = true)
    {
        if (!animate || Settings.Current.PanelAnimation == PanelAnimation.None && Imported is null)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = angle;
            return;
        }
        var turn = new DoubleAnimation(angle, Duration(closing: false))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        // Al terminar queda el valor final como propio (sin que la animación lo retenga).
        turn.Completed += (_, _) => rotate.Angle = angle;
        rotate.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    /// <summary>Un popup propio (bandeja oculta, carpeta del menú Inicio) se anima al abrirse y al cerrarse.</summary>
    public static void Attach(Popup popup, Func<TaskbarPosition> from)
    {
        popup.PopupAnimation = PopupAnimation.None;
        popup.Opened += (_, _) =>
        {
            if (popup.Child is FrameworkElement child)
                AnimatePopup(child, from(), 0.9, remember: true);
        };
        popup.Closed += (_, _) =>
        {
            if (popup.Child is FrameworkElement child)
                LeaveAsGhost(child);
        };
    }

    /// <summary>
    /// Lado desde el que llega un menú: si quedó debajo del puntero baja desde arriba; si quedó
    /// encima (abierto desde la barra inferior), sube.
    /// </summary>
    private static TaskbarPosition FromPointer(FrameworkElement element)
    {
        try
        {
            if (!GetCursorPos(out var cursor) || PresentationSource.FromVisual(element) is null)
                return TaskbarPosition.Top;
            var top = element.PointToScreen(new Point(0, 0));
            return top.Y >= cursor.Y - 4 ? TaskbarPosition.Top : TaskbarPosition.Bottom;
        }
        catch (InvalidOperationException)
        {
            return TaskbarPosition.Top;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    private static TimeSpan Duration(bool closing)
    {
        double ms = Settings.Current.AnimationSpeed switch
        {
            AnimationSpeed.Fast => 110,
            AnimationSpeed.Slow => 320,
            _ => 180,
        };
        // Al cerrar, más rápido: lo que se va no debe hacer esperar.
        return TimeSpan.FromMilliseconds(closing ? ms * 0.7 : ms);
    }

    /// <summary>
    /// Mueve una ventana entera en vez de un elemento: recibe el desplazamiento (DIP) respecto de su
    /// lugar final. Los paneles con Acrylic lo necesitan: el material de DWM cubre la ventana y no
    /// se mueve con una transformación de WPF (solo se movía el contenido, con el fondo quieto).
    /// </summary>
    public delegate void WindowMover(double x, double y);

    /// <summary>
    /// Anima un valor de <paramref name="from"/> a <paramref name="to"/> en cada cuadro que dibuja WPF
    /// (para lo que no es una propiedad animable, como la posición de una ventana nativa). El tiempo
    /// avanza con los cuadros dibujados y como mucho 1/20 s por cuadro: el primero de una ventana
    /// nueva puede tardar cientos de ms y, con un reloj normal, el recorrido ya habría terminado
    /// cuando se ve.
    /// </summary>
    private static void Drive(TimeSpan duration, IEasingFunction? ease, double from, double to, Action<double> apply,
                              Action? completed = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan last = TimeSpan.Zero, elapsed = TimeSpan.Zero;
        apply(from);
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var now = clock.Elapsed;
            elapsed += TimeSpan.FromTicks(Math.Min((now - last).Ticks, TimeSpan.FromSeconds(1.0 / 20).Ticks));
            last = now;
            double t = duration <= TimeSpan.Zero ? 1 : Math.Min(1, elapsed / duration);
            apply(from + (to - from) * (ease?.Ease(t) ?? t));
            if (t < 1)
                return;
            CompositionTarget.Rendering -= tick;
            completed?.Invoke();
        };
        CompositionTarget.Rendering += tick;
    }

    /// <summary>Desliza con <paramref name="window"/> o, sin ella, con una transformación de <paramref name="moving"/>.</summary>
    private static void Slide(FrameworkElement moving, WindowMover? window, TaskbarPosition side, double from, double to,
                              TimeSpan duration, IEasingFunction? ease)
    {
        var (property, sign) = Offset(side, 1);
        if (window != null)
        {
            bool vertical = property == TranslateTransform.YProperty;
            Drive(duration, ease, from * sign, to * sign, v => window(vertical ? 0 : v, vertical ? v : 0));
            return;
        }
        var slide = moving.RenderTransform as TranslateTransform ?? new TranslateTransform();
        moving.RenderTransform = slide;
        slide.BeginAnimation(property, new DoubleAnimation(from * sign, to * sign, duration) { EasingFunction = ease });
    }

    /// <summary>
    /// Escala de <paramref name="from"/> a <paramref name="to"/>. <paramref name="framed"/>: con el reloj
    /// de cuadros dibujados (<see cref="Drive"/>), para ventanas nuevas cuyo primer cuadro tarda (si no,
    /// el crecimiento terminaba antes de que se vieran).
    /// </summary>
    private static void Grow(ScaleTransform scale, double from, double to, TimeSpan duration, IEasingFunction? ease, bool framed)
    {
        if (framed)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Drive(duration, ease, from, to, v =>
            {
                scale.ScaleX = v;
                scale.ScaleY = v;
            });
            return;
        }
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, to, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, to, duration) { EasingFunction = ease });
    }

    /// <summary>Fundido de entrada; <paramref name="framed"/> como en <see cref="Grow"/>.</summary>
    private static void FadeIn(UIElement fade, double from, TimeSpan duration, bool framed)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (framed)
        {
            fade.BeginAnimation(UIElement.OpacityProperty, null);
            Drive(duration, ease, from, 1, v => fade.Opacity = v);
        }
        else
            fade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
    }

    /// <summary>
    /// Entrada: <paramref name="fade"/> (la ventana o el elemento) se desvanece y
    /// <paramref name="moving"/> se desliza o crece. <paramref name="from"/> es el lado desde el
    /// que llega (la barra, la esquina de los avisos). <paramref name="size"/> acorta recorrido y
    /// duración para cosas pequeñas (menús, tooltips, páginas del Hub). Con <paramref name="window"/>,
    /// lo que se desliza es la ventana entera. <paramref name="travel"/> alarga solo el recorrido
    /// (los avisos llegan de más lejos que un panel).
    /// </summary>
    public static void In(UIElement fade, FrameworkElement moving, TaskbarPosition from, double size = 1,
                          WindowMover? window = null, double travel = 1)
    {
        if (Imported is { } imported)
        {
            CustomIn(fade, moving, from, size, imported.Enter, window, travel);
            return;
        }
        var style = Settings.Current.PanelAnimation;
        var duration = Duration(closing: false) * Math.Max(0.6, size);
        if (style == PanelAnimation.None)
        {
            fade.BeginAnimation(UIElement.OpacityProperty, null);
            fade.Opacity = 1;
            moving.RenderTransform = Transform.Identity;
            window?.Invoke(0, 0);
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // Una ventana entera aparece rápido y el resto del tiempo se ve deslizarse (si el fundido
        // dura lo mismo, casi todo el recorrido pasa mientras todavía es transparente).
        FadeIn(fade, 0, window != null ? duration * 0.5 : duration, framed: window != null);

        switch (style)
        {
            case PanelAnimation.Slide or PanelAnimation.Rise:
                {
                    // Una ventana entera sale de detrás de la barra: recorre más que un contenido.
                    double distance = (style == PanelAnimation.Rise ? 80 : window != null ? 48 : 16) * size * travel;
                    IEasingFunction slideEase = window != null
                        ? new CubicEase { EasingMode = EasingMode.EaseOut }
                        : style == PanelAnimation.Rise ? new QuinticEase { EasingMode = EasingMode.EaseOut } : ease;
                    moving.RenderTransform = Transform.Identity;
                    Slide(moving, window, from, distance, 0, style == PanelAnimation.Rise ? duration * 1.4 : duration * 1.2, slideEase);
                    break;
                }
            case PanelAnimation.Zoom or PanelAnimation.Pop:
                {
                    var scale = new ScaleTransform(1, 1);
                    moving.RenderTransformOrigin = Origin(from);
                    moving.RenderTransform = scale;
                    double start = 1 - (style == PanelAnimation.Pop ? 0.2 : 0.08) * size;
                    IEasingFunction grow = style == PanelAnimation.Pop
                        ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
                        : ease;
                    var span = style == PanelAnimation.Pop ? duration * 1.5 : duration;
                    Grow(scale, start, 1, span, grow, framed: window != null);
                    // La ventana acompaña con un recorrido corto: el fondo de DWM no puede crecer.
                    if (window != null)
                        Slide(moving, window, from, 12 * size, 0, span, ease);
                    break;
                }
            default:
                moving.RenderTransform = Transform.Identity;
                // Desvanecer una ventana entera (panel, menú Inicio): su fondo de DWM no se puede
                // desvanecer, así que además asoma desde la barra con un recorrido corto.
                if (window != null)
                    Slide(moving, window, from, 16 * size * travel, 0, duration, ease);
                break;
        }
    }

    /// <summary>
    /// Salida: lo contrario de <see cref="In"/>; <paramref name="done"/> corre al terminar. Con
    /// <paramref name="window"/>, la ventana entera vuelve hacia la barra.
    /// </summary>
    public static void Out(UIElement fade, FrameworkElement moving, TaskbarPosition toward, Action done,
                           WindowMover? window = null, double travel = 1)
    {
        if (Imported is { } imported)
        {
            CustomOut(fade, moving, toward, imported.Exit!, done, window, travel);
            return;
        }
        var style = Settings.Current.PanelAnimation;
        if (style == PanelAnimation.None)
        {
            done();
            return;
        }
        var duration = Duration(closing: true);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = ease };
        bool finished = false;
        void Finish()
        {
            if (finished)
                return;
            finished = true;
            done();
        }
        fadeOut.Completed += (_, _) => Finish();

        switch (style)
        {
            case PanelAnimation.Slide or PanelAnimation.Rise:
                {
                    double distance = (style == PanelAnimation.Rise ? 40 : window != null ? 28 : 12) * travel;
                    if (window != null)
                        Slide(moving, window, toward, 0, distance, duration, ease);
                    else
                    {
                        var (property, offset) = Offset(toward, distance);
                        var slide = moving.RenderTransform as TranslateTransform ?? new TranslateTransform();
                        moving.RenderTransform = slide;
                        slide.BeginAnimation(property, new DoubleAnimation(offset, duration) { EasingFunction = ease });
                    }
                    break;
                }
            case PanelAnimation.Zoom or PanelAnimation.Pop:
                {
                    var scale = moving.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
                    moving.RenderTransformOrigin = Origin(toward);
                    moving.RenderTransform = scale;
                    double end = style == PanelAnimation.Pop ? 0.85 : 0.94;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(end, duration) { EasingFunction = ease });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(end, duration) { EasingFunction = ease });
                    if (window != null)
                        Slide(moving, window, toward, 0, 10, duration, ease);
                    break;
                }
            default:
                // Desvanecer una ventana entera: vuelve hacia la barra un poco (ver In).
                if (window != null)
                    Slide(moving, window, toward, 0, 10 * travel, duration, ease);
                break;
        }
        fade.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        // Por si la animación no llega a correr (ventana oculta, sin render): no quedarse a medias.
        _ = Task.Delay(duration + TimeSpan.FromMilliseconds(150)).ContinueWith(
            _ => Application.Current?.Dispatcher.BeginInvoke(Finish));
    }

    /// <summary>La animación importada elegida (null: una de las incluidas).</summary>
    private static AnimationModule? Imported =>
        Settings.Current.AnimationModule is { } id ? ModuleStore.Get<AnimationModule>(ModuleKind.Animation, id) : null;

    /// <summary>Entrada de una animación importada: se desvanece, se desliza y crece a la vez.</summary>
    private static void CustomIn(UIElement fade, FrameworkElement moving, TaskbarPosition from, double size, MotionStep step,
                                 WindowMover? window, double travel)
    {
        var duration = Duration(closing: false) * Math.Max(0.6, size) * step.Duration;
        // Una ventana entera (panel, menú Inicio) con el reloj de cuadros dibujados, como las incluidas.
        bool framed = window != null;
        FadeIn(fade, step.Opacity, duration, framed);
        var ease = Ease(step, EasingMode.EaseOut);
        double start = 1 - (1 - step.Scale) * size;
        var scale = new ScaleTransform(1, 1);
        moving.RenderTransformOrigin = Origin(from);
        moving.RenderTransform = scale;
        Grow(scale, start, 1, duration, ease, framed);
        if (window != null)
            Slide(moving, window, from, step.Offset * size * travel, 0, duration, ease);
        else
        {
            var (property, offset) = Offset(from, step.Offset * size * travel);
            var slide = new TranslateTransform();
            moving.RenderTransform = new TransformGroup { Children = { scale, slide } };
            slide.BeginAnimation(property, new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });
        }
    }

    private static void CustomOut(UIElement fade, FrameworkElement moving, TaskbarPosition toward, MotionStep step, Action done,
                                  WindowMover? window, double travel)
    {
        var duration = Duration(closing: true) * step.Duration;
        var ease = Ease(step, EasingMode.EaseIn);
        var scale = new ScaleTransform(1, 1);
        moving.RenderTransformOrigin = Origin(toward);
        moving.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(step.Scale, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(step.Scale, duration) { EasingFunction = ease });
        if (window != null)
            Slide(moving, window, toward, 0, step.Offset * travel, duration, ease);
        else
        {
            var (property, offset) = Offset(toward, step.Offset * travel);
            var slide = new TranslateTransform();
            moving.RenderTransform = new TransformGroup { Children = { scale, slide } };
            slide.BeginAnimation(property, new DoubleAnimation(offset, duration) { EasingFunction = ease });
        }

        bool finished = false;
        void Finish()
        {
            if (finished)
                return;
            finished = true;
            done();
        }
        var fadeOut = new DoubleAnimation(step.Opacity, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) => Finish();
        fade.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        _ = Task.Delay(duration + TimeSpan.FromMilliseconds(150)).ContinueWith(
            _ => Application.Current?.Dispatcher.BeginInvoke(Finish));
    }

    private static IEasingFunction? Ease(MotionStep step, EasingMode mode) => step.Easing switch
    {
        MotionEasing.Linear => null,
        MotionEasing.Sine => new SineEase { EasingMode = mode },
        MotionEasing.Quint => new QuinticEase { EasingMode = mode },
        MotionEasing.Back => new BackEase { EasingMode = mode, Amplitude = step.Amplitude },
        MotionEasing.Elastic => new ElasticEase { EasingMode = mode, Oscillations = 2, Springiness = Math.Max(1, 8 - step.Amplitude * 2) },
        MotionEasing.Bounce => new BounceEase { EasingMode = mode, Bounces = 2, Bounciness = Math.Max(1.5, 4 - step.Amplitude) },
        _ => new CubicEase { EasingMode = mode },
    };

    /// <summary>Desplazamiento inicial: del lado de la barra hacia el interior de la pantalla.</summary>
    private static (DependencyProperty Property, double Offset) Offset(TaskbarPosition from, double distance) => from switch
    {
        TaskbarPosition.Top => (TranslateTransform.YProperty, -distance),
        TaskbarPosition.Left => (TranslateTransform.XProperty, -distance),
        TaskbarPosition.Right => (TranslateTransform.XProperty, distance),
        _ => (TranslateTransform.YProperty, distance),
    };

    /// <summary>Crece desde el borde pegado a la barra.</summary>
    private static Point Origin(TaskbarPosition from) => from switch
    {
        TaskbarPosition.Top => new Point(0.5, 0),
        TaskbarPosition.Left => new Point(0, 0.5),
        TaskbarPosition.Right => new Point(1, 0.5),
        _ => new Point(0.5, 1),
    };
}
