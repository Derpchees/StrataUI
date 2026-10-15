using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Strata.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Services.Brightness;

/// <summary>
/// Un monitor cuyo brillo se puede cambiar: externos por DDC/CI (el mismo canal que los botones
/// del monitor) y la pantalla de un portátil por WMI.
/// </summary>
/// <remarks>
/// Las órdenes por DDC/CI tardan decenas de milisegundos y el monitor ignora las que llegan muy
/// seguidas: se aplican en segundo plano y, mientras se arrastra el control, solo el último valor.
/// </remarks>
public sealed class BrightnessMonitor : INotifyPropertyChanged
{
    private readonly IntPtr _physical;
    private readonly uint _min, _max;
    private readonly object _gate = new();
    private int _percent;
    private int _pending = -1;
    private bool _working;

    internal BrightnessMonitor(string name, string device, IntPtr physical, uint min, uint max, int percent, bool isInternal = false)
    {
        IsInternal = isInternal;
        Name = name;
        Device = device;
        _physical = physical;
        _min = min;
        _max = max;
        _percent = percent;
    }

    public string Name { get; }

    /// <summary>Dispositivo de Windows ("\\.\DISPLAY1"); vacío en la pantalla integrada.</summary>
    public string Device { get; }

    /// <summary>Pantalla del portátil (WMI). No se deduce del identificador DDC: puede valer 0.</summary>
    public bool IsInternal { get; }

    /// <summary>Brillo de 0 a 100. Asignarlo lo aplica en segundo plano.</summary>
    public int Percent
    {
        get => _percent;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (value == _percent)
                return;
            _percent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Percent)));
            Request(value);
        }
    }

    private void Request(int percent)
    {
        lock (_gate)
        {
            _pending = percent;
            if (_working)
                return;
            _working = true;
        }
        Task.Run(ApplyPending);
    }

    private void ApplyPending()
    {
        while (true)
        {
            int value;
            lock (_gate)
            {
                value = _pending;
                _pending = -1;
                if (value < 0)
                {
                    _working = false;
                    return;
                }
            }
            try
            {
                if (IsInternal)
                    BrightnessService.SetInternal(value);
                else
                    BrightnessService.SetPhysical(_physical, (uint)Math.Round(_min + (_max - _min) * value / 100.0));
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo cambiar el brillo de {Name}", ex);
            }
        }
    }

    internal IntPtr Physical => _physical;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Monitores con brillo ajustable (se detectan en segundo plano).</summary>
public static class BrightnessService
{
    private static List<PhysicalMonitor[]> _handles = new();
    private static Task? _loading;

    public static ObservableCollection<BrightnessMonitor> Monitors { get; } = new();

    /// <summary>Terminó una detección (para mostrar u ocultar el botón de la barra).</summary>
    public static event Action? Changed;

    public static bool Available => Monitors.Count > 0;

    /// <summary>
    /// Vuelve a detectar los monitores (al iniciar, al abrir el panel y al conectar uno). Si ya hay
    /// una detección en curso, se espera a esa.
    /// </summary>
    public static Task RefreshAsync()
    {
        if (_loading is { IsCompleted: false })
            return _loading;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        return _loading = Task.Run(Detect).ContinueWith(t =>
        {
            if (t.Exception != null)
                Log.Error("No se pudieron detectar los monitores para el brillo", t.Exception);
            var (found, handles) = t.IsCompletedSuccessfully ? t.Result : ([], []);
            dispatcher.Invoke(() =>
            {
                ReleaseHandles();
                _handles = handles;
                Monitors.Clear();
                foreach (var monitor in found)
                    Monitors.Add(monitor);
                Changed?.Invoke();
            });
        });
    }

    /// <summary>Monitor de la pantalla donde está ese punto (para la rueda sobre el botón).</summary>
    public static BrightnessMonitor? MonitorAt(System.Drawing.Point point)
    {
        var device = Forms.Screen.FromPoint(point).DeviceName;
        return Monitors.FirstOrDefault(m => m.Device == device) ?? Monitors.FirstOrDefault(m => m.IsInternal);
    }

    public static void Shutdown() => ReleaseHandles();

    private static void ReleaseHandles()
    {
        foreach (var group in _handles)
            DestroyPhysicalMonitors((uint)group.Length, group);
        _handles = new();
    }

    // ── Detección ───────────────────────────────────────────────────────────

    private static (List<BrightnessMonitor>, List<PhysicalMonitor[]>) Detect()
    {
        var monitors = new List<BrightnessMonitor>();
        var handles = new List<PhysicalMonitor[]>();
        // Mismo orden y nombres que el Hub: de izquierda a derecha.
        var screens = Forms.Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();

        if (ReadInternal() is { } internalPercent)
            monitors.Add(new BrightnessMonitor(Loc.T("Pantalla integrada"), "", IntPtr.Zero, 0, 100, internalPercent, isInternal: true));

        foreach (var (hMonitor, device) in DisplayMonitors())
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                continue;
            var physical = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical))
                continue;
            handles.Add(physical);
            int index = screens.FindIndex(s => s.DeviceName == device);
            foreach (var p in physical)
            {
                if (!ReadPhysical(p.Handle, out uint min, out uint current, out uint max) || max <= min)
                    continue;
                string name = screens.Count == 1 || index < 0 ? Loc.T("Monitor") : Loc.F("Monitor {0}", index + 1);
                int percent = (int)Math.Round((current - min) * 100.0 / (max - min));
                monitors.Add(new BrightnessMonitor(name, device, p.Handle, min, max, percent));
            }
        }
        // Mismo orden que los nombres (de izquierda a derecha); la pantalla integrada primero.
        var ordered = monitors.OrderBy(m => m.IsInternal ? -1 : screens.FindIndex(s => s.DeviceName == m.Device)).ToList();
        return (ordered, handles);
    }

    private static bool ReadPhysical(IntPtr handle, out uint min, out uint current, out uint max)
    {
        if (GetMonitorBrightness(handle, out min, out current, out max))
            return true;
        // Algunos monitores no declaran la función de brillo pero sí aceptan el código VCP 0x10.
        min = 0;
        return GetVCPFeatureAndVCPFeatureReply(handle, VcpBrightness, IntPtr.Zero, out current, out max);
    }

    internal static void SetPhysical(IntPtr handle, uint value)
    {
        if (!SetMonitorBrightness(handle, value))
            SetVCPFeature(handle, VcpBrightness, value);
    }

    private static int? ReadInternal()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            foreach (ManagementObject item in searcher.Get())
                using (item)
                    return Convert.ToInt32(item["CurrentBrightness"]);
        }
        catch (ManagementException)
        {
            // Sin pantalla integrada (equipo de escritorio): WMI no tiene la clase.
        }
        return null;
    }

    internal static void SetInternal(int percent)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
        foreach (ManagementObject item in searcher.Get())
            using (item)
                item.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
    }

    private static List<(IntPtr Handle, string Device)> DisplayMonitors()
    {
        var list = new List<(IntPtr, string)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(hMonitor, ref info))
                list.Add((hMonitor, info.Device));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // ── Win32 ───────────────────────────────────────────────────────────────

    private const byte VcpBrightness = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public RectI Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectI { public int Left, Top, Right, Bottom; }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [DllImport("dxva2.dll")]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, IntPtr type, out uint current, out uint max);

    [DllImport("dxva2.dll")]
    private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
}
