using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>
/// Lecturas avanzadas: temperatura de CPU y GPU y FPS. Windows solo las da con administrador, así
/// que las hace un proceso aparte (StrataUI.exe --sensors, elevado con UAC al activarlas) que manda los
/// datos por una tubería con nombre cada segundo y termina cuando Strata UI se cierra.
/// </summary>
/// <remarks>
/// <para>Temperaturas: LibreHardwareMonitor. La de la CPU necesita además el controlador firmado
/// PawnIO (pawnio.eu); sin él solo hay temperatura de GPU (NVIDIA y AMD).</para>
/// <para>FPS: eventos de presentación de DXGI, Direct3D 9 y del núcleo gráfico (ETW, lo mismo que
/// PresentMon) contados por proceso. Strata UI elige el proceso: la app que ocupa el monitor elegido.</para>
/// <para>También libera la RAM ("free") sin pedir UAC otra vez mientras está abierto.</para>
/// </remarks>
public static class Sensors
{

    private static NamedPipeServerStream? _pipe;
    private static StreamWriter? _writer;
    private static Dictionary<int, FrameRate> _presents = new();
    private static Dictionary<int, string> _kinds = new();
    private static TaskCompletionSource<bool>? _freed;

    public static bool Running { get; private set; }
    public static bool Starting { get; private set; }
    public static double? CpuTemperature { get; private set; }
    public static double? GpuTemperature { get; private set; }

    /// <summary>PawnIO no está instalado: no hay temperatura de CPU.</summary>
    public static bool MissingPawnIo { get; private set; }

    /// <summary>Cambió algo (en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>PawnIO instalado (su servicio existe): sin él no hay temperatura de CPU.</summary>
    public static bool PawnIoInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key != null;
        }
    }

    /// <summary>FPS de un proceso: en pantalla, reales y el tipo de generación de cuadros (si la hay).</summary>
    public readonly record struct Fps(int Shown, int Real, string? FrameGeneration)
    {
        /// <summary>Hay generación de cuadros: llegan a la pantalla bastantes más de los que dibuja la app.</summary>
        public bool Generated => Real > 0 && Shown >= Real * 1.4;
    }

    /// <summary>FPS del proceso (null si no presenta nada o no hay datos).</summary>
    public static Fps? FpsOf(int processId)
    {
        if (!Running || !_presents.TryGetValue(processId, out var rate) || rate.Shown <= 0)
            return null;
        // Lossless Scaling dibuja los cuadros generados en su propia ventana, encima del juego.
        if (LosslessScalingRate() is { } ls && ls.Shown >= rate.Shown * 1.4)
            return new Fps(ls.Shown, rate.Real, "Lossless Scaling");
        var fps = new Fps(rate.Shown, rate.Real, null);
        return fps.Generated && _kinds.GetValueOrDefault(processId) is { } kind ? fps with { FrameGeneration = kind + " FG" } : fps;
    }

    private static FrameRate? LosslessScalingRate()
    {
        foreach (var (pid, rate) in _presents)
        {
            if (ProcessName(pid, title: false).Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase))
                return rate;
        }
        return null;
    }

    // ── App medida ──────────────────────────────────────────────────────────

    /// <summary>
    /// Proceso cuyos FPS se muestran: la ventana activa si está en el monitor elegido; si no, la de
    /// más arriba que ocupe buena parte de ese monitor (un juego en otra pantalla sigue contando
    /// aunque se use la otra). Sin monitor elegido (automático), la ventana activa en cualquiera y,
    /// si no ocupa bastante pantalla, la del monitor principal de Strata UI. Null si no hay ninguna o las
    /// lecturas no están activas.
    /// </summary>
    public static int? TargetProcess(string? device)
    {
        if (!Running)
            return null;
        var screens = System.Windows.Forms.Screen.AllScreens;
        // Automático: la ventana activa en cualquier monitor (el juego que se está usando).
        if (device is null)
        {
            IntPtr active = GetForegroundWindow();
            foreach (var each in screens)
            {
                if (IsTarget(active, each.Bounds))
                    return ProcessOf(active);
            }
        }
        var screen = screens.FirstOrDefault(s => s.DeviceName == device)
                     ?? screens.FirstOrDefault(s => s.DeviceName == App.MainScreenDevice(screens.Select(sc => (sc.DeviceName, sc.Primary))))
                     ?? System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null)
            return null;
        var bounds = screen.Bounds;
        IntPtr foreground = GetForegroundWindow();
        if (IsTarget(foreground, bounds))
            return ProcessOf(foreground);
        int? found = null;
        EnumWindows((hwnd, _) =>
        {
            if (!IsTarget(hwnd, bounds))
                return true;
            found = ProcessOf(hwnd);
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static readonly Dictionary<(int, bool), string> Names = new();

    /// <summary>Nombre del proceso medido: el título de su ventana (para el tooltip) o el del ejecutable.</summary>
    public static string ProcessName(int processId, bool title = true)
    {
        if (Names.TryGetValue((processId, title), out var name))
            return name;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = title && process.MainWindowTitle is { Length: > 0 } windowTitle ? windowTitle : process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            name = processId.ToString(CultureInfo.InvariantCulture);
        }
        if (Names.Count > 128)
            Names.Clear();
        return Names[(processId, title)] = name;
    }

    private static int? ProcessOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 || pid == Environment.ProcessId ? null : (int)pid;
    }

    /// <summary>Ventana visible (no minimizada ni oculta por DWM) cuyo centro está en el monitor y lo ocupa al menos un cuarto.</summary>
    private static bool IsTarget(IntPtr hwnd, System.Drawing.Rectangle monitor)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd) || !GetWindowRect(hwnd, out var r))
            return false;
        if (DwmGetWindowAttribute(hwnd, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        if (!monitor.Contains(cx, cy))
            return false;
        long area = (long)Math.Max(0, r.Right - r.Left) * Math.Max(0, r.Bottom - r.Top);
        return area * 4 >= (long)monitor.Width * monitor.Height && ProcessOf(hwnd) != null;
    }

    private const int DwmCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int capacity);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>
    /// Inicia el proceso elevado. Con la tarea de <see cref="SensorsTask"/> no pregunta; la primera
    /// vez (o tras actualizar Strata UI) pide UAC para crearla. Si la tarea no se puede usar, se lanza
    /// elevado directamente (UAC cada vez). Devuelve false si se canceló o falló.
    /// </summary>
    public static async Task<bool> StartAsync()
    {
        if (Running || Starting)
            return Running;
        Starting = true;
        Changed?.Invoke();
        try
        {
            bool ready = await Task.Run(SensorsTask.IsReady) || await SensorsTask.InstallAsync();
            if (!ready)
                return Fail("Lecturas avanzadas: no se aceptó el permiso");
            string name = SensorsTask.PipeName(SensorsTask.UserSid);
            _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var wait = _pipe.WaitForConnectionAsync();
            if (!await Task.Run(SensorsTask.Run))
                return Fail("No se pudo iniciar la tarea de lecturas avanzadas");
            if (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(20))) != wait)
                return Fail("El proceso de lecturas no se conectó");
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
            Running = true;
            Log.Info("Lecturas avanzadas iniciadas (tarea programada)");
            _ = ReadLoopAsync(_pipe);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron iniciar las lecturas avanzadas", ex);
            Close();
            return false;
        }
        finally
        {
            Starting = false;
            Changed?.Invoke();
        }
    }

    private static bool _quietTried;

    /// <summary>
    /// Inicia las lecturas solo si el permiso ya está dado (la tarea existe y está al día): nunca
    /// pregunta. Lo usan las barras con medidores al abrir Strata UI. Una vez por sesión.
    /// </summary>
    public static async void StartQuietly()
    {
        if (Running || Starting || _quietTried)
            return;
        _quietTried = true;
        if (await Task.Run(SensorsTask.IsReady))
            await StartAsync();
    }

    private static bool Fail(string message)
    {
        Log.Info(message);
        Close();
        return false;
    }

    public static void Stop() => Close();

    /// <summary>Libera la RAM con el proceso elevado (lo inicia si hace falta); false si no se pudo.</summary>
    public static async Task<bool> FreeRamAsync()
    {
        if (!Running && !await StartAsync())
            return false;
        if (_writer is null)
            return false;
        _freed = new TaskCompletionSource<bool>();
        await _writer.WriteLineAsync("free");
        return await Task.WhenAny(_freed.Task, Task.Delay(TimeSpan.FromSeconds(60))) == _freed.Task;
    }

    private static async Task ReadLoopAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            bool first = true;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (first)
                    Log.Info("Lecturas avanzadas: primera lectura recibida: " + line);
                first = false;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Apply(line));
            }
        }
        catch (IOException)
        {
        }
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(pipe, _pipe))
            {
                Log.Info("El proceso de lecturas avanzadas terminó");
                Close();
            }
        });
    }

    /// <summary>
    /// Una línea del proceso elevado: "cpu=54.0;gpu=61;pawnio=1;fps=1234:144:72,5678:60:60;fg=1234:DLSS"
    /// (fps = proceso:en pantalla:reales) o "freed".
    /// </summary>
    internal static void Apply(string line)
    {
        if (line == "freed")
        {
            _freed?.TrySetResult(true);
            return;
        }
        var presents = new Dictionary<int, FrameRate>();
        var kinds = new Dictionary<int, string>();
        foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = part[..eq], value = part[(eq + 1)..];
            switch (key)
            {
                case "cpu":
                    CpuTemperature = ParseTemperature(value);
                    break;
                case "gpu":
                    GpuTemperature = ParseTemperature(value);
                    break;
                case "pawnio":
                    MissingPawnIo = value == "0";
                    break;
                case "fps":
                    foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var bits = pair.Split(':');
                        if (bits.Length == 3 && int.TryParse(bits[0], out int pid)
                            && int.TryParse(bits[1], out int shown) && int.TryParse(bits[2], out int real))
                            presents[pid] = new FrameRate(shown, real);
                    }
                    break;
                case "fg":
                    foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var bits = pair.Split(':');
                        if (bits.Length == 2 && int.TryParse(bits[0], out int pid) && bits[1] is "DLSS" or "FSR" or "XeSS")
                            kinds[pid] = bits[1];
                    }
                    break;
            }
        }
        _presents = presents;
        _kinds = kinds;
        Changed?.Invoke();
    }

    private static double? ParseTemperature(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t is > 0 and < 150 ? t : null;

    private static void Close()
    {
        Running = false;
        _writer = null;
        try
        {
            _pipe?.Dispose();
        }
        catch (IOException)
        {
        }
        _pipe = null;
        CpuTemperature = GpuTemperature = null;
        _presents = new();
        _freed?.TrySetResult(false);
        Changed?.Invoke();
    }

    // ── Proceso elevado ─────────────────────────────────────────────────────

    /// <summary>
    /// StrataUI.exe --sensors-task (lo inicia la tarea de <see cref="SensorsTask"/>): se conecta a la
    /// tubería del usuario, comprueba que quien la abrió sea Strata UI y solo manda lecturas y atiende
    /// "free". Termina cuando ese Strata UI se cierra.
    /// </summary>
    public static int RunHelper(string[] args)
    {
        if (args.Length != 1)
            return 2;
        // Asíncrona: en una tubería síncrona Windows pone en fila las operaciones del mismo
        // identificador, y la lectura pendiente de órdenes bloqueaba para siempre el envío de lecturas.
        using var pipe = new NamedPipeClientStream(".", SensorsTask.PipeName(SensorsTask.UserSid), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(10000);
        }
        catch (TimeoutException)
        {
            return 3;
        }
        Process parent;
        try
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverId))
                return 2;
            parent = Process.GetProcessById((int)serverId);
            if (!parent.ProcessName.Equals("StrataUI", StringComparison.OrdinalIgnoreCase))
                return 2;
        }
        catch (ArgumentException)
        {
            return 2;
        }
        var writer = new StreamWriter(pipe) { AutoFlush = true };
        var writeLock = new object();
        void Send(string line)
        {
            lock (writeLock)
                writer.WriteLine(line);
        }

        var clock = Stopwatch.StartNew();
        using var frames = new FrameCounter();
        var probe = new FrameGenerationProbe();
        frames.Start();
        Log.Info($"Lecturas avanzadas: contador de FPS listo en {clock.ElapsedMilliseconds} ms");

        // Temperaturas en su propio hilo: abrir los sensores (sobre todo los de la placa) puede
        // tardar o quedarse esperando a otro programa que los use, y eso no debe frenar los FPS.
        double? cpu = null, gpu = null;
        bool pawnIo = true;
        new Thread(() =>
        {
            var opening = Stopwatch.StartNew();
            using var temperatures = new Temperatures();
            pawnIo = temperatures.PawnIo;
            Log.Info($"Lecturas avanzadas: sensores abiertos en {opening.ElapsedMilliseconds} ms");
            while (!parent.HasExited)
            {
                var reading = Stopwatch.StartNew();
                (cpu, gpu) = temperatures.Read();
                if (reading.ElapsedMilliseconds > 2000)
                    Log.Info($"Lecturas avanzadas: leer las temperaturas tardó {reading.ElapsedMilliseconds} ms");
                Thread.Sleep(1000);
            }
        }) { IsBackground = true }.Start();

        // Órdenes de Strata UI: "free" libera la RAM.
        var commands = new Thread(() =>
        {
            try
            {
                using var reader = new StreamReader(pipe);
                while (reader.ReadLine() is { } command)
                {
                    if (command == "free")
                    {
                        FreeMemory.Run();
                        Send("freed");
                    }
                }
            }
            catch (IOException)
            {
            }
        }) { IsBackground = true };
        commands.Start();

        int sent = 0;
        while (!parent.HasExited && pipe.IsConnected)
        {
            Thread.Sleep(1000);
            var counts = frames.TakeCounts();
            // El tipo de generación de cuadros solo se busca en los procesos que la usan.
            var kinds = counts.Where(c => c.Value.Shown >= c.Value.Real * 1.4)
                .Select(c => (Pid: c.Key, Kind: probe.KindOf(c.Key)))
                .Where(k => k.Kind != null);
            string line = string.Create(CultureInfo.InvariantCulture,
                $"cpu={cpu:0.0};gpu={gpu:0.0};pawnio={(pawnIo ? 1 : 0)}"
                + $";fps={string.Join(",", counts.Select(c => $"{c.Key}:{c.Value.Shown}:{c.Value.Real}"))}"
                + $";fg={string.Join(",", kinds.Select(k => $"{k.Pid}:{k.Kind}"))}");
            if (frames.Debug && ++sent % 10 == 1)
                Log.Info("Lecturas avanzadas: enviado " + line);
            try
            {
                Send(line);
            }
            catch (IOException)
            {
                break;
            }
        }
        Log.Info("Lecturas avanzadas: proceso elevado terminado");
        return 0;
    }

    /// <summary>
    /// Temperaturas con LibreHardwareMonitor. CPU: la que da la placa base (la de la BIOS y las apps
    /// del fabricante, más estable); si la placa no la informa, la del procesador (paquete, Tctl o el
    /// núcleo más caliente). GPU: la del núcleo.
    /// </summary>
    private sealed class Temperatures : IDisposable
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
                            hardware.Update();
                            foreach (var chip in hardware.SubHardware)
                            {
                                chip.Update();
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
                    hardware.Update();
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

    /// <summary>Cuadros de un proceso en el último segundo: los que llegan a la pantalla y los que dibujó la app.</summary>
    internal readonly record struct FrameRate(int Shown, int Real);

    /// <summary>
    /// Cuenta los cuadros de cada proceso con ETW, como PresentMon: presentaciones de DXGI y Direct3D 9
    /// (las que hace la app o, con DLSS/FSR FG, también las que inserta la generación de cuadros) y
    /// del núcleo gráfico (OpenGL, Vulkan y los cuadros que inserta el controlador: Smooth Motion,
    /// AFMF). Los cuadros reales se toman de las marcas de Reflex (PCLStats, que llevan todos los
    /// juegos con DLSS FG): una por cuadro que simula el juego.
    /// </summary>
    private sealed class FrameCounter : IDisposable
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

        private Microsoft.Diagnostics.Tracing.Session.TraceEventSession? _session;
        private readonly object _lock = new();
        private Dictionary<int, int> _api = new(), _real = new();
        private Dictionary<(int Pid, int Id), int> _kernel = new();

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

        private void OnEvent(Microsoft.Diagnostics.Tracing.TraceEvent e)
        {
            int id = (int)e.ID, pid = e.ProcessID;
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
                if (e.EventDataLength >= 12 && Marker(e) == PclSimulationStart)
                    lock (_lock)
                        _real[pid] = _real.GetValueOrDefault(pid) + 1;
                return;
            }
            if ((e.ProviderGuid == Dxgi && id == DxgiPresentStart) || (e.ProviderGuid == D3D9 && id == D3D9PresentStart))
            {
                lock (_lock)
                    _api[pid] = _api.GetValueOrDefault(pid) + 1;
            }
            else if (e.ProviderGuid == DxgKrnl && Array.IndexOf(KernelPresents, id) >= 0)
            {
                lock (_lock)
                    _kernel[(pid, id)] = _kernel.GetValueOrDefault((pid, id)) + 1;
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
        /// Cuadros del último segundo por proceso. En pantalla: el mayor entre lo que presenta la app y
        /// lo que llega al núcleo gráfico. Reales: las marcas de Reflex si las hay; si no, lo que
        /// presenta la app (el controlador puede insertar más); si tampoco, lo del núcleo.
        /// </summary>
        public Dictionary<int, FrameRate> TakeCounts()
        {
            Dictionary<int, int> api, real;
            Dictionary<(int Pid, int Id), int> kernel;
            Dictionary<string, int>? histogram = null;
            lock (_lock)
            {
                (api, _api) = (_api, new());
                (real, _real) = (_real, new());
                (kernel, _kernel) = (_kernel, new());
                if (_debug && ++_seconds % 5 == 0)
                    (histogram, _histogram) = (_histogram, new());
            }
            if (histogram != null)
                Log.Info($"FPS (5 s, sesión {(_session is null ? "cerrada" : "abierta")}, {histogram.Count} claves): " +string.Join(" | ", histogram.OrderByDescending(h => h.Value).Take(25).Select(h => $"{h.Key}={h.Value}")));

            var shownByKernel = kernel.GroupBy(k => k.Key.Pid).ToDictionary(g => g.Key, g => g.Max(k => k.Value));
            var result = new Dictionary<int, FrameRate>();
            foreach (int pid in api.Keys.Concat(shownByKernel.Keys).Distinct())
            {
                int app = api.GetValueOrDefault(pid), byKernel = shownByKernel.GetValueOrDefault(pid);
                int shown = Math.Max(app, byKernel);
                int frames = real.GetValueOrDefault(pid) is > 0 and var marks ? Math.Min(marks, shown)
                    : app > 0 ? app : byKernel;
                if (shown > 0)
                    result[pid] = new FrameRate(shown, frames);
            }
            return result;
        }

        public void Dispose() => _session?.Dispose();
    }

    /// <summary>
    /// Generación de cuadros que usa un proceso, por las bibliotecas que tiene cargadas (se revisa
    /// cada 30 s por proceso). Los juegos con anti-trampas pueden no dejar verlas: queda sin nombre.
    /// </summary>
    private sealed class FrameGenerationProbe
    {
        private readonly Dictionary<int, (DateTime When, string? Kind)> _cache = new();

        public string? KindOf(int pid)
        {
            if (_cache.TryGetValue(pid, out var cached) && DateTime.UtcNow - cached.When < TimeSpan.FromSeconds(30))
                return cached.Kind;
            string? kind = null;
            try
            {
                using var process = Process.GetProcessById(pid);
                foreach (ProcessModule module in process.Modules)
                {
                    string name = module.ModuleName.ToLowerInvariant();
                    string? found = name is "nvngx_dlssg.dll" or "sl.dlss_g.dll" ? "DLSS"
                        : name.Contains("frameinterpolation") || name.StartsWith("amd_fidelityfx_") || name.Contains("ffx_fsr3") ? "FSR"
                        : name.StartsWith("libxess_fg") ? "XeSS"
                        : null;
                    if (found is null)
                        continue;
                    kind = found;
                    if (kind == "DLSS")
                        break; // DLSS gana: un juego puede traer también FidelityFX para escalar
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            if (_cache.Count > 64)
                _cache.Clear();
            _cache[pid] = (DateTime.UtcNow, kind);
            return kind;
        }
    }
    /// <summary>Vacía la memoria de trabajo de todos los procesos y la lista de espera (necesita administrador).</summary>
    private static class FreeMemory
    {
        private const int SystemMemoryListInformation = 80;
        private const int MemoryEmptyWorkingSets = 2, MemoryPurgeLowPriorityStandbyList = 5, MemoryPurgeStandbyList = 4;

        public static void Run()
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            foreach (int command in new[] { MemoryEmptyWorkingSets, MemoryPurgeLowPriorityStandbyList, MemoryPurgeStandbyList })
            {
                int value = command;
                NtSetSystemInformation(SystemMemoryListInformation, ref value, sizeof(int));
            }
        }

        private static void EnablePrivilege(string name)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out var token))
                return;
            try
            {
                if (!LookupPrivilegeValue(null, name, out long luid))
                    return;
                var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public int Count;
            public long Luid;
            public int Attributes;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length,
                                                         IntPtr previous, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
}
