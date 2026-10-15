using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Strata.Shell.Services;

namespace Strata.Shell.Widgets;

/// <summary>
/// Reloj digital grande para el escritorio, con la fecha debajo (opcional). Blanco con sombra suave
/// para leerse sobre cualquier fondo. Se actualiza con el reloj compartido de las barras (una vez
/// por minuto).
/// </summary>
public sealed class ClockWidget : StackPanel, IWidgetContent
{
    private readonly TextBlock _time = new() { FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _date = new() { FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9 };

    public ClockWidget(WidgetConfig config)
    {
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _time.FontFamily = _date.FontFamily = font;
        _time.Foreground = _date.Foreground = Brushes.White;
        Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };
        Children.Add(_time);
        Children.Add(_date);
        Apply(config);
        Loaded += (_, _) =>
        {
            Taskbar.ClockTick += Update;
            Taskbar.StartSharedClock();
            Update();
        };
        Unloaded += (_, _) => Taskbar.ClockTick -= Update;
    }

    public void Apply(WidgetConfig config)
    {
        _time.FontSize = 84 * config.Scale;
        _date.FontSize = 18 * config.Scale;
        _date.Margin = new Thickness(0, -8 * config.Scale, 0, 0);
        _date.Visibility = config.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        Update();
    }

    private void Update()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        _time.Text = now.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        // "jueves, 15 de octubre" / "Thursday, October 15": el día y la fecha corta del idioma.
        string date = $"{now.ToString("dddd", culture)}, {now.ToString(culture.DateTimeFormat.MonthDayPattern, culture)}";
        _date.Text = char.ToUpper(date[0], culture) + date[1..];
    }
}
