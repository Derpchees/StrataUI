using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Winx.Shell.Services;

/// <summary>
/// Lecturas avanzadas: temperatura de CPU y GPU y FPS. Windows solo las da con administrador, así
/// que las hace un proceso aparte (WINX.exe --sensors, elevado con UAC al activarlas) que manda los
/// datos por una tubería con nombre cada segundo y termina cuando WINX se cierra.
/// </summary>
/// <remarks>
/// <para>Temperaturas: LibreHardwareMonitor. La de la CPU necesita además el controlador firmado
/// PawnIO (pawnio.eu); sin él solo hay temperatura de GPU (NVIDIA y AMD).</para>
/// <para>FPS: eventos de presentación de DXGI, Direct3D 9 y del núcleo gráfico (ETW, lo mismo que
/// PresentMon) contados por proceso. WINX elige el proceso: la app que ocupa el monitor elegido.</para>
/// <para>También libera la RAM ("free") sin pedir UAC otra vez mientras está abierto.</para>
/// </remarks>
public static class Sensors
{

    private static NamedPipeServerStream? _pipe;
    private static StreamWriter? _writer;
    private static Dictionary<int, int> _presents = new();
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

    /// <summary>Presentaciones por segundo de un proceso (null si no presenta nada o no hay datos).</summary>
    public static int? FpsOf(int processId) =>
        Running && _presents.TryGetValue(processId, out int count) && count > 0 ? count : null;

    // ── App medida ──────────────────────────────────────────────────────────

    /// <summary>
    /// Proceso cuyos FPS se muestran: la ventana activa si está en el monitor elegido; si no, la de
    /// más arriba que ocupe buena parte de ese monitor (un juego en otra pantalla sigue contando
    /// aunque se use la otra). Null si no hay ninguna o las lecturas no están activas.
    /// </summary>
    public static int? TargetProcess(string? device)
    {
        if (!Running)
            return null;
        var screens = System.Windows.Forms.Screen.AllScreens;
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

    private static readonly Dictionary<int, string> Names = new();

    /// <summary>Nombre del proceso medido (para el tooltip).</summary>
    public static string ProcessName(int processId)
    {
        if (Names.TryGetValue(processId, out var name))
            return name;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.MainWindowTitle is { Length: > 0 } title ? title : process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            name = processId.ToString(CultureInfo.InvariantCulture);
        }
        if (Names.Count > 64)
            Names.Clear();
        return Names[processId] = name;
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
    /// vez (o tras actualizar WINX) pide UAC para crearla. Si la tarea no se puede usar, se lanza
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
    /// pregunta. Lo usan las barras con medidores al abrir WINX. Una vez por sesión.
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
            while (await reader.ReadLineAsync() is { } line)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Apply(line));
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

    /// <summary>Una línea del proceso elevado: "cpu=54.0;gpu=61;pawnio=1;fps=1234:144,5678:60" o "freed".</summary>
    internal static void Apply(string line)
    {
        if (line == "freed")
        {
            _freed?.TrySetResult(true);
            return;
        }
        var presents = new Dictionary<int, int>();
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
                        if (bits.Length == 2 && int.TryParse(bits[0], out int pid) && int.TryParse(bits[1], out int count))
                            presents[pid] = count;
                    }
                    break;
            }
        }
        _presents = presents;
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
    /// WINX.exe --sensors-task (lo inicia la tarea de <see cref="SensorsTask"/>): se conecta a la
    /// tubería del usuario, comprueba que quien la abrió sea WINX y solo manda lecturas y atiende
    /// "free". Termina cuando ese WINX se cierra.
    /// </summary>
    public static int RunHelper(string[] args)
    {
        if (args.Length != 1)
            return 2;
        using var pipe = new NamedPipeClientStream(".", SensorsTask.PipeName(SensorsTask.UserSid), PipeDirection.InOut);
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
            if (!parent.ProcessName.Equals("WINX", StringComparison.OrdinalIgnoreCase))
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

        using var temperatures = new Temperatures();
        using var frames = new FrameCounter();
        frames.Start();

        // Órdenes de WINX: "free" libera la RAM.
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

        while (!parent.HasExited && pipe.IsConnected)
        {
            Thread.Sleep(1000);
            var (cpu, gpu) = temperatures.Read();
            var counts = frames.TakeCounts();
            string line = string.Create(CultureInfo.InvariantCulture,
                $"cpu={cpu:0.0};gpu={gpu:0.0};pawnio={(temperatures.PawnIo ? 1 : 0)};fps={string.Join(",", counts.Select(c => $"{c.Key}:{c.Value}"))}");
            try
            {
                Send(line);
            }
            catch (IOException)
            {
                break;
            }
        }
        return 0;
    }

    /// <summary>Temperaturas con LibreHardwareMonitor (CPU: paquete o el núcleo más caliente; GPU: núcleo).</summary>
    private sealed class Temperatures : IDisposable
    {
        private readonly LibreHardwareMonitor.Hardware.Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };

        public bool PawnIo { get; }

        public Temperatures()
        {
            try
            {
                PawnIo = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
                _computer.Open();
            }
            catch (Exception)
            {
                // Sin sensores: se mandan vacíos.
            }
        }

        public (double? Cpu, double? Gpu) Read()
        {
            double? cpu = null, gpu = null;
            bool integrated = false;
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    // Intel integrada: no informa temperatura propia; está en el mismo chip que la CPU.
                    if (hardware.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuIntel)
                        integrated = true;
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
            catch (Exception)
            {
            }
            if (gpu is null && integrated)
                gpu = cpu;
            return (cpu, gpu);
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

    /// <summary>Cuenta las presentaciones de cada proceso con ETW (DXGI, Direct3D 9 y DxgKrnl).</summary>
    private sealed class FrameCounter : IDisposable
    {
        private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
        private const int DxgiPresentStart = 42, D3D9PresentStart = 1, DxgKrnlPresentInfo = 184;

        private Microsoft.Diagnostics.Tracing.Session.TraceEventSession? _session;
        private readonly object _lock = new();
        private Dictionary<int, int> _api = new(), _kernel = new();

        public void Start()
        {
            try
            {
                const string name = "WINX-FPS";
                // Una sesión con el mismo nombre que quedó abierta (WINX murió) se reemplaza.
                _session = new Microsoft.Diagnostics.Tracing.Session.TraceEventSession(name) { StopOnDispose = true };
                _session.EnableProvider(Dxgi, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational);
                _session.EnableProvider(D3D9, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational);
                _session.EnableProvider(DxgKrnl, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational, 0x8000000 | 0x1);
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
            catch (Exception)
            {
                _session = null; // sin FPS
            }
        }

        private void OnEvent(Microsoft.Diagnostics.Tracing.TraceEvent e)
        {
            int id = (int)e.ID;
            Dictionary<int, int>? target =
                (e.ProviderGuid == Dxgi && id == DxgiPresentStart) || (e.ProviderGuid == D3D9 && id == D3D9PresentStart) ? _api
                : e.ProviderGuid == DxgKrnl && id == DxgKrnlPresentInfo ? _kernel
                : null;
            if (target is null)
                return;
            lock (_lock)
                target[e.ProcessID] = target.GetValueOrDefault(e.ProcessID) + 1;
        }

        /// <summary>
        /// Presentaciones del último segundo por proceso: las de DXGI/D3D9 si las hay; si no
        /// (OpenGL, Vulkan), las del núcleo gráfico.
        /// </summary>
        public Dictionary<int, int> TakeCounts()
        {
            Dictionary<int, int> api, kernel;
            lock (_lock)
            {
                (api, _api) = (_api, new());
                (kernel, _kernel) = (_kernel, new());
            }
            foreach (var (pid, count) in kernel)
                api.TryAdd(pid, count);
            return api;
        }

        public void Dispose() => _session?.Dispose();
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
