using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Strata.Shell.Hub;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Updates;

namespace Strata.Shell.Services;

/// <summary>
/// Módulo de optimización (StrataUI.Optimization.dll): las páginas del grupo Optimización del Hub no
/// vienen en el instalador. El Hub las descarga de la publicación de GitHub de esta misma versión a
/// %LOCALAPPDATA%\StrataUI\Optimization y las carga al abrirse. Tiene que ser de la misma compilación
/// que la app (usa sus tipos internos): se compara la versión completa, que incluye el commit.
/// Solo lo carga el Hub; los procesos elevados (--tweak, lecturas) nunca, porque esa carpeta la puede
/// cambiar cualquier programa del usuario. Junto a StrataUI.exe (desarrollo) también vale.
/// </summary>
public static class OptimizationModule
{
    public const string FileName = "StrataUI.Optimization.dll";

    public static string Folder => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "Optimization");
    private static string Downloaded => Path.Combine(Folder, FileName);
    private static string Bundled => Path.Combine(AppContext.BaseDirectory, FileName);
    // Se quitó con el módulo ya cargado (Windows no deja borrar el archivo): se borra al próximo inicio.
    private static string RemoveMarker => Path.Combine(Folder, "remove");

    private static IHubModule? _module;
    private static bool _loadFailed;

    public static bool IsDownloading { get; private set; }
    /// <summary>Avance de la descarga, de 0 a 1.</summary>
    public static double Progress { get; private set; }
    public static string? ErrorText { get; private set; }

    /// <summary>Cambió el estado: se instaló, se quitó, avanzó o falló la descarga.</summary>
    public static event Action? Changed;

    public static bool IsInstalled => FindFile() is { } path && !(path == Downloaded && File.Exists(RemoveMarker));

    /// <summary>Se puede quitar desde el Hub (el que está junto a StrataUI.exe no).</summary>
    public static bool CanRemove => IsInstalled && FindFile() == Downloaded;

    private static string? AppVersion =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>El archivo es de esta misma compilación de Strata UI.</summary>
    private static bool Matches(string path) =>
        File.Exists(path) && FileVersionInfo.GetVersionInfo(path).ProductVersion is { Length: > 0 } version
        && version == AppVersion;

    private static string? FindFile() =>
        Matches(Bundled) ? Bundled : Matches(Downloaded) ? Downloaded : null;

    /// <summary>Las páginas del módulo (lo carga la primera vez), o null si no está o no se pudo cargar.</summary>
    public static IHubModule? Load()
    {
        if (!IsInstalled)
            return null;
        if (_module != null || _loadFailed)
            return _module;
        try
        {
            var assembly = Assembly.LoadFrom(FindFile()!);
            var type = assembly.GetExportedTypes().First(t => typeof(IHubModule).IsAssignableFrom(t) && !t.IsAbstract);
            _module = (IHubModule)Activator.CreateInstance(type)!;
            Log.Info($"Módulo de optimización cargado desde {assembly.Location}");
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            ErrorText = Loc.T("No se pudo cargar el módulo de optimización.");
            Log.Error("No se pudo cargar el módulo de optimización", ex);
        }
        return _module;
    }

    /// <summary>
    /// Al iniciar: borra el que se quitó y, si hay uno de otra versión (se actualizó Strata UI), baja
    /// el de esta.
    /// </summary>
    public static void StartupMaintenance()
    {
        try
        {
            if (File.Exists(RemoveMarker))
            {
                File.Delete(Downloaded);
                File.Delete(RemoveMarker);
                Log.Info("Módulo de optimización quitado");
            }
            else if (File.Exists(Downloaded) && FindFile() is null)
            {
                Log.Info("El módulo de optimización es de otra versión: se descarga el de esta");
                _ = InstallAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudo revisar el módulo de optimización", ex);
        }
    }

    /// <summary>Descarga el módulo de la publicación vX.Y.Z de GitHub y lo deja listo para cargar.</summary>
    public static async Task InstallAsync()
    {
        if (IsDownloading)
            return;
        ErrorText = null;
        // Se había quitado en esta sesión y sigue en su lugar: basta con olvidar que se quitó.
        if (File.Exists(RemoveMarker) && Matches(Downloaded))
        {
            File.Delete(RemoveMarker);
            Changed?.Invoke();
            return;
        }
        IsDownloading = true;
        Progress = 0;
        Changed?.Invoke();
        string part = Downloaded + ".part";
        try
        {
            string? url = await FindAssetAsync();
            if (url is null)
            {
                ErrorText = Loc.F("La versión {0} todavía no tiene el módulo publicado.", App.Version);
                return;
            }
            Directory.CreateDirectory(Folder);
            using (var response = await UpdateService.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(part);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                    {
                        Progress = (double)read / total;
                        Changed?.Invoke();
                    }
                }
            }
            if (!Matches(part))
            {
                File.Delete(part);
                ErrorText = Loc.T("El módulo descargado no es de esta versión de Strata UI.");
                Log.Info($"Módulo de optimización descartado: versión {FileVersionInfo.GetVersionInfo(part).ProductVersion}, se esperaba {AppVersion}");
                return;
            }
            File.Move(part, Downloaded, overwrite: true);
            File.Delete(RemoveMarker);
            Log.Info($"Módulo de optimización instalado en {Downloaded}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                      or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            Log.Error("No se pudo descargar el módulo de optimización", ex);
            ErrorText = Loc.T("No se pudo descargar el módulo de optimización.");
            try
            {
                File.Delete(part);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
        finally
        {
            IsDownloading = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Dirección de descarga del módulo en la publicación de esta versión, o null si no está.</summary>
    private static async Task<string?> FindAssetAsync()
    {
        using var response = await UpdateService.Client.GetAsync(
            $"https://api.github.com/repos/{UpdateService.Repository}/releases/tags/v{App.Version}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (string.Equals(asset.GetProperty("name").GetString(), FileName, StringComparison.OrdinalIgnoreCase))
                return asset.GetProperty("browser_download_url").GetString();
        return null;
    }

    /// <summary>
    /// Quita el módulo descargado. Los cambios que ya se aplicaron a Windows se quedan (se deshacen
    /// en Optimización › Respaldo, con el módulo instalado).
    /// </summary>
    public static void Remove()
    {
        if (!CanRemove)
            return;
        try
        {
            if (_module is null)
                File.Delete(Downloaded);
            else
                File.WriteAllText(RemoveMarker, "");
            Log.Info("Módulo de optimización quitado desde el Hub");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudo quitar el módulo de optimización", ex);
        }
        Changed?.Invoke();
    }
}
