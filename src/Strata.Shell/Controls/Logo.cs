using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Strata.Shell.Controls;

/// <summary>
/// Logo de Strata UI dibujado como vector (el mismo de scripts/build-logo.ps1, en una caja de 256):
/// tres capas isométricas con un hueco entre ellas. Dentro de la app se usa esto y no el .ico, que
/// WPF estira desde uno de sus tamaños chicos.
/// </summary>
public static class Logo
{
    /// <summary>Capas de abajo a arriba, ya recortadas.</summary>
    private static readonly Geometry[] Layers = Build();

    /// <summary>El logo con sus colores (portada del Hub, avisos propios).</summary>
    public static DrawingImage Image { get; } = CreateImage();

    private static Geometry[] Build()
    {
        var top = Layer(92);
        var middle = Layer(130);
        var bottom = Layer(168);
        Geometry[] layers = [Cut(bottom, middle), Cut(middle, top), top];
        foreach (var layer in layers)
            layer.Freeze();
        return layers;
    }

    /// <summary>Rombo de esquinas redondeadas (un cuadrado girado y aplastado), agrandado para llenar la caja.</summary>
    private static Geometry Layer(double centerY)
    {
        const double side = 124.5;
        var geometry = new RectangleGeometry(new Rect(-side / 2, -side / 2, side, side), 18, 18);
        var transform = new TransformGroup();
        transform.Children.Add(new RotateTransform(45));
        transform.Children.Add(new ScaleTransform(1, 0.56));
        transform.Children.Add(new TranslateTransform(128, centerY - 2));
        transform.Children.Add(new ScaleTransform(1.36, 1.36, 128, 130));
        geometry.Transform = transform;
        return geometry.GetFlattenedPathGeometry();
    }

    /// <summary>Quita de una capa lo que tapa la de encima, más un hueco alrededor.</summary>
    private static Geometry Cut(Geometry layer, Geometry over)
    {
        var pen = new Pen(Brushes.Black, 24) { LineJoin = PenLineJoin.Round };
        var hole = Geometry.Combine(over, over.GetWidenedPathGeometry(pen), GeometryCombineMode.Union, null);
        return Geometry.Combine(layer, hole, GeometryCombineMode.Exclude, null);
    }

    private static DrawingImage CreateImage()
    {
        (string From, string To)[] colors = [("#6E5ADB", "#3570DE"), ("#8F7BF2", "#4E8EF0"), ("#B9A6FF", "#6CB2FF")];
        var group = new DrawingGroup();
        // Caja completa transparente: así la imagen mide 256×256 y el logo queda centrado.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
        for (int i = 0; i < Layers.Length; i++)
        {
            var brush = new LinearGradientBrush(Parse(colors[i].From), Parse(colors[i].To), new Point(0, 0), new Point(1, 1));
            group.Children.Add(new GeometryDrawing(brush, null, Layers[i]));
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>El logo con el color de acento del tema: la capa de arriba llena y las de abajo más tenues.</summary>
    public static FrameworkElement Themed(double size)
    {
        var canvas = new Canvas { Width = 256, Height = 256 };
        double[] opacity = [0.5, 0.75, 1];
        for (int i = 0; i < Layers.Length; i++)
        {
            var path = new Path { Data = Layers[i], Opacity = opacity[i] };
            path.SetResourceReference(Shape.FillProperty, "StrataAccentBrush");
            canvas.Children.Add(path);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
