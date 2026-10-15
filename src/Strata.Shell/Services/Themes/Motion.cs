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
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !ReferenceEquals(e.OriginalSource, sender))
            return;
        In(element, element, FromPointer(element), element is ToolTip ? 0.35 : 0.5);
    }

    /// <summary>Un popup propio (bandeja oculta, carpeta del menú Inicio) se anima al abrirse.</summary>
    public static void Attach(Popup popup, Func<TaskbarPosition> from)
    {
        popup.PopupAnimation = PopupAnimation.None;
        popup.Opened += (_, _) =>
        {
            if (popup.Child is FrameworkElement child)
                In(child, child, from(), 0.6);
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
    /// Entrada: <paramref name="fade"/> (la ventana o el elemento) se desvanece y
    /// <paramref name="moving"/> se desliza o crece. <paramref name="from"/> es el lado desde el
    /// que llega (la barra, la esquina de los avisos). <paramref name="size"/> acorta recorrido y
    /// duración para cosas pequeñas (menús, tooltips, páginas del Hub).
    /// </summary>
    public static void In(UIElement fade, FrameworkElement moving, TaskbarPosition from, double size = 1)
    {
        if (Imported is { } imported)
        {
            CustomIn(fade, moving, from, size, imported.Enter);
            return;
        }
        var style = Settings.Current.PanelAnimation;
        var duration = Duration(closing: false) * Math.Max(0.6, size);
        if (style == PanelAnimation.None)
        {
            fade.BeginAnimation(UIElement.OpacityProperty, null);
            fade.Opacity = 1;
            moving.RenderTransform = Transform.Identity;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        fade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        switch (style)
        {
            case PanelAnimation.Slide or PanelAnimation.Rise:
                {
                    double distance = (style == PanelAnimation.Rise ? 60 : 16) * size;
                    IEasingFunction slideEase = style == PanelAnimation.Rise
                        ? new QuinticEase { EasingMode = EasingMode.EaseOut }
                        : ease;
                    var (property, offset) = Offset(from, distance);
                    var slide = new TranslateTransform();
                    moving.RenderTransform = slide;
                    slide.BeginAnimation(property, new DoubleAnimation(offset, 0, style == PanelAnimation.Rise ? duration * 1.4 : duration)
                    { EasingFunction = slideEase });
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
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(start, 1, span) { EasingFunction = grow });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(start, 1, span) { EasingFunction = grow });
                    break;
                }
            default:
                moving.RenderTransform = Transform.Identity;
                break;
        }
    }

    /// <summary>Salida: lo contrario de <see cref="In"/>; <paramref name="done"/> corre al terminar.</summary>
    public static void Out(UIElement fade, FrameworkElement moving, TaskbarPosition toward, Action done)
    {
        if (Imported is { } imported)
        {
            CustomOut(fade, moving, toward, imported.Exit!, done);
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
                    var (property, offset) = Offset(toward, style == PanelAnimation.Rise ? 40 : 12);
                    var slide = moving.RenderTransform as TranslateTransform ?? new TranslateTransform();
                    moving.RenderTransform = slide;
                    slide.BeginAnimation(property, new DoubleAnimation(offset, duration) { EasingFunction = ease });
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
                    break;
                }
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
    private static void CustomIn(UIElement fade, FrameworkElement moving, TaskbarPosition from, double size, MotionStep step)
    {
        var duration = Duration(closing: false) * Math.Max(0.6, size) * step.Duration;
        fade.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(step.Opacity, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var ease = Ease(step, EasingMode.EaseOut);
        var (property, offset) = Offset(from, step.Offset * size);
        double start = 1 - (1 - step.Scale) * size;
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        moving.RenderTransformOrigin = Origin(from);
        moving.RenderTransform = new TransformGroup { Children = { scale, slide } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(start, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(start, 1, duration) { EasingFunction = ease });
        slide.BeginAnimation(property, new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });
    }

    private static void CustomOut(UIElement fade, FrameworkElement moving, TaskbarPosition toward, MotionStep step, Action done)
    {
        var duration = Duration(closing: true) * step.Duration;
        var ease = Ease(step, EasingMode.EaseIn);
        var (property, offset) = Offset(toward, step.Offset);
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        moving.RenderTransformOrigin = Origin(toward);
        moving.RenderTransform = new TransformGroup { Children = { scale, slide } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(step.Scale, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(step.Scale, duration) { EasingFunction = ease });
        slide.BeginAnimation(property, new DoubleAnimation(offset, duration) { EasingFunction = ease });

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
