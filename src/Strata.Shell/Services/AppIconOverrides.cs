using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ManagedShell.WindowsTasks;

namespace Strata.Shell.Services;

/// <summary>
/// Apps que dejan elegir su propio ícono: la barra muestra el elegido en vez del del ejecutable.
/// Vesktop guarda los de su personalización en <c>%APPDATA%\vesktop\userAssets</c> (<c>tray</c> y
/// <c>trayUnread</c>, sin extensión); el de no leídos se usa cuando el título lo indica ("(3) Discord",
/// "• Discord"). Si se cambian en Vesktop, la barra se actualiza sola.
/// </summary>
public static partial class AppIconOverrides
{
    private static readonly string VesktopAssets =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vesktop", "userAssets");

    private static readonly Dictionary<string, (DateTime Written, ImageSource? Image)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static FileSystemWatcher? _watcher;

    /// <summary>Cambió algún ícono elegido (los botones se vuelven a dibujar). Llega en el hilo de la interfaz.</summary>
    public static event Action? Changed;

    /// <summary>El ícono elegido para esta ventana; null si su app no tiene uno.</summary>
    public static ImageSource? For(ApplicationWindow? window) =>
        window is not null && IsVesktop(window.WinFileName) ? Vesktop(Unread(window.Title)) : null;

    /// <summary>El de un anclado sin ventana abierta (por su ID: ruta o AppUserModelID).</summary>
    public static ImageSource? ForPin(string? id) =>
        id is not null && id.Contains("vesktop", StringComparison.OrdinalIgnoreCase) ? Vesktop(unread: false) : null;

    private static bool IsVesktop(string? exe) =>
        Path.GetFileName(exe ?? "").Equals("vesktop.exe", StringComparison.OrdinalIgnoreCase);

    internal static bool Unread(string? title) => title is { Length: > 0 } && UnreadTitle().IsMatch(title);

    [GeneratedRegex(@"^\s*(\(\d+\+?\)|•)")]
    private static partial Regex UnreadTitle();

    private static ImageSource? Vesktop(bool unread)
    {
        Watch();
        return (unread ? Load(Path.Combine(VesktopAssets, "trayUnread")) : null) ?? Load(Path.Combine(VesktopAssets, "tray"));
    }

    private static ImageSource? Load(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var written = File.GetLastWriteTimeUtc(file);
            lock (Cache)
            {
                if (Cache.TryGetValue(file, out var cached) && cached.Written == written)
                    return cached.Image;
            }
            var image = AppIcons.Load(file, 64);
            lock (Cache)
                Cache[file] = (written, image);
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Vigila la carpeta de Vesktop (la primera vez que se pide uno de sus íconos).</summary>
    private static void Watch()
    {
        if (_watcher != null || !Directory.Exists(VesktopAssets) || System.Windows.Application.Current is not { } app)
            return;
        try
        {
            var watcher = new FileSystemWatcher(VesktopAssets) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, _) => app.Dispatcher.BeginInvoke(() =>
            {
                lock (Cache)
                    Cache.Clear();
                Changed?.Invoke();
            });
            watcher.Created += changed;
            watcher.Changed += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (s, e) => changed(s, e);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
    }
}
