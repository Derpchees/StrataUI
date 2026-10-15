using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// Íconos elegidos con "Cambiar ícono" para apps y anclados de Inicio
/// (<see cref="StrataSettings.StartCustomIcons"/>: ID de la app → PNG en %LOCALAPPDATA%\StrataUI\start-icons).
/// La imagen elegida se copia, así sigue aunque el archivo original se borre.
/// </summary>
public static class CustomIcons
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "start-icons");

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cambió el ícono de una app (su ID).</summary>
    public static event Action<string>? Changed;

    /// <summary>Formatos que acepta el diálogo de "Cambiar ícono".</summary>
    public const string DialogFilter = "*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll";

    public static bool Has(string appId) => Settings.Current.StartCustomIcons.ContainsKey(appId);

    public static ImageSource? Get(string appId)
    {
        if (!Settings.Current.StartCustomIcons.TryGetValue(appId, out var path))
            return null;
        if (!Cache.TryGetValue(path, out var image))
            Cache[path] = image = Load(path);
        return image;
    }

    /// <summary>Usa como ícono de la app una imagen o el ícono de un .ico, .exe o .dll.</summary>
    public static bool Set(string appId, string source)
    {
        try
        {
            if (Extract(source) is not { } bitmap)
                return false;
            Directory.CreateDirectory(Folder);
            var target = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".png");
            using (var stream = File.Create(target))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
            DeleteFile(appId);
            Settings.Current.StartCustomIcons[appId] = target;
            Settings.Save();
            Changed?.Invoke(appId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo usar {source} como ícono", ex);
            return false;
        }
    }

    /// <summary>Vuelve al ícono de Windows.</summary>
    public static void Reset(string appId)
    {
        if (!Has(appId))
            return;
        DeleteFile(appId);
        Settings.Current.StartCustomIcons.Remove(appId);
        Settings.Save();
        Changed?.Invoke(appId);
    }

    private static void DeleteFile(string appId)
    {
        if (!Settings.Current.StartCustomIcons.TryGetValue(appId, out var old))
            return;
        Cache.Remove(old);
        try
        {
            // Solo los que copió Strata UI: nunca un archivo de otro lugar.
            if (Path.GetDirectoryName(old) is { } dir && dir.Equals(Folder, StringComparison.OrdinalIgnoreCase))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ImageSource? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 128;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo cargar el ícono {path}", ex);
            return null;
        }
    }

    /// <summary>Imagen de hasta 256 px: el ícono de un .ico/.exe/.dll o la imagen escalada.</summary>
    private static BitmapSource? Extract(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is ".ico" or ".exe" or ".dll")
        {
            var icons = new IntPtr[1];
            if (PrivateExtractIcons(source, 0, 256, 256, icons, null, 1, 0) == 0 || icons[0] == IntPtr.Zero)
                return null;
            try
            {
                var icon = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                return icon;
            }
            finally
            {
                DestroyIcon(icons[0]);
            }
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(source);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        if (image.PixelWidth <= 256 && image.PixelHeight <= 256)
            return image;
        double scale = 256.0 / Math.Max(image.PixelWidth, image.PixelHeight);
        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, int[]? ids, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
