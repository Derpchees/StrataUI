using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Strata.Shell.Services.Emoji;

/// <summary>
/// Emojis a color como imágenes. WPF dibuja la fuente de emojis de Windows en blanco y negro; aquí
/// los dibuja DirectWrite (con Direct2D, sobre un mapa de bits WIC), igual que el resto de Windows,
/// y se guardan por emoji. Sin internet: es la fuente Segoe UI Emoji del sistema.
/// </summary>
public static class EmojiRenderer
{
    /// <summary>Lado de la imagen en píxeles: nítida hasta ~2× el tamaño en que se muestra.</summary>
    private const int Pixels = 48;

    private static readonly Dictionary<string, ImageSource?> Cache = new();
    private static IWICImagingFactory? _wic;
    private static ID2D1Factory? _d2d;
    private static IDWriteTextFormat? _format;
    private static bool _failed;

    /// <summary>La imagen del emoji (null si no se pudo dibujar: se muestra como texto).</summary>
    public static ImageSource? Get(string emoji)
    {
        if (_failed)
            return null;
        if (Cache.TryGetValue(emoji, out var cached))
            return cached;
        ImageSource? image = null;
        try
        {
            image = Render(emoji);
        }
        catch (Exception ex)
        {
            // Sin Direct2D (sesión remota rara, controlador roto): los emojis quedan como texto.
            _failed = true;
            Log.Error("No se pudieron dibujar los emojis a color", ex);
        }
        if (Cache.Count > 500)
            Cache.Clear();
        return Cache[emoji] = image;
    }

    private static BitmapSource Render(string emoji)
    {
        _wic ??= new IWICImagingFactory();
        _d2d ??= D2D1.D2D1CreateFactory<ID2D1Factory>();
        if (_format is null)
        {
            using var dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
            // El tamaño de letra deja margen arriba y abajo para el emoji entero.
            _format = dwrite.CreateTextFormat("Segoe UI Emoji", FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal,
                                              FontStretch.Normal, Pixels * 0.74f);
            _format.TextAlignment = TextAlignment.Center;
            _format.ParagraphAlignment = ParagraphAlignment.Center;
            _format.WordWrapping = WordWrapping.NoWrap;
        }
        using var bitmap = _wic.CreateBitmap(Pixels, Pixels, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var properties = new RenderTargetProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm,
                                                                                    Vortice.DCommon.AlphaMode.Premultiplied));
        using (var target = _d2d.CreateWicBitmapRenderTarget(bitmap, properties))
        using (var brush = target.CreateSolidColorBrush(new Color4(0.5f, 0.5f, 0.5f, 1)))
        {
            target.BeginDraw();
            target.Clear(new Color4(0, 0, 0, 0));
            target.DrawText(emoji, _format, new Rect(0, 0, Pixels, Pixels), brush, DrawTextOptions.EnableColorFont);
            target.EndDraw();
        }
        int stride = Pixels * 4;
        var pixels = new byte[stride * Pixels];
        bitmap.CopyPixels((uint)stride, pixels);
        var source = BitmapSource.Create(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
