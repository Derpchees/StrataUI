using System.Globalization;
using System.IO;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Cleanup;

/// <summary>Una carpeta que se vacía: sus archivos (con el patrón) más viejos que <see cref="MinAge"/>.</summary>
public sealed record CleanupTarget(string Folder, string Pattern = "*", bool Recursive = true, TimeSpan? MinAge = null);

/// <summary>
/// Una categoría del limpiador. <see cref="DefaultOn"/>: las que no tienen costo vienen marcadas;
/// las que lo tienen (navegadores, sombreadores) lo dicen en su descripción.
/// </summary>
public sealed record CleanupCategory(
    string Id, string Name, string Description, bool NeedsAdmin, bool DefaultOn, Func<IEnumerable<CleanupTarget>> Targets);

/// <summary>Cuánto ocupa (o se liberó) en una categoría.</summary>
public readonly record struct CleanupSize(long Bytes, int Files);

/// <summary>
/// Limpieza de archivos basura con un clic: temporales, volcados e informes de errores, cachés de
/// Windows Update y de los navegadores… Nunca la papelera de reciclaje. Solo borra archivos con más
/// de un día (los de una instalación en curso siguen ahí) y se salta los que estén en uso. Lo que
/// está en carpetas de Windows lo hace un proceso elevado (<c>--clean-junk</c>, un solo UAC).
/// </summary>
public static class JunkCleaner
{
    public const string ElevatedArg = "--clean-junk";

    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static IReadOnlyList<CleanupCategory> Categories { get; } =
    [
        new("temp", Loc.T("Archivos temporales"),
            Loc.T("Lo que las apps dejan en tu carpeta temporal."), false, true,
            () => [new(Path.GetTempPath(), MinAge: OneDay)]),
        new("windows-temp", Loc.T("Temporales de Windows"),
            Loc.T("La carpeta temporal de Windows y de los servicios del sistema."), true, true,
            () => [new(Path.Combine(WindowsDir, "Temp"), MinAge: OneDay)]),
        new("crash-dumps", Loc.T("Volcados de errores"),
            Loc.T("Copias de la memoria que se guardan cuando una app o Windows se cierra con error."), true, true,
            () => [new(Path.Combine(Local, "CrashDumps")),
                   new(Path.Combine(WindowsDir, "Minidump")),
                   new(WindowsDir, "MEMORY.DMP", Recursive: false)]),
        new("error-reports", Loc.T("Informes de errores"),
            Loc.T("Informes de errores de Windows que ya se enviaron o quedaron en cola."), true, true,
            () => [new(Path.Combine(Local, @"Microsoft\Windows\WER")),
                   new(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive")),
                   new(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"))]),
        new("windows-update", Loc.T("Descargas de Windows Update"),
            Loc.T("Actualizaciones que ya se descargaron e instalaron. Si alguna falta, Windows la vuelve a bajar."), true, true,
            () => [new(Path.Combine(WindowsDir, @"SoftwareDistribution\Download"), MinAge: OneDay)]),
        new("delivery-optimization", Loc.T("Caché de Optimización de distribución"),
            Loc.T("Partes de actualizaciones que Windows guarda para compartirlas con otros equipos."), true, true,
            () => [new(Path.Combine(WindowsDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"))]),
        new("thumbnails", Loc.T("Miniaturas"),
            Loc.T("Vistas previas de imágenes y videos del Explorador; se vuelven a crear al abrir las carpetas."), false, true,
            () => [new(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", Recursive: false)]),
        new("logs", Loc.T("Registros viejos de Windows"),
            Loc.T("Registros de instalación y diagnóstico de más de una semana."), true, false,
            () => [new(Path.Combine(WindowsDir, "Logs"), "*.log", MinAge: TimeSpan.FromDays(7)),
                   new(Path.Combine(WindowsDir, "Logs"), "*.cab", MinAge: TimeSpan.FromDays(7)),
                   new(Path.Combine(WindowsDir, "Panther"), "*.log", MinAge: TimeSpan.FromDays(7))]),
        new("browsers", Loc.T("Caché de los navegadores"),
            Loc.T("Chrome, Edge, Brave, Opera y Firefox. Las páginas tardan un poco más la primera vez; no se borran contraseñas, historial ni sesiones."), false, false,
            BrowserCaches),
        new("shaders", Loc.T("Caché de sombreadores"),
            Loc.T("DirectX, NVIDIA y AMD. Ojo: los juegos se traban un rato mientras la vuelven a crear."), false, false,
            () => [new(Path.Combine(Local, "D3DSCache")),
                   new(Path.Combine(Local, @"NVIDIA\DXCache")),
                   new(Path.Combine(Local, @"NVIDIA\GLCache")),
                   new(Path.Combine(Local, @"AMD\DxCache")),
                   new(Path.Combine(Local, @"AMD\DxcCache")),
                   new(Path.Combine(Local, @"AMD\GLCache")),
                   new(Path.Combine(Local, @"AMD\VkCache"))]),
    ];

    public static CleanupCategory? Find(string id) => Categories.FirstOrDefault(c => c.Id == id);

    /// <summary>Carpetas de caché de los perfiles de los navegadores Chromium y de Firefox.</summary>
    private static IEnumerable<CleanupTarget> BrowserCaches()
    {
        string[] chromium =
        [
            @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data",
            @"Vivaldi\User Data", @"Chromium\User Data",
        ];
        var roots = chromium.Select(r => Path.Combine(Local, r))
            .Concat([Path.Combine(Roaming, @"Opera Software\Opera Stable"), Path.Combine(Roaming, @"Opera Software\Opera GX Stable")]);
        foreach (var root in roots.Where(Directory.Exists))
        {
            // Cada perfil ("Default", "Profile 1"…) tiene sus propias cachés; Opera las tiene en la raíz.
            IEnumerable<string> profiles;
            try
            {
                profiles = Directory.EnumerateDirectories(root).Append(root).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var profile in profiles)
                foreach (var cache in new[] { @"Cache\Cache_Data", "Code Cache", "GPUCache" })
                    yield return new CleanupTarget(Path.Combine(profile, cache));
        }
        var firefox = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefox))
        {
            foreach (var profile in SafeDirectories(firefox))
                yield return new CleanupTarget(Path.Combine(profile, "cache2"));
        }
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ── Medir y limpiar ─────────────────────────────────────────────────────

    public static CleanupSize Measure(CleanupCategory category) => Walk(category, delete: false);

    public static CleanupSize Clean(CleanupCategory category)
    {
        var freed = Walk(category, delete: true);
        Log.Info($"Limpieza '{category.Id}': {freed.Files} archivos, {freed.Bytes / 1048576.0:0.0} MB");
        return freed;
    }

    private static CleanupSize Walk(CleanupCategory category, bool delete)
    {
        long bytes = 0;
        int files = 0;
        var now = DateTime.Now;
        foreach (var target in category.Targets())
        {
            if (!Directory.Exists(target.Folder))
                continue;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = target.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, // ni enlaces ni archivos del sistema
            };
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(target.Folder, target.Pattern, options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var path in paths)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (target.MinAge is { } age && now - info.LastWriteTime < age)
                        continue;
                    long length = info.Length;
                    if (delete)
                    {
                        if (info.IsReadOnly)
                            info.IsReadOnly = false;
                        info.Delete();
                    }
                    bytes += length;
                    files++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // En uso o protegido: se queda.
                }
            }
            if (delete && target.Recursive)
                DeleteEmptyFolders(target.Folder);
        }
        return new CleanupSize(bytes, files);
    }

    /// <summary>Quita las subcarpetas que quedaron vacías (nunca la carpeta de la categoría).</summary>
    private static void DeleteEmptyFolders(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                     }).OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ── Proceso elevado ─────────────────────────────────────────────────────

    /// <summary>
    /// StrataUI.exe --clean-junk id1,id2 archivo: limpia solo categorías conocidas y escribe en el
    /// archivo (en la carpeta temporal) los bytes y archivos liberados.
    /// </summary>
    internal static int RunElevated(string[] args)
    {
        if (args.Length != 3)
            return 2;
        var ids = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
        var categories = ids.Select(Find).ToList();
        if (categories.Count == 0 || categories.Any(c => c is null))
            return 2;
        var result = Path.GetFullPath(args[2]);
        if (!Path.GetFileName(result).StartsWith("strataui-clean-", StringComparison.Ordinal)
            || Path.GetFileName(Path.GetDirectoryName(result)) is not { } folder || !folder.Equals("Temp", StringComparison.OrdinalIgnoreCase)
            || !result.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return 2;
        long bytes = 0;
        int files = 0;
        foreach (var category in categories)
        {
            var freed = Clean(category!);
            bytes += freed.Bytes;
            files += freed.Files;
        }
        File.WriteAllText(result, string.Create(CultureInfo.InvariantCulture, $"{bytes} {files}"));
        return 0;
    }

    /// <summary>
    /// Limpia las categorías: las del usuario aquí y las de Windows con un solo UAC. Devuelve lo
    /// liberado o null si se canceló el permiso (lo del usuario igual se limpió).
    /// </summary>
    public static async Task<(CleanupSize Freed, bool Cancelled)> CleanAsync(IReadOnlyList<CleanupCategory> categories)
    {
        long bytes = 0;
        int files = 0;
        var local = categories.Where(c => !c.NeedsAdmin || Tweaks.Elevation.IsAdmin).ToList();
        var elevated = categories.Except(local).ToList();
        await Task.Run(() =>
        {
            foreach (var category in local)
            {
                var freed = Clean(category);
                bytes += freed.Bytes;
                files += freed.Files;
            }
        });
        bool cancelled = false;
        if (elevated.Count > 0)
        {
            var result = Path.Combine(Path.GetTempPath(), $"strataui-clean-{Guid.NewGuid():N}.txt");
            int code = await Tweaks.Elevation.RunSelfAsync(ElevatedArg, string.Join(',', elevated.Select(c => c.Id)), result);
            cancelled = code == Tweaks.Elevation.Cancelled;
            try
            {
                if (File.Exists(result))
                {
                    var parts = File.ReadAllText(result).Split(' ');
                    bytes += long.Parse(parts[0], CultureInfo.InvariantCulture);
                    files += int.Parse(parts[1], CultureInfo.InvariantCulture);
                    File.Delete(result);
                }
            }
            catch (Exception ex) when (ex is IOException or FormatException or IndexOutOfRangeException)
            {
                Log.Error("No se pudo leer el resultado de la limpieza", ex);
            }
        }
        return (new CleanupSize(bytes, files), cancelled);
    }
}
