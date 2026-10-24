using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
    // Acciones de administrador pedidas al proceso elevado (ver Tweaks/ElevatedBroker), por número.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<int>> _runs = new();
    private static int _lastRun;

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
    /// <param name="automatic">Las inicia un medidor o "Iniciarlas al abrir Strata UI" (se detienen
    /// solas si ya nadie las usa); a mano o por una acción, siguen hasta detenerlas.</param>
    public static async Task<bool> StartAsync(bool automatic = false)
    {
        _automatic = automatic;
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

    /// <summary>Se iniciaron solas (por un medidor), no a pedido del usuario: se detienen si ya nadie las usa.</summary>
    private static bool _automatic;

    /// <summary>
    /// Inicia las lecturas solo si el permiso ya está dado (la tarea existe y está al día): nunca
    /// pregunta. Lo usan las barras y los widgets con medidores. Una vez por sesión (o hasta que
    /// <see cref="StopIfUnused"/> las detenga).
    /// </summary>
    public static async void StartQuietly()
    {
        // Sin el módulo Lecturas avanzadas no hay temperaturas ni FPS que leer.
        if (Running || Starting || _quietTried || !Modules.OptionalModules.Sensors.IsInstalled)
            return;
        _quietTried = true;
        if (await Task.Run(SensorsTask.IsReady))
            await StartAsync(automatic: true);
    }

    /// <summary>
    /// Ninguna barra ni widget muestra CPU, GPU o FPS: si las lecturas se iniciaron solas, el proceso
    /// elevado se cierra (deja de leer el hardware y los eventos de la GPU). Vuelven a iniciarse
    /// solas al agregar un medidor.
    /// </summary>
    public static void StopIfUnused(bool used)
    {
        if (used || !Running || !_automatic || !_runs.IsEmpty)
            return;
        Log.Info("Lecturas avanzadas: ningún medidor las usa; se detienen");
        Close();
        _quietTried = false;
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

    /// <summary>
    /// Ejecuta una acción de administrador con el proceso elevado, sin UAC (lo inicia si el permiso ya
    /// está dado). Null si no se pudo usar (Strata UI no está en Archivos de programa, el permiso no
    /// está dado o la acción no está en la lista): quien la pidió la lanza con UAC.
    /// </summary>
    public static Task<int?> RunElevatedAsync(string[] args) =>
        System.Windows.Application.Current is { } app
            ? app.Dispatcher.InvokeAsync(() => RunElevatedCoreAsync(args)).Task.Unwrap()
            : Task.FromResult<int?>(null);

    private static async Task<int?> RunElevatedCoreAsync(string[] args)
    {
        if (!SensorsTask.InstalledProtected || !Tweaks.ElevatedBroker.IsAllowed(args))
            return null;
        if (!Running && (!await Task.Run(SensorsTask.IsReady) || !await StartAsync()))
            return null;
        if (_writer is null)
            return null;
        int id = Interlocked.Increment(ref _lastRun);
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _runs[id] = done;
        try
        {
            await _writer.WriteLineAsync($"run {id} {Tweaks.ElevatedBroker.Encode(args)}");
            int code = await done.Task;
            return code == Tweaks.ElevatedBroker.Rejected ? null : code;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            _runs.TryRemove(id, out _);
        }
    }

    private static async Task ReadLoopAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            bool first = true;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("ran ", StringComparison.Ordinal))
                {
                    // "ran número código": terminó una acción de administrador.
                    var bits = line.Split(' ');
                    if (bits.Length == 3 && int.TryParse(bits[1], out int id) && int.TryParse(bits[2], out int code)
                        && _runs.TryGetValue(id, out var run))
                        run.TrySetResult(code);
                    continue;
                }
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
        // Se cortó el proceso elevado con acciones en curso: fallan (no se repiten con UAC).
        foreach (var run in _runs.Values)
            run.TrySetResult(1);
        Changed?.Invoke();
    }

    // ── Proceso elevado ─────────────────────────────────────────────────────

    /// <summary>
    /// StrataUI.exe --sensors-task (lo inicia la tarea de <see cref="SensorsTask"/>): se conecta a la
    /// tubería del usuario, comprueba que quien la abrió sea Strata UI, manda lecturas y atiende "free"
    /// y, si es el Strata UI de su misma carpeta protegida, "run" (acciones de administrador de la
    /// lista fija de Tweaks/ElevatedBroker). Termina cuando ese Strata UI se cierra.
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
        // Las acciones de administrador (Tweaks/ElevatedBroker) solo para el Strata UI de esta misma
        // carpeta protegida: otro programa que se llame StrataUI.exe recibe lecturas, nada más.
        bool trusted;
        try
        {
            trusted = SensorsTask.InstalledProtected
                      && string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            trusted = false;
        }
        // Se cierra poco después que Strata UI aunque un hilo esté trabado leyendo el hardware: si no,
        // el proceso quedaba vivo (y el instalador no podía reemplazar sus archivos).
        new Thread(() =>
        {
            parent.WaitForExit();
            Thread.Sleep(3000);
            Log.Info("Lecturas avanzadas: Strata UI se cerró; el proceso elevado termina");
            Process.GetCurrentProcess().Kill();
        }) { IsBackground = true }.Start();
        var writer = new StreamWriter(pipe) { AutoFlush = true };
        var writeLock = new object();
        void Send(string line)
        {
            lock (writeLock)
                writer.WriteLine(line);
        }

        // Temperaturas y FPS: del módulo Lecturas avanzadas, solo de su copia protegida. Sin él, este
        // proceso atiende igual "free" y "run" y manda las lecturas vacías.
        var readers = Modules.SensorsModule.LoadProtected();
        var clock = Stopwatch.StartNew();
        using var frames = readers?.CreateFrameCounter();
        var probe = new FrameGenerationProbe();
        frames?.Start();
        if (frames != null)
            Log.Info($"Lecturas avanzadas: contador de FPS listo en {clock.ElapsedMilliseconds} ms");

        // Temperaturas en su propio hilo: abrir los sensores (sobre todo los de la placa) puede
        // tardar o quedarse esperando a otro programa que los use, y eso no debe frenar los FPS.
        double? cpu = null, gpu = null;
        bool pawnIo = true;
        new Thread(() =>
        {
            var opening = Stopwatch.StartNew();
            if (readers is null)
                return;
            using var temperatures = readers.OpenTemperatures();
            pawnIo = temperatures.PawnIo;
            Log.Info($"Lecturas avanzadas: sensores abiertos en {opening.ElapsedMilliseconds} ms");
            while (!parent.HasExited)
            {
                var reading = Stopwatch.StartNew();
                (cpu, gpu) = temperatures.Read();
                if (reading.ElapsedMilliseconds > 2000)
                    Log.Info($"Lecturas avanzadas: leer las temperaturas tardó {reading.ElapsedMilliseconds} ms");
                // Cada 2 s: las temperaturas cambian despacio y leer el hardware (sobre todo la placa) es
                // lo que más consume de este proceso. Los FPS siguen saliendo cada segundo.
                Thread.Sleep(2000);
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
                    else if (command.StartsWith("run ", StringComparison.Ordinal))
                    {
                        // "run número pedido": una acción de la lista fija, en su propio hilo (la limpieza
                        // puede tardar y las lecturas siguen).
                        var bits = command.Split(' ');
                        if (bits.Length == 3 && int.TryParse(bits[1], out int id))
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                int code = trusted ? Tweaks.ElevatedBroker.Run(bits[2]) : Tweaks.ElevatedBroker.Rejected;
                                try
                                {
                                    Send($"ran {id} {code}");
                                }
                                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                                {
                                    // Strata UI se cerró mientras tanto: no hay a quién avisar.
                                }
                            });
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
            var counts = frames?.TakeCounts() ?? new Dictionary<int, FrameRate>();
            // El tipo de generación de cuadros solo se busca en los procesos que la usan.
            // Si el juego no deja ver sus bibliotecas (anti-trampas), las marcas de Reflex de
            // presentación fuera de banda delatan a DLSS Frame Generation.
            var kinds = counts.Where(c => c.Value.Shown >= c.Value.Real * 1.4)
                .Select(c => (Pid: c.Key, Kind: probe.KindOf(c.Key) ?? (frames!.OutOfBand.Contains(c.Key) ? "DLSS" : null)))
                .Where(k => k.Kind != null);
            string line = string.Create(CultureInfo.InvariantCulture,
                $"cpu={cpu:0.0};gpu={gpu:0.0};pawnio={(pawnIo ? 1 : 0)}"
                + $";fps={string.Join(",", counts.Select(c => $"{c.Key}:{c.Value.Shown}:{c.Value.Real}"))}"
                + $";fg={string.Join(",", kinds.Select(k => $"{k.Pid}:{k.Kind}"))}");
            if (frames is { Debug: true } && ++sent % 10 == 1)
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

    /// <summary>Marcas de tiempo (ms del reloj de la sesión) de un tipo de cuadro de un proceso.</summary>
    internal sealed class FrameTrack
    {
        public readonly Queue<double> Times = new();
        public double Last;

        public void Add(double time)
        {
            Times.Enqueue(time);
            Last = time;
        }

        /// <summary>
        /// Cuadros por segundo en el segundo que termina en <paramref name="now"/>: con cuadros
        /// recientes, el promedio de sus intervalos (no salta de a uno por redondeo); si el proceso
        /// dejó de presentar, los que quedan en la ventana (va bajando hasta 0).
        /// </summary>
        public int Rate(double now)
        {
            while (Times.Count > 0 && Times.Peek() <= now - 1000)
                Times.Dequeue();
            int count = Times.Count;
            if (count >= 3 && now - Last < 250 && Last > Times.Peek())
                return (int)Math.Round((count - 1) * 1000.0 / (Last - Times.Peek()));
            return count;
        }
    }

    /// <summary>Cuadros de un proceso en el último segundo: los que llegan a la pantalla y los que dibujó la app.</summary>
    internal readonly record struct FrameRate(int Shown, int Real);

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
