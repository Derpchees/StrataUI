using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Strata.Shell.Services.Files;

/// <summary>Un GIF animado ya armado: cada cuadro completo y cuánto dura.</summary>
public sealed record GifAnimation(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays);

/// <summary>
/// Vistas de lo que WPF no dibuja solo: SVG (SharpVectors), GIF animados (cuadro por cuadro) y
/// páginas de PDF (el lector de PDF de Windows, <c>Windows.Data.Pdf</c>). Crean objetos de WPF:
/// llamar en un hilo STA (el de los íconos, <see cref="FileIcons.RunAsync"/>).
/// </summary>
public static class Previews
{
    /// <summary>El SVG como imagen, con su lado más largo de <paramref name="size"/> píxeles; null si no se puede leer.</summary>
    public static BitmapSource? Svg(string path, int size)
    {
        try
        {
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = false };
            using var reader = new FileSvgReader(settings);
            if (reader.Read(path) is not { } drawing || drawing.Bounds.IsEmpty || drawing.Bounds.Width <= 0 || drawing.Bounds.Height <= 0)
                return null;
            var bounds = drawing.Bounds;
            double scale = size / Math.Max(bounds.Width, bounds.Height);
            int width = Math.Max(1, (int)(bounds.Width * scale)), height = Math.Max(1, (int)(bounds.Height * scale));
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(scale, scale));
                context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                context.DrawDrawing(drawing);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Info($"Vista previa: no se pudo dibujar el SVG {Path.GetFileName(path)} ({ex.Message.Trim()})");
            return null;
        }
    }

    /// <summary>
    /// Los cuadros de un GIF animado, compuestos como los muestra un navegador (cada cuadro se dibuja
    /// sobre lo anterior en su posición, y se borra o se restaura según pide). Null si tiene un solo
    /// cuadro o no se puede leer; como mucho ~250 MB de cuadros.
    /// </summary>
    public static GifAnimation? Gif(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count < 2)
                return null;
            int width = Query<ushort>(decoder.Metadata, "/logscrdesc/Width") is var w and > 0 ? w : decoder.Frames[0].PixelWidth;
            int height = Query<ushort>(decoder.Metadata, "/logscrdesc/Height") is var h and > 0 ? h : decoder.Frames[0].PixelHeight;
            long budget = 250L << 20;
            int max = (int)Math.Clamp(budget / Math.Max(1L, (long)width * height * 4), 2, 600);

            var frames = new List<BitmapSource>();
            var delays = new List<TimeSpan>();
            BitmapSource? previous = null;
            foreach (var frame in decoder.Frames.Take(max))
            {
                var metadata = frame.Metadata as BitmapMetadata;
                double left = Query<ushort>(metadata, "/imgdesc/Left"), top = Query<ushort>(metadata, "/imgdesc/Top");
                int delay = Query<ushort>(metadata, "/grctlext/Delay");
                int disposal = Query<byte>(metadata, "/grctlext/Disposal");
                var area = new Rect(left, top, frame.PixelWidth, frame.PixelHeight);

                var composed = Compose(width, height, previous, null, frame, area);
                frames.Add(composed);
                // Como los navegadores: 0 o 1 centésimas se muestran a 100 ms.
                delays.Add(TimeSpan.FromMilliseconds(delay <= 1 ? 100 : delay * 10));
                previous = disposal switch
                {
                    2 => Compose(width, height, composed, area, null, default), // se borra su área
                    3 => previous,                                               // se vuelve a lo de antes
                    _ => composed,
                };
            }
            return new GifAnimation(frames, delays);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException
                                       or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Info($"Vista previa: no se pudo leer el GIF {Path.GetFileName(path)} ({ex.Message.Trim()})");
            return null;
        }
    }

    /// <summary>Lo anterior (sin el área <paramref name="erase"/>, si se pide) y encima el cuadro nuevo.</summary>
    private static BitmapSource Compose(int width, int height, BitmapSource? below, Rect? erase, BitmapSource? frame, Rect area)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (below != null)
            {
                if (erase is { } hole)
                    context.PushClip(Geometry.Combine(new RectangleGeometry(new Rect(0, 0, width, height)),
                                                      new RectangleGeometry(hole), GeometryCombineMode.Exclude, null));
                context.DrawImage(below, new Rect(0, 0, width, height));
                if (erase != null)
                    context.Pop();
            }
            if (frame != null)
                context.DrawImage(frame, area);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static T Query<T>(BitmapMetadata? metadata, string query) where T : struct
    {
        try
        {
            return metadata?.GetQuery(query) is T value ? value : default;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return default;
        }
    }

    /// <summary>Abre un PDF con el lector de Windows (null si no se puede).</summary>
    public static async Task<Windows.Data.Pdf.PdfDocument?> OpenPdfAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            return await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex)
        {
            Log.Info($"Vista previa: no se pudo abrir el PDF {Path.GetFileName(path)} ({ex.Message.Trim()})");
            return null;
        }
    }

    /// <summary>Una página del PDF como imagen de <paramref name="width"/> píxeles de ancho (fondo blanco, como el papel).</summary>
    public static async Task<BitmapSource?> PdfPageAsync(Windows.Data.Pdf.PdfDocument document, uint index, uint width)
    {
        try
        {
            using var page = document.GetPage(index);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = width,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            });
            var memory = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(memory);
            memory.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = memory;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Info($"Vista previa: no se pudo dibujar la página {index + 1} del PDF ({ex.Message.Trim()})");
            return null;
        }
    }
}
