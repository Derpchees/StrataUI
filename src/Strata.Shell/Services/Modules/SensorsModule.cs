using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services
{
    /// <summary>Lo que exporta el módulo Lecturas avanzadas (StrataUI.Sensors.dll) al proceso elevado.</summary>
    internal interface ISensorReaders
    {
        ITemperatureReader OpenTemperatures();
        IFrameReader CreateFrameCounter();
    }

    /// <summary>Temperaturas de CPU y GPU (LibreHardwareMonitor).</summary>
    internal interface ITemperatureReader : IDisposable
    {
        /// <summary>PawnIO está instalado: sin él no hay temperatura de CPU.</summary>
        bool PawnIo { get; }
        (double? Cpu, double? Gpu) Read();
    }

    /// <summary>Cuadros por segundo de cada proceso (ETW).</summary>
    internal interface IFrameReader : IDisposable
    {
        void Start();
        Dictionary<int, Sensors.FrameRate> TakeCounts();
        /// <summary>Procesos que mandaron marcas de presentación fuera de banda (DLSS FG) desde la lectura anterior.</summary>
        HashSet<int> OutOfBand { get; }
        bool Debug { get; }
    }
}

namespace Strata.Shell.Services.Modules
{
    /// <summary>
    /// Lecturas avanzadas (StrataUI.Sensors.zip): temperaturas de CPU y GPU y contador de FPS. Trae
    /// LibreHardwareMonitor y TraceEvent (varias DLL, también nativas): se publica comprimido y se
    /// descomprime en %LOCALAPPDATA%\StrataUI\Sensors. Lo usa solo el proceso elevado de las lecturas
    /// (<see cref="SensorsTask"/>), y nunca desde esa carpeta (la puede cambiar cualquier programa del
    /// usuario): al crear la tarea con UAC se copia a la carpeta protegida y se carga de ahí.
    /// </summary>
    public sealed class SensorsModule() : OptionalModule("Sensors", "StrataUI.Sensors.zip", SymbolRegular.Temperature24)
    {
        public const string DllName = "StrataUI.Sensors.dll";

        public override string Title => Loc.T("Lecturas avanzadas");
        public override string Description => Loc.T("Temperatura de CPU y GPU y contador de FPS para los medidores de la barra y los widgets. Al activarlas, Windows pide permiso de administrador una vez.");

        private static string UserFolder => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "Sensors");
        private static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "Sensors");

        /// <summary>Donde lo carga el proceso elevado: dentro de la carpeta protegida de las lecturas.</summary>
        public static string ProtectedFolder => Path.Combine(SensorsTask.CopyFolder, "Sensors");

        /// <summary>La carpeta del módulo de esta versión (junto a StrataUI.exe en desarrollo, o la descargada), o null.</summary>
        public string? SourceFolder =>
            Matches(Path.Combine(BundledFolder, DllName)) ? BundledFolder
            : Matches(Path.Combine(UserFolder, DllName)) ? UserFolder
            : null;

        public override bool IsInstalled => SourceFolder != null;
        public override bool CanRemove => SourceFolder == UserFolder;

        public override void StartupMaintenance()
        {
            // Quedó el de otra versión (se actualizó Strata UI): se baja el de esta.
            if (Directory.Exists(UserFolder) && SourceFolder is null)
            {
                Log.Info("El módulo Sensors es de otra versión: se descarga el de esta");
                _ = InstallAsync();
            }
        }

        public override async Task InstallAsync()
        {
            if (IsDownloading)
                return;
            ErrorText = null;
            IsDownloading = true;
            Progress = 0;
            RaiseChanged();
            string zip = Path.Combine(Path.GetTempPath(), $"strataui-sensors-{Guid.NewGuid():N}.zip");
            string fresh = UserFolder + ".new";
            try
            {
                string? url = await FindAssetAsync(FileName);
                if (url is null)
                {
                    ErrorText = Loc.F("La versión {0} todavía no tiene el módulo publicado.", App.Version);
                    return;
                }
                await DownloadAsync(url, zip, p =>
                {
                    Progress = p;
                    RaiseChanged();
                });
                if (Directory.Exists(fresh))
                    Directory.Delete(fresh, recursive: true);
                ZipFile.ExtractToDirectory(zip, fresh);
                if (!Matches(Path.Combine(fresh, DllName)))
                {
                    Log.Info($"Módulo Sensors descartado: versión {FileVersionInfo.GetVersionInfo(Path.Combine(fresh, DllName)).ProductVersion}, se esperaba {AppVersion}");
                    Directory.Delete(fresh, recursive: true);
                    ErrorText = Loc.T("El módulo descargado no es de esta versión de Strata UI.");
                    return;
                }
                if (Directory.Exists(UserFolder))
                    Directory.Delete(UserFolder, recursive: true);
                Directory.Move(fresh, UserFolder);
                Log.Info($"Módulo Sensors instalado en {UserFolder}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or JsonException or KeyNotFoundException
                                          or InvalidDataException)
            {
                Log.Error("No se pudo descargar el módulo Sensors", ex);
                ErrorText = Loc.F("No se pudo descargar el módulo {0}.", Title);
            }
            finally
            {
                try
                {
                    File.Delete(zip);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
                IsDownloading = false;
                RaiseChanged();
            }
        }

        /// <summary>
        /// Quita la copia descargada. La protegida se va la próxima vez que se renueve la tarea (al
        /// volver a iniciar las lecturas, con UAC); mientras tanto Strata UI no muestra temperaturas ni FPS.
        /// </summary>
        public override void Remove()
        {
            if (!CanRemove)
                return;
            try
            {
                Sensors.Stop();
                Directory.Delete(UserFolder, recursive: true);
                Log.Info("Módulo Sensors quitado desde el Hub");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("No se pudo quitar el módulo Sensors", ex);
            }
            RaiseChanged();
        }

        /// <summary>
        /// En el proceso elevado: los lectores del módulo de la carpeta protegida (si está y es de esta
        /// versión), o null. Sus DLL (LibreHardwareMonitor, TraceEvent) se resuelven en esa misma carpeta.
        /// </summary>
        internal static ISensorReaders? LoadProtected()
        {
            string path = Path.Combine(ProtectedFolder, DllName);
            if (!Matches(path))
            {
                Log.Info("Lecturas avanzadas: el módulo no está en la carpeta protegida; sin temperaturas ni FPS");
                return null;
            }
            try
            {
                var assembly = Assembly.LoadFrom(path);
                var type = assembly.GetExportedTypes().First(t => typeof(ISensorReaders).IsAssignableFrom(t) && !t.IsAbstract);
                return (ISensorReaders)Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                Log.Error("Lecturas avanzadas: no se pudo cargar el módulo", ex);
                return null;
            }
        }
    }
}
