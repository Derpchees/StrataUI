using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Strata.Shell.Services;

namespace Strata.Shell.Notepad;

/// <summary>
/// Ícono del Bloc de notas: una libreta con espiral, hoja clara con renglones y la tapa con los colores
/// del logo de Strata UI (en una caja de 256). La ventana usa su propia identidad de app
/// (<see cref="AppId"/>): su botón en la barra lleva la libreta y no se agrupa con el Hub.
/// </summary>
public static class NotepadIcon
{
    public const string AppId = "StrataUI.Notepad";

    /// <summary>Vectorial, para la barra de título.</summary>
    public static DrawingImage Image { get; } = CreateImage();

    /// <summary>En mapa de bits, para el ícono de la ventana (Windows lo pide así).</summary>
    public static BitmapSource Bitmap { get; } = Render(256);

    public static void ApplyAppId(IntPtr hwnd) => WindowAppId.Apply(hwnd, AppId);

    private static DrawingImage CreateImage()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 256, 256))));

        // Hoja de atrás, un poco girada: da el grosor de la libreta.
        var back = new RectangleGeometry(new Rect(50, 46, 164, 186), 22, 22)
        {
            Transform = new RotateTransform(5, 132, 139),
        };
        group.Children.Add(new GeometryDrawing(Gradient("#6E5ADB", "#3570DE"), null, back));

        // La hoja: clara, con la tapa de color arriba.
        var page = new RectangleGeometry(new Rect(42, 40, 164, 188), 22, 22);
        group.Children.Add(new GeometryDrawing(Gradient("#FFFFFF", "#E6E9F7"), null, page));
        var cover = new PathGeometry
        {
            Figures = PathFigureCollection.Parse("M 42,62 Q 42,40 64,40 L 184,40 Q 206,40 206,62 L 206,90 L 42,90 Z"),
        };
        group.Children.Add(new GeometryDrawing(Gradient("#9A88F7", "#4E8EF0"), null, cover));

        // Renglones (el último más corto).
        var line = new SolidColorBrush(Color.FromRgb(0xA9, 0xB0, 0xD6));
        foreach (var (y, width) in new[] { (118.0, 124.0), (146.0, 124.0), (174.0, 124.0), (202.0, 72.0) })
            group.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(62, y - 5, width, 10), 5, 5)));

        // Espiral: aros oscuros que pasan por la tapa.
        var ring = new SolidColorBrush(Color.FromRgb(0x2B, 0x2D, 0x42));
        var hole = new SolidColorBrush(Color.FromRgb(0x3D, 0x3A, 0x8C));
        foreach (var x in new[] { 76.0, 108.0, 140.0, 172.0 })
        {
            group.Children.Add(new GeometryDrawing(hole, null, new EllipseGeometry(new Point(x, 70), 7, 7)));
            group.Children.Add(new GeometryDrawing(ring, null, new RectangleGeometry(new Rect(x - 5, 22, 10, 50), 5, 5)));
        }

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static LinearGradientBrush Gradient(string from, string to) =>
        new((Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to), new Point(0, 0), new Point(1, 1));

    private static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawImage(Image, new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var frame = BitmapFrame.Create(bitmap);
        frame.Freeze();
        return frame;
    }
}
