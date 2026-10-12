using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Winx.Shell.Services.Themes;

/// <summary>
/// Animaciones de entrada y salida de paneles, menú Inicio, avisos y OSD, según lo elegido en
/// Hub › Apariencia (<see cref="PanelAnimation"/> y <see cref="AnimationSpeed"/>).
/// </summary>
public static class Motion
{
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
    /// que llega (la barra, la esquina de los avisos).
    /// </summary>
    public static void In(UIElement fade, FrameworkElement moving, TaskbarPosition from)
    {
        var style = Settings.Current.PanelAnimation;
        var duration = Duration(closing: false);
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
                    double distance = style == PanelAnimation.Rise ? 60 : 16;
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
                    double start = style == PanelAnimation.Pop ? 0.8 : 0.92;
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
