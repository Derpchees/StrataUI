using System.Diagnostics;
using System.IO;

namespace Strata.Shell.Services;

/// <summary>Lo que exporta el módulo Lecturas avanzadas al proceso elevado (<see cref="ISensorReaders"/>).</summary>
public sealed class SensorReaders : ISensorReaders
{
    ITemperatureReader ISensorReaders.OpenTemperatures() => new Temperatures();

    IFrameReader ISensorReaders.CreateFrameCounter() => new FrameCounter();
}

/// <summary>
/// Temperaturas con LibreHardwareMonitor. CPU: la que da la placa base (la de la BIOS y las apps
/// del fabricante, más estable); si la placa no la informa, la del procesador (paquete, Tctl o el
/// núcleo más caliente). GPU: la del núcleo.
/// </summary>
internal sealed class Temperatures : ITemperatureReader
{
    private readonly LibreHardwareMonitor.Hardware.Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };

    public bool PawnIo { get; }

    public Temperatures()
    {
        try
        {
            PawnIo = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            _computer.Open();
        }
        catch (Exception ex)
        {
            // Sin sensores: se mandan vacíos.
            Log.Error("Lecturas avanzadas: no se pudieron abrir los sensores", ex);
        }
    }

    public (double? Cpu, double? Gpu) Read()
    {
        double? cpu = null, gpu = null, board = null;
        bool integrated = false;
        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                // Intel integrada: no informa temperatura propia; está en el mismo chip que la CPU.
                if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuIntel)
                    integrated = true;
                if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Motherboard)
                {
                    // El chip de la placa (Super I/O) va como subcomponente; su sensor se llama "CPU".
                    // Aparte: si la placa falla, la CPU y la GPU se siguen leyendo.
                    try
                    {
                        if (!Update(hardware))
                            continue;
                        foreach (var chip in hardware.SubHardware)
                        {
                            if (!Update(chip))
                                continue;
                            board ??= chip.Sensors.FirstOrDefault(s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature
                                                                      && s.Name.Equals("CPU", StringComparison.OrdinalIgnoreCase)
                                                                      && s.Value is > 0 and < 150)?.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportOnce("placa", ex);
                    }
                    continue;
                }
                if (!Update(hardware))
                    continue;
                var temps = hardware.Sensors
                    .Where(s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature && s.Value is > 0)
                    .ToList();
                if (temps.Count == 0)
                    continue;
                if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                {
                    var package = temps.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                                            || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));
                    cpu = package?.Value ?? temps.Max(s => s.Value);
                }
                else if (hardware.HardwareType is LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia
                         or LibreHardwareMonitor.Hardware.HardwareType.GpuAmd
                         or LibreHardwareMonitor.Hardware.HardwareType.GpuIntel)
                {
                    var core = temps.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) ?? temps[0];
                    gpu = Math.Max(gpu ?? 0, core.Value ?? 0);
                }
            }
        }
        catch (Exception ex)
        {
            ReportOnce("temperaturas", ex);
        }
        if (gpu is null && integrated)
            gpu = cpu;
        return (board ?? cpu, gpu);
    }

    // Piezas lentas: en algunas laptops el controlador de la placa (o del equipo) tarda segundos en
    // responder y, mientras, frena a todo el sistema (menús que tardaban 8 s en aparecer). Después
    // de dos lecturas lentas, esa pieza deja de leerse en esta sesión.
    private const int SlowMs = 1500;
    private readonly Dictionary<string, int> _slowReads = new();
    private readonly HashSet<string> _skipped = new();
    private readonly List<string> _skippedNames = new();

    public IReadOnlyCollection<string> Skipped { get { lock (_skippedNames) return _skippedNames.ToList(); } }

    /// <summary>Lee una pieza si no quedó descartada por lenta; false si se la salta.</summary>
    private bool Update(LibreHardwareMonitor.Hardware.IHardware hardware)
    {
        string id = hardware.Identifier.ToString();
        if (_skipped.Contains(id))
            return false;
        var watch = Stopwatch.StartNew();
        hardware.Update();
        if (watch.ElapsedMilliseconds > SlowMs)
        {
            int slow = _slowReads[id] = _slowReads.GetValueOrDefault(id) + 1;
            if (slow >= 2 && _skipped.Add(id))
            {
                lock (_skippedNames)
                    _skippedNames.Add(hardware.Name);
                Log.Info($"Lecturas avanzadas: {hardware.Name} tarda {watch.ElapsedMilliseconds} ms en leerse; se deja de leer");
            }
        }
        return true;
    }

    private readonly HashSet<string> _reported = new();

    /// <summary>Registra el primer error de cada parte (se lee cada segundo: no llenar el registro).</summary>
    private void ReportOnce(string part, Exception ex)
    {
        if (_reported.Add(part))
            Log.Error($"Lecturas avanzadas: falló la lectura ({part})", ex);
    }

    public void Dispose()
    {
        try
        {
            _computer.Close();
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>
/// Cuenta los cuadros de cada proceso con ETW, como PresentMon: presentaciones de DXGI y Direct3D 9
/// (las que hace la app o, con DLSS/FSR FG, también las que inserta la generación de cuadros) y
/// del núcleo gráfico (OpenGL, Vulkan y los cuadros que inserta el controlador: Smooth Motion,
/// AFMF). Los cuadros reales se toman de las marcas de Reflex (PCLStats, que llevan todos los
/// juegos con DLSS FG): una por cuadro que simula el juego.
/// <para>Los FPS salen de la marca de tiempo de cada cuadro (el promedio del último segundo de
/// cuadros), no de cuántos eventos llegaron desde la lectura anterior: ETW los entrega en tandas
/// (más o menos cada segundo) y contar por llegada daba, en un juego quieto a 112 FPS, 46 y 179
/// alternados.</para>
/// </summary>
internal sealed class FrameCounter : IFrameReader
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private static readonly Guid PclStats = new("0D216F06-82A6-4D49-BC4F-8F38AE56EFAB");
    private const int DxgiPresentStart = 42, D3D9PresentStart = 1;
    // Present_Info (copia), Flip_Info (pantalla completa) y PresentHistoryDetailed_Start (ventana
    // con composición): cada presentación pasa por al menos uno; se toma el mayor, no la suma.
    private static readonly int[] KernelPresents = [184, 168, 215];
    private const uint PclSimulationStart = 0;
    /// <summary>OUT_OF_BAND_PRESENT_START: la presentación de un cuadro que inserta DLSS Frame Generation.</summary>
    private const uint PclOutOfBandPresent = 11;

    /// <summary>Procesos que mandaron marcas de presentación fuera de banda desde la lectura anterior.</summary>
    public HashSet<int> OutOfBand { get; private set; } = new();
    private HashSet<int> _outOfBand = new();

    private Microsoft.Diagnostics.Tracing.Session.TraceEventSession? _session;
    private readonly object _lock = new();
    private readonly Dictionary<int, Sensors.FrameTrack> _api = new(), _real = new();
    private readonly Dictionary<(int Pid, int Id), Sensors.FrameTrack> _kernel = new();
    /// <summary>El evento más nuevo que llegó (de cualquier proceso): el "ahora" del reloj de los cuadros.</summary>
    private double _latest;

    /// <summary>Diagnóstico: con el archivo %LOCALAPPDATA%\StrataUI\fps-debug se registran los eventos que llegan.</summary>
    public bool Debug => _debug;

    private readonly bool _debug = File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "fps-debug"));
    private Dictionary<string, int> _histogram = new();
    private int _seconds;

    public void Start()
    {
        try
        {
            const string name = "StrataUI-FPS";
            // Una sesión con el mismo nombre que quedó abierta (Strata UI murió) se reemplaza.
            _session = new Microsoft.Diagnostics.Tracing.Session.TraceEventSession(name) { StopOnDispose = true };
            // Solo los eventos que se cuentan: el núcleo gráfico manda miles por segundo de otros.
            static Microsoft.Diagnostics.Tracing.Session.TraceEventProviderOptions Only(params int[] ids) =>
                new() { EventIDsToEnable = ids };
            _session.EnableProvider(Dxgi, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational, ulong.MaxValue, Only(DxgiPresentStart));
            _session.EnableProvider(D3D9, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational, ulong.MaxValue, Only(D3D9PresentStart));
            _session.EnableProvider(DxgKrnl, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational, 0x8000000 | 0x1, Only(KernelPresents));
            _session.EnableProvider(PclStats, Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);
            _session.Source.AllEvents += OnEvent;
            new Thread(() =>
            {
                try
                {
                    _session.Source.Process();
                }
                catch (Exception)
                {
                }
            }) { IsBackground = true }.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Contador de FPS: no se pudo abrir la sesión de ETW", ex);
            _session = null; // sin FPS
        }
    }

    private static void Add<TKey>(Dictionary<TKey, Sensors.FrameTrack> tracks, TKey key, double time) where TKey : notnull
    {
        if (!tracks.TryGetValue(key, out var track))
            tracks[key] = track = new Sensors.FrameTrack();
        track.Add(time);
    }

    private void OnEvent(Microsoft.Diagnostics.Tracing.TraceEvent e)
    {
        int id = (int)e.ID, pid = e.ProcessID;
        double time = e.TimeStampRelativeMSec;
        if (_debug)
        {
            string provider = e.ProviderGuid == Dxgi ? "DXGI" : e.ProviderGuid == D3D9 ? "D3D9"
                : e.ProviderGuid == DxgKrnl ? "DxgKrnl" : e.ProviderGuid == PclStats ? "PCL" : e.ProviderGuid.ToString();
            string key = provider == "PCL" ? $"{pid} PCL {e.EventName} len={e.EventDataLength} m={Marker(e)}" : $"{pid} {provider} {id}";
            lock (_lock)
                _histogram[key] = _histogram.GetValueOrDefault(key) + 1;
        }
        if (e.ProviderGuid == PclStats)
        {
            // PCLStatsEvent: Marker (UINT32) y FrameID (UINT64).
            uint? marker = e.EventDataLength >= 12 ? Marker(e) : null;
            if (marker == PclSimulationStart)
                lock (_lock)
                {
                    Add(_real, pid, time);
                    _latest = Math.Max(_latest, time);
                }
            else if (marker == PclOutOfBandPresent)
                lock (_lock)
                    _outOfBand.Add(pid);
            return;
        }
        if ((e.ProviderGuid == Dxgi && id == DxgiPresentStart) || (e.ProviderGuid == D3D9 && id == D3D9PresentStart))
        {
            lock (_lock)
            {
                Add(_api, pid, time);
                _latest = Math.Max(_latest, time);
            }
        }
        else if (e.ProviderGuid == DxgKrnl && Array.IndexOf(KernelPresents, id) >= 0)
        {
            lock (_lock)
            {
                Add(_kernel, (pid, id), time);
                _latest = Math.Max(_latest, time);
            }
        }
    }

    private static uint? Marker(Microsoft.Diagnostics.Tracing.TraceEvent e)
    {
        if (e.EventDataLength < 4)
            return null;
        byte[] data = e.EventData();
        return BitConverter.ToUInt32(data, 0);
    }

    /// <summary>
    /// Cuadros por segundo de cada proceso (el último segundo de cuadros). En pantalla: el mayor
    /// entre lo que presenta la app y lo que llega al núcleo gráfico. Reales: las marcas de Reflex
    /// si las hay; si no, lo que presenta la app (el controlador puede insertar más); si tampoco,
    /// lo del núcleo.
    /// </summary>
    public Dictionary<int, Sensors.FrameRate> TakeCounts()
    {
        var api = new Dictionary<int, int>();
        var real = new Dictionary<int, int>();
        var kernel = new Dictionary<(int Pid, int Id), int>();
        Dictionary<string, int>? histogram = null;
        lock (_lock)
        {
            Rates(_api, api);
            Rates(_real, real);
            Rates(_kernel, kernel);
            (OutOfBand, _outOfBand) = (_outOfBand, new());
            if (_debug && ++_seconds % 5 == 0)
                (histogram, _histogram) = (_histogram, new());
        }
        if (histogram != null)
            Log.Info($"FPS (5 s, sesión {(_session is null ? "cerrada" : "abierta")}, {histogram.Count} claves): " +string.Join(" | ", histogram.OrderByDescending(h => h.Value).Take(25).Select(h => $"{h.Key}={h.Value}")));

        var shownByKernel = kernel.GroupBy(k => k.Key.Pid).ToDictionary(g => g.Key, g => g.Max(k => k.Value));
        var result = new Dictionary<int, Sensors.FrameRate>();
        foreach (int pid in api.Keys.Concat(shownByKernel.Keys).Distinct())
        {
            int app = api.GetValueOrDefault(pid), byKernel = shownByKernel.GetValueOrDefault(pid);
            int shown = Math.Max(app, byKernel);
            int frames = real.GetValueOrDefault(pid) is > 0 and var marks ? Math.Min(marks, shown)
                : app > 0 ? app : byKernel;
            if (shown > 0)
                result[pid] = new Sensors.FrameRate(shown, frames);
        }
        return result;
    }

    /// <summary>FPS de cada pista; las que se quedaron sin cuadros se descartan.</summary>
    private void Rates<TKey>(Dictionary<TKey, Sensors.FrameTrack> tracks, Dictionary<TKey, int> into) where TKey : notnull
    {
        foreach (var (key, track) in tracks.ToList())
        {
            int rate = track.Rate(_latest);
            if (rate > 0)
                into[key] = rate;
            else
                tracks.Remove(key);
        }
    }

    public void Dispose() => _session?.Dispose();
}
