using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Strata.Shell.Controls;

/// <summary>
/// Ícono de Archivos: una carpeta con los colores del logo de Strata UI (en una caja de 256). La
/// ventana usa además su propia identidad de app (<see cref="AppId"/>), así su botón en la barra
/// lleva la carpeta y no se agrupa con el Hub.
/// </summary>
public static class FilesIcon
{
    public const string AppId = "StrataUI.Files";

    /// <summary>Vectorial, para la barra de título.</summary>
    public static DrawingImage Image { get; } = CreateImage();

    /// <summary>En mapa de bits, para el ícono de la ventana (Windows lo pide así).</summary>
    public static BitmapSource Bitmap { get; } = Render(256);

    private static DrawingImage CreateImage()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
        // Atrás: la pestaña y el fondo de la carpeta, más oscuros.
        var back = new PathGeometry
        {
            Figures = PathFigureCollection.Parse("M 28,60 Q 28,40 48,40 L 100,40 Q 110,40 117,48 L 130,62 L 208,62 Q 228,62 228,82 L 228,196 Q 228,216 208,216 L 48,216 Q 28,216 28,196 Z"),
        };
        group.Children.Add(new GeometryDrawing(Gradient("#6E5ADB", "#3570DE"), null, back));
        // Una hoja que asoma.
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), null,
                                               new RectangleGeometry(new Rect(52, 78, 152, 90), 10, 10)));
        // Adelante: el frente de la carpeta, más claro, con el borde de arriba inclinado.
        var front = new PathGeometry
        {
            Figures = PathFigureCollection.Parse("M 28,112 Q 28,96 44,96 L 212,96 Q 228,96 228,112 L 228,196 Q 228,216 208,216 L 48,216 Q 28,216 28,196 Z"),
        };
        group.Children.Add(new GeometryDrawing(Gradient("#9A88F7", "#4E8EF0"), null, front));
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

    /// <summary>Identidad propia de la ventana (AppUserModelID): su botón de la barra es "Archivos".</summary>
    public static void ApplyAppId(IntPtr hwnd)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); // IPropertyStore
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) < 0 || store is not IPropertyStore properties)
            return;
        var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
        var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(AppId) }; // VT_LPWSTR
        try
        {
            properties.SetValue(ref key, ref value);
            properties.Commit();
        }
        finally
        {
            PropVariantClear(ref value);
            Marshal.ReleaseComObject(properties);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
