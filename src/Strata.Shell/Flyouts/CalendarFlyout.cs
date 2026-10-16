using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Calendario del reloj, como el de Windows 11: la fecha de hoy arriba y el mes en una cuadrícula
/// propia con los estilos de los paneles (el Calendar de WPF trae el aspecto de Windows 7). Las
/// flechas o la rueda cambian de mes; clic en el mes vuelve a hoy.
/// </summary>
public sealed class CalendarFlyout : FlyoutWindow
{
    private readonly TextBlock _month = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _days = new() { Columns = 7 };
    private DateTime _shown = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private static CultureInfo Culture => CultureInfo.GetCultureInfo(Loc.Language);

    public CalendarFlyout()
    {
        Title = Loc.T("Calendario");
        Width = 340;
        var content = new StackPanel { Margin = new Thickness(14, 16, 14, 12) };

        var today = new TextBlock { Margin = new Thickness(4, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        today.SetResourceReference(StyleProperty, "FlyoutTitle");
        string date = DateTime.Today.ToString(Loc.T("dddd d 'de' MMMM"), Culture);
        today.Text = char.ToUpper(date[0], Culture) + date[1..];
        content.Children.Add(today);

        // Mes y flechas
        var header = new Grid { Margin = new Thickness(4, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var month = new System.Windows.Controls.Button { Content = _month, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 4, 6, 4),
                                 ToolTip = Loc.T("Volver a hoy") };
        month.SetResourceReference(StyleProperty, "FlyoutRow");
        _month.SetResourceReference(StyleProperty, "FlyoutText");
        _month.FontWeight = FontWeights.SemiBold;
        month.Click += (_, _) => Show(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), 0);
        header.Children.Add(month);
        header.Children.Add(Arrow(SymbolRegular.ChevronUp20, Loc.T("Mes anterior"), -1, 1));
        header.Children.Add(Arrow(SymbolRegular.ChevronDown20, Loc.T("Mes siguiente"), 1, 2));
        content.Children.Add(header);

        // Iniciales de los días, empezando por el primero de la semana del idioma
        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
        var first = Culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var name = new TextBlock
            {
                Text = Culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4),
            };
            name.SetResourceReference(StyleProperty, "FlyoutHint");
            names.Children.Add(name);
        }
        content.Children.Add(names);
        content.Children.Add(_days);
        content.PreviewMouseWheel += (_, e) =>
        {
            Show(_shown.AddMonths(e.Delta > 0 ? -1 : 1), e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };
        Content = content;
        Show(_shown, 0);
    }

    private System.Windows.Controls.Button Arrow(SymbolRegular icon, string tip, int months, int column)
    {
        var button = new System.Windows.Controls.Button
        {
            Width = 34, Height = 34, Padding = new Thickness(0), ToolTip = tip,
            Content = new SymbolIcon { Symbol = icon, FontSize = 14 },
        };
        button.SetResourceReference(StyleProperty, "FlyoutIconButton");
        button.Click += (_, _) => Show(_shown.AddMonths(months), months);
        Grid.SetColumn(button, column);
        return button;
    }

    /// <summary>Dibuja el mes; <paramref name="direction"/> anima la entrada desde arriba (-1) o abajo (1).</summary>
    private void Show(DateTime month, int direction)
    {
        _shown = month;
        string title = month.ToString("Y", Culture);
        _month.Text = char.ToUpper(title[0], Culture) + title[1..];

        _days.Children.Clear();
        int offset = ((int)month.DayOfWeek - (int)Culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var start = month.AddDays(-offset);
        for (int i = 0; i < 42; i++)
            _days.Children.Add(Day(start.AddDays(i), month));

        if (direction != 0)
            Services.Themes.Motion.In(_days, _days, direction < 0 ? TaskbarPosition.Top : TaskbarPosition.Bottom, 0.6);
    }

    private static FrameworkElement Day(DateTime day, DateTime month)
    {
        bool today = day == DateTime.Today;
        var cell = new Grid { Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        if (today)
        {
            var circle = new Ellipse();
            circle.SetResourceReference(Shape.FillProperty, "StrataAccentBrush");
            cell.Children.Add(circle);
        }
        var number = new TextBlock
        {
            Text = day.Day.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal,
            // Los días del mes anterior y el siguiente, tenues.
            Opacity = day.Month == month.Month ? 1 : 0.4,
        };
        number.SetResourceReference(StyleProperty, "FlyoutText");
        if (today)
            number.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        cell.Children.Add(number);
        return cell;
    }
}
