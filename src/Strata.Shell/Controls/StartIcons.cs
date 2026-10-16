using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Controls;

/// <summary>Íconos para el botón Inicio, dibujados con los colores del tema.</summary>
public static class StartIcons
{
    public static IReadOnlyList<(StartIconKind Kind, string Name)> All { get; } =
    [
        (StartIconKind.Strata, "Strata UI"),
        (StartIconKind.Windows, "Windows"),
        (StartIconKind.WindowsMono, Loc.T("Monocromo")),
        (StartIconKind.Circle, Loc.T("Círculo")),
        (StartIconKind.Dot, Loc.T("Punto")),
        (StartIconKind.Grid, Loc.T("Cuadrícula")),
        (StartIconKind.Search, Loc.T("Lupa")),
    ];

    public static FrameworkElement Create(StartIconKind kind, double size = 16) => kind switch
    {
        StartIconKind.Windows => Squares(size, "StrataAccentBrush"),
        StartIconKind.WindowsMono => Squares(size, "StrataForegroundBrush"),
        StartIconKind.Circle => Themed(new Ellipse { Width = size, Height = size, StrokeThickness = size / 7 }, Shape.StrokeProperty, "StrataAccentBrush"),
        StartIconKind.Dot => Themed(new Ellipse { Width = size * 0.6, Height = size * 0.6 }, Shape.FillProperty, "StrataAccentBrush"),
        StartIconKind.Grid => Dots(size),
        StartIconKind.Search => Glyph("", size),
        _ => Logo.Themed(size),
    };

    /// <summary>Cuatro cuadrados: el logo de Windows.</summary>
    private static FrameworkElement Squares(double size, string brush)
    {
        var grid = new UniformGrid { Rows = 2, Columns = 2, Width = size, Height = size };
        for (int i = 0; i < 4; i++)
        {
            var square = new Rectangle { Margin = new Thickness(size / 20), RadiusX = size / 32, RadiusY = size / 32 };
            square.SetResourceReference(Shape.FillProperty, brush);
            grid.Children.Add(square);
        }
        return grid;
    }

    private static FrameworkElement Dots(double size)
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = size, Height = size };
        for (int i = 0; i < 9; i++)
            grid.Children.Add(Themed(new Ellipse { Width = size / 4.5, Height = size / 4.5 }, Shape.FillProperty, "StrataForegroundBrush"));
        return grid;
    }

    private static FrameworkElement Glyph(string glyph, double size)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "StrataForegroundBrush");
        return text;
    }

    private static FrameworkElement Themed(Shape shape, DependencyProperty property, string brush)
    {
        shape.SetResourceReference(property, brush);
        return shape;
    }
}
