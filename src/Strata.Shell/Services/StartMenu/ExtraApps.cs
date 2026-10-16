using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// Una app que no está en shell:AppsFolder: <paramref name="Id"/> es lo que se abre (steam://…, un
/// .exe o el acceso directo) y <paramref name="IconSource"/> el archivo del que sale el ícono.
/// </summary>
public sealed record ExtraApp(string Id, string Name, string IconSource, string Launch, string? WorkingDirectory);

/// <summary>
/// Lo instalado que Windows no pone en "Todas las apps" porque no dejó acceso directo en el menú
/// Inicio: juegos de Steam, Epic y GOG (sus lanzadores no los crean o solo en el escritorio) y los
/// accesos directos del escritorio a programas o juegos. Se lee en segundo plano.
/// </summary>
public static partial class ExtraApps
{
    public static List<ExtraApp> Scan()
    {
        var apps = new List<ExtraApp>();
        Try("Steam", () => apps.AddRange(Steam()));
        Try("Epic", () => apps.AddRange(Epic()));
        Try("GOG", () => apps.AddRange(Gog()));
        Try("escritorio", () => apps.AddRange(DesktopShortcuts()));
        // Un juego puede venir de su biblioteca y de su acceso directo: queda el primero (la biblioteca).
        return apps.DistinctBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>"Monitor ASD (1)": la copia de un acceso directo se llama como el original.</summary>
    [GeneratedRegex(@"\s*\(\d+\)$")]
    private static partial Regex CopySuffix();

    private static string ShortcutName(string file) => CopySuffix().Replace(Path.GetFileNameWithoutExtension(file), "");

    /// <summary>Carpetas a vigilar para notar juegos nuevos o desinstalados.</summary>
    public static IEnumerable<string> WatchedFolders()
    {
        foreach (var library in SteamLibraries())
            yield return Path.Combine(library, "steamapps");
        if (Directory.Exists(EpicManifests))
            yield return EpicManifests;
    }

    private static void Try(string source, Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            Log.Error($"No se pudieron leer las apps de {source}", ex);
        }
    }

    // ── Steam ───────────────────────────────────────────────────────────────

    /// <summary>Herramientas de Steam que no son juegos (redistribuibles, Proton, servidores…).</summary>
    [GeneratedRegex(@"Redistributable|Steamworks|Proton|Steam Linux Runtime|Dedicated Server|SDK\b|SteamVR", RegexOptions.IgnoreCase)]
    private static partial Regex SteamTool();

    private static string? SteamPath =>
        (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string)?.Replace('/', '\\');

    private static IEnumerable<string> SteamLibraries()
    {
        if (SteamPath is not { } steam)
            yield break;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield return steam;
            yield break;
        }
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
        {
            var path = m.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path))
                yield return path;
        }
    }

    internal static IEnumerable<ExtraApp> Steam()
    {
        foreach (var library in SteamLibraries())
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps))
                continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text = File.ReadAllText(manifest);
                string? Field(string name) => Regex.Match(text, $"\"{name}\"\\s+\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : null;
                if (Field("appid") is not { } appId || Field("name") is not { Length: > 0 } name || SteamTool().IsMatch(name))
                    continue;
                var folder = Field("installdir") is { } dir ? Path.Combine(apps, "common", dir) : null;
                if (folder is null || !Directory.Exists(folder))
                    continue; // descargando o desinstalado a medias
                var icon = MainExecutable(folder) ?? Path.Combine(SteamPath!, "steam.exe");
                yield return new ExtraApp($"steam://rungameid/{appId}", name, icon, $"steam://rungameid/{appId}", null);
            }
        }
    }

    // ── Epic ────────────────────────────────────────────────────────────────

    private static string EpicManifests => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    internal static IEnumerable<ExtraApp> Epic()
    {
        if (!Directory.Exists(EpicManifests))
            yield break;
        foreach (var file in Directory.EnumerateFiles(EpicManifests, "*.item"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
                continue;
            // Solo juegos (no complementos ni el motor de Unreal).
            if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
                && !categories.EnumerateArray().Any(c => c.GetString() == "games"))
                continue;
            if (Text("DisplayName") is not { Length: > 0 } name || Text("InstallLocation") is not { } location
                || Text("CatalogNamespace") is not { } space || Text("CatalogItemId") is not { } item || Text("AppName") is not { } appName)
                continue;
            var exe = Text("LaunchExecutable") is { Length: > 0 } launch ? Path.Combine(location, launch) : null;
            string uri = $"com.epicgames.launcher://apps/{space}%3A{item}%3A{appName}?action=launch&silent=true";
            yield return new ExtraApp(uri, name, exe is not null && File.Exists(exe) ? exe : MainExecutable(location) ?? location, uri, null);
        }
    }

    // ── GOG ─────────────────────────────────────────────────────────────────

    internal static IEnumerable<ExtraApp> Gog()
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games is null)
            yield break;
        foreach (var id in games.GetSubKeyNames())
        {
            using var game = games.OpenSubKey(id);
            if (game?.GetValue("gameName") is not string { Length: > 0 } name || game.GetValue("exe") is not string exe || !File.Exists(exe))
                continue;
            yield return new ExtraApp(exe, name, exe, exe, game.GetValue("path") as string ?? Path.GetDirectoryName(exe));
        }
    }

    // ── Accesos directos del escritorio ─────────────────────────────────────

    /// <summary>
    /// Accesos directos del escritorio a un programa (.exe) o a un juego (steam://, Epic…): muchos
    /// instaladores solo dejan ahí el suyo. Los de documentos, carpetas y páginas web no.
    /// </summary>
    internal static IEnumerable<ExtraApp> DesktopShortcuts()
    {
        var desktops = new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }
            .Select(Environment.GetFolderPath).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var desktop in desktops)
        {
            foreach (var link in Directory.EnumerateFiles(desktop, "*.lnk"))
                if (ShortcutTarget(link) is { } target && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(target) && !IsUninstaller(target))
                    yield return new ExtraApp(link, ShortcutName(link), link, link, null);
            foreach (var url in Directory.EnumerateFiles(desktop, "*.url"))
                if (UrlOf(url) is { } address && !address.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    yield return new ExtraApp(url, ShortcutName(url), url, url, null);
        }
    }

    private static string? UrlOf(string file) =>
        File.ReadLines(file).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();

    /// <summary>Destino de un .lnk (null si no se puede leer).</summary>
    private static string? ShortcutTarget(string link)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
                return null;
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                return (string)shell.CreateShortcut(link).TargetPath;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return null;
        }
    }

    // ── Ayudantes ───────────────────────────────────────────────────────────

    [GeneratedRegex(@"unins|uninstall|setup|install|crash|report|redist|vcredist|dxsetup|helper|updater|launcherpatcher|easyanticheat|be_service", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheGame();

    private static bool IsUninstaller(string exe) => NotTheGame().IsMatch(Path.GetFileNameWithoutExtension(exe));

    /// <summary>El ejecutable principal de un juego: el más grande de su carpeta (o de una subcarpeta), sin instaladores.</summary>
    internal static string? MainExecutable(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        IEnumerable<FileInfo> Exes(SearchOption depth) => new DirectoryInfo(folder)
            .EnumerateFiles("*.exe", new EnumerationOptions { RecurseSubdirectories = depth == SearchOption.AllDirectories, MaxRecursionDepth = 2, IgnoreInaccessible = true })
            .Where(f => !IsUninstaller(f.FullName));
        return (Exes(SearchOption.TopDirectoryOnly).MaxBy(f => f.Length) ?? Exes(SearchOption.AllDirectories).MaxBy(f => f.Length))?.FullName;
    }
}
