using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Controls;

/// <summary>Íconos para el botón Inicio, dibujados con los colores del tema.</summary>
public static class StartIcons
{
    public static IReadOnlyList<(StartIconKind Kind, string Name)> All { get; } =
    [
        (StartIconKind.Winx, "WINX"),
        (StartIconKind.Windows, "Windows"),
        (StartIconKind.WindowsMono, Loc.T("Monocromo")),
        (StartIconKind.Circle, Loc.T("Círculo")),
        (StartIconKind.Dot, Loc.T("Punto")),
        (StartIconKind.Grid, Loc.T("Cuadrícula")),
        (StartIconKind.Search, Loc.T("Lupa")),
    ];

    public static FrameworkElement Create(StartIconKind kind, double size = 16) => kind switch
    {
        StartIconKind.Windows => Squares(size, "WinxAccentBrush", rotate: false, fade: false),
        StartIconKind.WindowsMono => Squares(size, "WinxForegroundBrush", rotate: false, fade: false),
        StartIconKind.Circle => Themed(new Ellipse { Width = size, Height = size, StrokeThickness = size / 7 }, Shape.StrokeProperty, "WinxAccentBrush"),
        StartIconKind.Dot => Themed(new Ellipse { Width = size * 0.6, Height = size * 0.6 }, Shape.FillProperty, "WinxAccentBrush"),
        StartIconKind.Grid => Dots(size),
        StartIconKind.Search => Glyph("", size),
        _ => Squares(size, "WinxAccentBrush", rotate: true, fade: true),
    };

    /// <summary>Cuatro cuadrados: el logo de WINX (girado y degradado) o el de Windows.</summary>
    private static FrameworkElement Squares(double size, string brush, bool rotate, bool fade)
    {
        var grid = new UniformGrid { Rows = 2, Columns = 2, Width = size, Height = size };
        double[] opacity = fade ? [1, 0.75, 0.75, 0.5] : [1, 1, 1, 1];
        foreach (var o in opacity)
        {
            var square = new Rectangle
            {
                Margin = new Thickness(size / 20),
                RadiusX = rotate ? size / 10 : size / 32,
                RadiusY = rotate ? size / 10 : size / 32,
                Opacity = o,
            };
            square.SetResourceReference(Shape.FillProperty, brush);
            grid.Children.Add(square);
        }
        if (rotate)
        {
            grid.RenderTransformOrigin = new Point(0.5, 0.5);
            grid.RenderTransform = new RotateTransform(45);
        }
        return grid;
    }

    private static FrameworkElement Dots(double size)
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = size, Height = size };
        for (int i = 0; i < 9; i++)
            grid.Children.Add(Themed(new Ellipse { Width = size / 4.5, Height = size / 4.5 }, Shape.FillProperty, "WinxForegroundBrush"));
        return grid;
    }

    private static FrameworkElement Glyph(string glyph, double size)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "WinxForegroundBrush");
        return text;
    }

    private static FrameworkElement Themed(Shape shape, DependencyProperty property, string brush)
    {
        shape.SetResourceReference(property, brush);
        return shape;
    }
}
