using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Updates;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services.Modules;

/// <summary>
/// Un módulo opcional: una parte de Strata UI que no viene en el instalador y se instala desde Hub ›
/// Módulos opcionales. Su DLL se adjunta a la publicación de GitHub de esta misma versión.
/// </summary>
public abstract class OptionalModule
{
    protected OptionalModule(string id, string fileName, SymbolRegular icon)
    {
        Id = id;
        FileName = fileName;
        Icon = icon;
    }

    public string Id { get; }
    public string FileName { get; }
    public SymbolRegular Icon { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }

    public bool IsDownloading { get; protected set; }
    /// <summary>Avance de la descarga, de 0 a 1.</summary>
    public double Progress { get; protected set; }
    public string? ErrorText { get; protected set; }

    public abstract bool IsInstalled { get; }
    /// <summary>Se puede quitar desde el Hub (el que está junto a StrataUI.exe, en desarrollo, no).</summary>
    public abstract bool CanRemove { get; }
    /// <summary>Quitarlo surte efecto al reiniciar Strata UI (ya está cargado).</summary>
    public virtual bool RemovePending => false;

    /// <summary>Cambió el estado: se instaló, se quitó, avanzó o falló la descarga.</summary>
    public event Action? Changed;

    protected void RaiseChanged()
    {
        Changed?.Invoke();
        OptionalModules.RaiseChanged(this);
    }

    public abstract Task InstallAsync();
    public abstract void Remove();
    public virtual void StartupMaintenance() { }

    protected static string? AppVersion =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>El archivo es de esta misma compilación de Strata UI (la versión completa incluye el commit).</summary>
    protected static bool Matches(string path) =>
        File.Exists(path) && FileVersionInfo.GetVersionInfo(path).ProductVersion is { Length: > 0 } version
        && version == AppVersion;

    /// <summary>Dirección de descarga de un archivo de la publicación de esta versión, o null si no está.</summary>
    public static async Task<string?> FindAssetAsync(string fileName)
    {
        using var response = await UpdateService.Client.GetAsync(
            $"https://api.github.com/repos/{UpdateService.Repository}/releases/tags/v{App.Version}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (string.Equals(asset.GetProperty("name").GetString(), fileName, StringComparison.OrdinalIgnoreCase))
                return asset.GetProperty("browser_download_url").GetString();
        return null;
    }

    /// <summary>Descarga <paramref name="url"/> a <paramref name="target"/> (pasando por un .part), con avance.</summary>
    public static async Task DownloadAsync(string url, string target, Action<double>? progress = null)
    {
        string part = target + ".part";
        try
        {
            using var response = await UpdateService.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(part))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                        progress?.Invoke((double)read / total);
                }
            }
            File.Move(part, target, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(part);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// Un módulo que corre dentro de Strata UI: se descarga a %LOCALAPPDATA%\StrataUI\&lt;carpeta&gt; y se
/// carga al usarse. Tiene que ser de la misma compilación que la app (usa sus tipos internos). Los
/// procesos elevados nunca lo cargan: esa carpeta la puede cambiar cualquier programa del usuario.
/// Junto a StrataUI.exe (desarrollo) también vale.
/// </summary>
public abstract class DownloadedModule<T> : OptionalModule where T : class
{
    private readonly string _folderName;
    private T? _instance;
    private bool _loadFailed;

    protected DownloadedModule(string id, string fileName, string folderName, SymbolRegular icon) : base(id, fileName, icon) =>
        _folderName = folderName;

    public string Folder => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, _folderName);
    private string Downloaded => Path.Combine(Folder, FileName);
    private string Bundled => Path.Combine(AppContext.BaseDirectory, FileName);
    // Se quitó con el módulo ya cargado (Windows no deja borrar el archivo): se borra al próximo inicio.
    private string RemoveMarker => Path.Combine(Folder, "remove");

    public override bool IsInstalled => FindFile() is { } path && !(path == Downloaded && File.Exists(RemoveMarker));
    public override bool CanRemove => IsInstalled && FindFile() == Downloaded;
    public override bool RemovePending => File.Exists(RemoveMarker) && _instance != null;

    private string? FindFile() => Matches(Bundled) ? Bundled : Matches(Downloaded) ? Downloaded : null;

    /// <summary>Lo que exporta el módulo (lo carga la primera vez), o null si no está o no se pudo cargar.</summary>
    public T? Load()
    {
        if (!IsInstalled)
            return null;
        if (_instance != null || _loadFailed)
            return _instance;
        try
        {
            var assembly = Assembly.LoadFrom(FindFile()!);
            var type = assembly.GetExportedTypes().First(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract);
            _instance = (T)Activator.CreateInstance(type)!;
            Log.Info($"Módulo {Id} cargado desde {assembly.Location}");
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            ErrorText = Loc.F("No se pudo cargar el módulo {0}.", Title);
            Log.Error($"No se pudo cargar el módulo {Id}", ex);
        }
        return _instance;
    }

    /// <summary>Al iniciar: borra el que se quitó y, si hay uno de otra versión (se actualizó Strata UI), baja el de esta.</summary>
    public override void StartupMaintenance()
    {
        try
        {
            if (File.Exists(RemoveMarker))
            {
                File.Delete(Downloaded);
                File.Delete(RemoveMarker);
                Log.Info($"Módulo {Id} quitado");
            }
            else if (File.Exists(Downloaded) && FindFile() is null)
            {
                Log.Info($"El módulo {Id} es de otra versión: se descarga el de esta");
                _ = InstallAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"No se pudo revisar el módulo {Id}", ex);
        }
    }

    /// <summary>Descarga el módulo de la publicación vX.Y.Z de GitHub y lo deja listo para cargar.</summary>
    public override async Task InstallAsync()
    {
        if (IsDownloading)
            return;
        ErrorText = null;
        // Se había quitado en esta sesión y sigue en su lugar: basta con olvidar que se quitó.
        if (File.Exists(RemoveMarker) && Matches(Downloaded))
        {
            File.Delete(RemoveMarker);
            RaiseChanged();
            return;
        }
        IsDownloading = true;
        Progress = 0;
        RaiseChanged();
        string fresh = Downloaded + ".new";
        try
        {
            string? url = await FindAssetAsync(FileName);
            if (url is null)
            {
                ErrorText = Loc.F("La versión {0} todavía no tiene el módulo publicado.", App.Version);
                return;
            }
            Directory.CreateDirectory(Folder);
            await DownloadAsync(url, fresh, p =>
            {
                Progress = p;
                RaiseChanged();
            });
            if (!Matches(fresh))
            {
                Log.Info($"Módulo {Id} descartado: versión {FileVersionInfo.GetVersionInfo(fresh).ProductVersion}, se esperaba {AppVersion}");
                File.Delete(fresh);
                ErrorText = Loc.T("El módulo descargado no es de esta versión de Strata UI.");
                return;
            }
            File.Move(fresh, Downloaded, overwrite: true);
            File.Delete(RemoveMarker);
            Log.Info($"Módulo {Id} instalado en {Downloaded}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                      or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            Log.Error($"No se pudo descargar el módulo {Id}", ex);
            ErrorText = Loc.F("No se pudo descargar el módulo {0}.", Title);
        }
        finally
        {
            IsDownloading = false;
            RaiseChanged();
        }
    }

    /// <summary>Quita el módulo descargado (si ya está cargado, al próximo inicio).</summary>
    public override void Remove()
    {
        if (!CanRemove)
            return;
        try
        {
            if (_instance is null)
                File.Delete(Downloaded);
            else
                File.WriteAllText(RemoveMarker, "");
            Log.Info($"Módulo {Id} quitado desde el Hub");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"No se pudo quitar el módulo {Id}", ex);
        }
        RaiseChanged();
    }
}

/// <summary>Los módulos opcionales, en el orden de Hub › Módulos opcionales.</summary>
public static class OptionalModules
{
    public static readonly OptimizationModule Optimization = new();
    public static readonly FilesModule Files = new();
    public static readonly SensorsModule Sensors = new();

    public static IReadOnlyList<OptionalModule> All { get; } = [Optimization, Files, Sensors];

    /// <summary>Cambió el estado de alguno.</summary>
    public static event Action<OptionalModule>? Changed;

    internal static void RaiseChanged(OptionalModule module) => Changed?.Invoke(module);

    public static void StartupMaintenance()
    {
        foreach (var module in All)
            module.StartupMaintenance();
    }
}

/// <summary>
/// Optimización (StrataUI.Optimization.dll): las páginas de ajustes de Windows del Hub. El motor
/// (Tweaks/, limpieza, NVIDIA) sigue en la app: lo usan otras partes y los procesos elevados.
/// </summary>
public sealed class OptimizationModule() : DownloadedModule<Hub.IHubModule>("Optimization", "StrataUI.Optimization.dll", "Optimization", SymbolRegular.Flash24)
{
    public override string Title => Loc.T("Optimización");
    public override string Description => Loc.T("Rendimiento, privacidad, IA, limpieza, apps preinstaladas y más ajustes de Windows.");
}

/// <summary>
/// Archivos (StrataUI.Files.dll): el explorador de archivos y el visor de fotos, video y música. Sin
/// él, las carpetas se abren en el Explorador de Windows; el escritorio de Strata UI sigue igual.
/// </summary>
public sealed class FilesModule() : DownloadedModule<IFilesModule>("Files", "StrataUI.Files.dll", "Files", SymbolRegular.Folder24)
{
    public override string Title => Loc.T("Archivos");
    public override string Description => Loc.T("Explorador de archivos con pestañas, vista doble, carátulas y visor de fotos, video y música. Reemplaza al Explorador dentro de Strata UI.");

    /// <summary>Win+E y las carpetas de Inicio, Win+X y el escritorio abren Archivos (instalado y elegido en el Hub).</summary>
    public bool ReplacesExplorer => Settings.Current.FilesReplaceExplorer && IsInstalled;
}

/// <summary>Lo que exporta el módulo Archivos para el resto de Strata UI.</summary>
public interface IFilesModule
{
    /// <summary>Una ventana de Archivos nueva en esa carpeta (o en Este equipo), sin mostrar.</summary>
    System.Windows.Window CreateWindow(string? path);

    /// <summary>El visor con esas fotos, videos o canciones, abierto en la de <paramref name="index"/>.</summary>
    void OpenMedia(IReadOnlyList<string> items, int index);
}
