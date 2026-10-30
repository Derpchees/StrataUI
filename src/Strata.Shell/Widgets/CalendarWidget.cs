using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Widgets;

/// <summary>
/// El mes en curso para el escritorio: nombre del mes, iniciales de los días (desde el primer día de la
/// semana de la región) y hoy marcado con el color de acento. Cambia de día con el reloj compartido.
/// </summary>
public sealed class CalendarWidget : StackPanel, IWidgetContent
{
    private readonly TextBlock _month = new() { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly UniformGrid _days = new() { Columns = 7 };
    private DateTime _shown;
    private double _scale = 1;

    public CalendarWidget(WidgetConfig config)
    {
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };
        _month.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        Children.Add(_month);
        Children.Add(_days);
        Apply(config);
        Loaded += (_, _) =>
        {
            Taskbar.ClockTick += OnTick;
            Taskbar.StartSharedClock();
            Build();
        };
        Unloaded += (_, _) => Taskbar.ClockTick -= OnTick;
    }

    public void Apply(WidgetConfig config)
    {
        _scale = config.Scale;
        _month.FontSize = 20 * _scale;
        _month.Margin = new Thickness(4 * _scale, 0, 0, 8 * _scale);
        _days.Width = 7 * 36 * _scale;
        Build();
    }

    private void OnTick()
    {
        if (DateTime.Today != _shown)
            Build();
    }

    private void Build()
    {
        var today = DateTime.Today;
        _shown = today;
        var culture = CultureInfo.GetCultureInfo(Loc.Language);
        string month = today.ToString("MMMM yyyy", culture);
        _month.Text = char.ToUpper(month[0], culture) + month[1..];

        _days.Children.Clear();
        // El primer día de la semana es el de la región de Windows; los nombres, del idioma de Strata UI.
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            _days.Children.Add(Cell(culture.DateTimeFormat.GetShortestDayName(day), dim: true, today: false, header: true));
        }
        var start = new DateTime(today.Year, today.Month, 1);
        int offset = ((int)start.DayOfWeek - (int)first + 7) % 7;
        var cursor = start.AddDays(-offset);
        // Seis semanas: el widget no cambia de alto de un mes a otro.
        for (int i = 0; i < 42; i++, cursor = cursor.AddDays(1))
            _days.Children.Add(Cell(cursor.Day.ToString(culture), dim: cursor.Month != today.Month, today: cursor == today, header: false));
    }

    private FrameworkElement Cell(string text, bool dim, bool today, bool header)
    {
        double size = 36 * _scale;
        var label = new TextBlock
        {
            Text = text,
            FontSize = (header ? 11 : 14) * _scale,
            FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brushes.White,
            Opacity = dim ? 0.5 : 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cell = new Border { Width = size, Height = header ? size * 0.7 : size, CornerRadius = new CornerRadius(size / 2), Child = label };
        if (today)
            cell.SetResourceReference(Border.BackgroundProperty, "StrataAccentBrush");
        return cell;
    }
}
