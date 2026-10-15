using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Lo que Strata UI necesita saber de Wallpaper Engine: si está cambiando el color de Windows
/// (Configuración › General › "Adjust Windows color") y qué fondo tiene puesto. Lo lee de su
/// config.json, que Wallpaper Engine reescribe al cambiar una opción o el fondo; un
/// <see cref="FileSystemWatcher"/> avisa de esos cambios con <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// Con "Adjust Windows color" Wallpaper Engine no calcula el color: copia el "schemecolor" que el
/// autor de cada fondo puso en su project.json (a menudo "0 0 0", y el acento queda negro).
/// </remarks>
public static class WallpaperEngine
{
    private static FileSystemWatcher? _watcher;
    private static string? _folder;
    private static System.Windows.Threading.DispatcherTimer? _debounce;

    /// <summary>Cambió su configuración o el fondo (en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>Carpeta de instalación (la del proceso en ejecución o la de Steam); null si no está.</summary>
    public static string? Folder
    {
        get
        {
            if (RunningFolder() is { } running)
                _folder = running;
            if (_folder is null)
                _folder = SteamFolder();
            return _folder;
        }
    }

    public static bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")).ToList();
            processes.ForEach(p => p.Dispose());
            return processes.Count > 0;
        }
    }

    /// <summary>Wallpaper Engine está abierto y ajusta el color de acento de Windows con cada fondo.</summary>
    public static bool AdjustsWindowsColor
    {
        get
        {
            if (!IsRunning)
                return false;
            using var config = ReadConfig();
            return AdjustsColor(config);
        }
    }

    /// <summary>
    /// Último valor de <see cref="AdjustsWindowsColor"/> (leerlo cuesta: procesos y archivo). Se
    /// actualiza cuando cambia su config.json y con <see cref="Recheck"/>.
    /// </summary>
    public static bool AdjustingWindowsColor { get; private set; }

    /// <summary>Vuelve a mirar si ajusta el color (p. ej. se abrió o se cerró); avisa si cambió.</summary>
    public static void Recheck()
    {
        bool adjusting = AdjustsWindowsColor;
        if (adjusting == AdjustingWindowsColor)
            return;
        AdjustingWindowsColor = adjusting;
        Log.Info(adjusting
            ? "Wallpaper Engine ajusta el color de Windows: el acento del fondo de Strata UI queda en pausa"
            : "Wallpaper Engine ya no ajusta el color de Windows");
        Changed?.Invoke();
    }

    /// <summary>Empieza a vigilar su config.json (si está instalado).</summary>
    public static void Watch()
    {
        if (_watcher != null || Folder is not { } folder || !Directory.Exists(folder))
            return;
        try
        {
            _watcher = new FileSystemWatcher(folder, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Raise();
            _watcher.Created += (_, _) => Raise();
            _watcher.Renamed += (_, _) => Raise();
        }
        catch (Exception ex)
        {
            Log.Info($"No se pudo vigilar la configuración de Wallpaper Engine: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Wallpaper Engine escribe el archivo varias veces seguidas: se avisa una sola vez.</summary>
    private static void Raise() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        _debounce ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(600),
            System.Windows.Threading.DispatcherPriority.Background, (s, _) =>
            {
                ((System.Windows.Threading.DispatcherTimer)s!).Stop();
                AdjustingWindowsColor = AdjustsWindowsColor;
                Changed?.Invoke();
            }, System.Windows.Application.Current.Dispatcher);
        _debounce.Stop();
        _debounce.Start();
    });

    /// <summary>
    /// Imagen que representa el fondo actual: la vista previa del proyecto (preview.jpg/gif) o el
    /// archivo mismo si es una imagen. Null si no está abierto o no hay fondo elegido.
    /// </summary>
    public static string? CurrentWallpaperImage()
    {
        if (!IsRunning)
            return null;
        using var config = ReadConfig();
        if (config is null)
            return null;
        foreach (var file in SelectedFiles(config))
        {
            try
            {
                string path = file.Replace('/', '\\');
                if (IsImage(path) && File.Exists(path))
                    return path;
                var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (dir is null || !Directory.Exists(dir))
                    continue;
                foreach (var name in new[] { "preview.jpg", "preview.png", "preview.gif", "preview.jpeg" })
                {
                    var preview = Path.Combine(dir, name);
                    if (File.Exists(preview))
                        return preview;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
            }
        }
        return null;
    }

    /// <summary>"adjustdwmcolormode": "accent" o "everything" lo activan; "disabled" (o nada), no.</summary>
    internal static bool AdjustsColor(JsonDocument? config)
    {
        if (config is null)
            return false;
        foreach (var user in Users(config))
        {
            if (user.TryGetProperty("general", out var general) && general.TryGetProperty("user", out var prefs)
                && prefs.TryGetProperty("adjustdwmcolormode", out var mode) && mode.ValueKind == JsonValueKind.String)
            {
                var value = mode.GetString()?.Trim().ToLowerInvariant();
                if (value is { Length: > 0 } and not ("disabled" or "none" or "off" or "false"))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Archivos de los fondos elegidos en cada monitor ("selectedwallpapers").</summary>
    internal static IEnumerable<string> SelectedFiles(JsonDocument config)
    {
        foreach (var user in Users(config))
        {
            if (!user.TryGetProperty("general", out var general)
                || !general.TryGetProperty("wallpaperconfig", out var wallpapers)
                || !wallpapers.TryGetProperty("selectedwallpapers", out var selected)
                || selected.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var monitor in selected.EnumerateObject().OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (monitor.Value.ValueKind == JsonValueKind.Object && monitor.Value.TryGetProperty("file", out var file)
                    && file.GetString() is { Length: > 0 } path)
                    yield return path;
            }
        }
    }

    /// <summary>Cada usuario de Windows tiene su sección (el nombre del equipo o la cuenta).</summary>
    private static IEnumerable<JsonElement> Users(JsonDocument config) =>
        config.RootElement.ValueKind != JsonValueKind.Object
            ? []
            : config.RootElement.EnumerateObject()
                .Where(p => !p.Name.StartsWith('?') && p.Value.ValueKind == JsonValueKind.Object)
                .Select(p => p.Value);

    private static JsonDocument? ReadConfig()
    {
        if (Folder is not { } folder)
            return null;
        try
        {
            var file = Path.Combine(folder, "config.json");
            if (!File.Exists(file))
                return null;
            // Wallpaper Engine puede estar escribiéndolo: se lee compartido.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Info($"No se pudo leer la configuración de Wallpaper Engine: {ex.Message}");
            return null;
        }
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp";

    private static string? RunningFolder()
    {
        foreach (var process in Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } exe)
                        return Path.GetDirectoryName(exe);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Elevado o terminando: no se puede leer su ruta.
                }
            }
        }
        return null;
    }

    private static string? SteamFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string steam)
            return null;
        var folder = Path.Combine(steam.Replace('/', '\\'), "steamapps", "common", "wallpaper_engine");
        return Directory.Exists(folder) ? folder : null;
    }
}
