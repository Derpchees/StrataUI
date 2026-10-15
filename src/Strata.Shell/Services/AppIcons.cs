using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ManagedShell.WindowsTasks;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>
/// Ícono y nombre de una app a partir de su identidad (AppUserModelID), para notificaciones y
/// multimedia. Busca, en orden: el catálogo del menú Inicio (por identidad, por el ejecutable y por
/// el nombre), el registro de identidades de Windows (HKCU/HKLM\Software\Classes\AppUserModelId,
/// donde las apps de escritorio dejan DisplayName e IconUri) y las ventanas abiertas con esa identidad.
/// </summary>
public static class AppIcons
{
    public static ImageSource? Find(string? appId, string? appName)
    {
        var apps = StartMenu.StartMenuService.Apps;
        if (!string.IsNullOrEmpty(appId))
        {
            var app = apps.FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase))
                      ?? apps.FirstOrDefault(a => Path.GetFileName(a.Id).Equals(appId, StringComparison.OrdinalIgnoreCase));
            if (app?.Icon is { } icon)
                return icon;
        }
        if (!string.IsNullOrWhiteSpace(appName)
            && apps.FirstOrDefault(a => a.Name.Equals(appName, StringComparison.CurrentCultureIgnoreCase))?.Icon is { } byName)
            return byName;
        if (!string.IsNullOrEmpty(appId))
        {
            if (RegisteredIcon(appId) is { } registered)
                return registered;
            if (WindowIcon(appId) is { } window)
                return window;
        }
        return null;
    }

    /// <summary>Nombre que la app registró para sus notificaciones (si no es un recurso "@dll,-id").</summary>
    public static string? RegisteredName(string appId) =>
        Registered(appId, "DisplayName") is { } name && !name.StartsWith('@') ? name : null;

    private static ImageSource? RegisteredIcon(string appId)
    {
        if (Registered(appId, "IconUri") is not { } uri)
            return null;
        string path = Environment.ExpandEnvironmentVariables(uri);
        return Path.IsPathFullyQualified(path) && File.Exists(path) ? Load(path) : null;
    }

    private static string? Registered(string appId, string value)
    {
        try
        {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = root.OpenSubKey(@"Software\Classes\AppUserModelId\" + appId);
                if (key?.GetValue(value) is string text && text.Length > 0)
                    return text;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or ArgumentException or IOException)
        {
        }
        return null;
    }

    /// <summary>Ícono de una ventana abierta de esa app (por su identidad o su ejecutable).</summary>
    private static ImageSource? WindowIcon(string appId)
    {
        try
        {
            return App.Current.Shell.Tasks.GroupedWindows.OfType<ApplicationWindow>()
                .FirstOrDefault(w => string.Equals(w.AppUserModelID, appId, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetFileName(w.WinFileName), appId, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(w.WinFileName, appId, StringComparison.OrdinalIgnoreCase))
                ?.Icon;
        }
        catch (InvalidOperationException)
        {
            return null; // el shell aún no arrancó
        }
    }

    /// <summary>Imagen de un archivo (png, jpg, ico…), sin dejarlo bloqueado.</summary>
    public static ImageSource? Load(string path, int width = 64)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                // Del .ico, el tamaño más cercano al pedido.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - width)).First();
                frame.Freeze();
                return frame;
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = width;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                       or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
