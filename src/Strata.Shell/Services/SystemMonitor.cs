using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Strata.Shell.Services;

/// <summary>
/// Uso de CPU, GPU y memoria para los medidores de la barra. CPU y GPU salen de los contadores de
/// rendimiento de Windows (PDH, los mismos del Administrador de tareas: "% Processor Utility" y
/// "GPU Engine"), sin administrador. Las temperaturas y los FPS los da el proceso de lecturas
/// avanzadas (<see cref="Sensors"/>). Un solo temporizador para todas las barras, que solo corre
/// mientras alguna muestra un medidor.
/// </summary>
public static class SystemMonitor
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private static DispatcherTimer? _timer;
    private static int _users;
    private static bool _sampling;
    private static IntPtr _query;
    private static IntPtr _cpuCounter, _gpuCounter;
    private static bool _pdhFailed;

    /// <summary>Uso total de la CPU (0-100); null hasta la segunda lectura.</summary>
    public static double? CpuPercent { get; private set; }

    /// <summary>Uso de la GPU más ocupada (el motor más ocupado, como el Administrador de tareas).</summary>
    public static double? GpuPercent { get; private set; }

    public static double RamPercent { get; private set; }
    public static ulong RamUsed { get; private set; }
    public static ulong RamTotal { get; private set; }

    /// <summary>Hay datos nuevos (en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>Una barra empieza a mostrar medidores: arranca el temporizador compartido.</summary>
    public static void Subscribe()
    {
        if (_users++ > 0)
            return;
        _timer ??= new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) => Sample(),
                                       System.Windows.Application.Current.Dispatcher);
        _timer.Start();
        Sample();
    }

    public static void Unsubscribe()
    {
        if (_users == 0 || --_users > 0)
            return;
        _timer?.Stop();
    }

    private static void Sample()
    {
        if (_sampling)
            return;
        _sampling = true;
        _ = Task.Run(() =>
        {
            var (cpu, gpu) = ReadCounters();
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            bool memoryOk = GlobalMemoryStatusEx(ref memory);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _sampling = false;
                CpuPercent = cpu;
                GpuPercent = gpu;
                if (memoryOk)
                {
                    RamTotal = memory.TotalPhys;
                    RamUsed = memory.TotalPhys - memory.AvailPhys;
                    RamPercent = memory.MemoryLoad;
                }
                Changed?.Invoke();
            });
        });
    }

    /// <summary>Memoria disponible ahora (para saber cuánto liberó "Liberar RAM").</summary>
    public static ulong AvailableRam()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? memory.AvailPhys : 0;
    }

    // ── PDH ─────────────────────────────────────────────────────────────────

    private static (double? Cpu, double? Gpu) ReadCounters()
    {
        if (_pdhFailed)
            return (null, null);
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
                    throw new InvalidOperationException("PdhOpenQuery");
                PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _cpuCounter);
                // Todas las instancias: una por proceso y motor de cada GPU ("pid_…_luid_…_eng_N_engtype_3D").
                PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpuCounter);
            }
            if (PdhCollectQueryData(_query) != 0)
                return (null, null);
            double? cpu = null;
            if (_cpuCounter != IntPtr.Zero
                && PdhGetFormattedCounterValue(_cpuCounter, FmtDouble | FmtNoCap100, out _, out var value) == 0
                && value.CStatus is 0 or 1)
                cpu = Math.Clamp(value.DoubleValue, 0, 100);
            return (cpu, _gpuCounter == IntPtr.Zero ? null : GpuUsage());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _pdhFailed = true;
            Log.Info($"No se pueden leer los contadores de rendimiento: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Suma el uso de todos los procesos en cada motor (3D, copia, video…) de cada GPU y devuelve
    /// el del motor más ocupado: así lo calcula el Administrador de tareas.
    /// </summary>
    private static double? GpuUsage()
    {
        uint size = 0;
        int status = PdhGetFormattedCounterArray(_gpuCounter, FmtDouble, ref size, out uint count, IntPtr.Zero);
        if (status != PdhMoreData || size == 0)
            return count == 0 && status == 0 ? 0 : null;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_gpuCounter, FmtDouble, ref size, out count, buffer) != 0)
                return null;
            var engines = new Dictionary<string, double>();
            int itemSize = Marshal.SizeOf<CounterValueItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterValueItem>(buffer + i * itemSize);
                if (item.Value.CStatus is not (0 or 1) || Marshal.PtrToStringUni(item.Name) is not { } name)
                    continue;
                string engine = EngineOf(name);
                engines[engine] = engines.GetValueOrDefault(engine) + item.Value.DoubleValue;
            }
            return engines.Count == 0 ? 0 : Math.Clamp(engines.Values.Max(), 0, 100);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>"pid_4340_luid_0x0_0x122fd_phys_0_eng_0_engtype_3D" → "luid_0x0_0x122fd_phys_0_eng_0_engtype_3D".</summary>
    internal static string EngineOf(string instance)
    {
        int luid = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return luid >= 0 ? instance[luid..] : instance;
    }

    private const uint FmtDouble = 0x00000200, FmtNoCap100 = 0x00008000;
    private const int PdhMoreData = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    // ── Memoria ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
