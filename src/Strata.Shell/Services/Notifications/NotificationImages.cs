using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Imágenes de las notificaciones: las del disco al momento y las de internet (las fotos de
/// contacto y los stickers de WhatsApp vienen por https, como los muestra Windows) descargadas una
/// vez y guardadas en memoria.
/// </summary>
internal static class NotificationImages
{
    private const int MaxBytes = 8 * 1024 * 1024;

    private static readonly Dictionary<string, Task<ImageSource?>> Remote = new();
    private static HttpClient? _http;

    /// <summary>Carga la imagen y la entrega a <paramref name="set"/> (en el hilo de la UI).</summary>
    public static async Task SetAsync(string source, int width, Action<ImageSource> set)
    {
        if (!source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (AppIcons.Load(source, width) is { } local)
                set(local);
            return;
        }
        string key = width + "|" + source;
        if (!Remote.TryGetValue(key, out var task))
        {
            if (Remote.Count > 200)
                Remote.Clear();
            Remote[key] = task = DownloadAsync(source, width);
        }
        if (await task is { } image)
            set(image);
    }

    private static async Task<ImageSource?> DownloadAsync(string url, int width)
    {
        try
        {
            _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > MaxBytes)
                return null;
            return await Task.Run(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = width;
                bitmap.EndInit();
                bitmap.Freeze();
                return (ImageSource)bitmap;
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException
                                       or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Info($"No se pudo cargar una imagen de notificación: {ex.Message}");
            return null;
        }
    }
}
