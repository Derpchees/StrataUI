using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>Modelo del procesador y de las tarjetas gráficas (para los tooltips de los medidores).</summary>
public static class HardwareInfo
{
    private static readonly Lazy<string?> Cpu = new(ReadCpu);
    private static readonly Lazy<IReadOnlyList<string>> Gpus = new(ReadGpus);
    private static readonly Lazy<bool?> HwSch = new(ReadHwSch);

    /// <summary>
    /// Alguna gráfica admite la programación de GPU acelerada por hardware (WDDM 2.7);
    /// null si no se pudo saber.
    /// </summary>
    public static bool? GpuSchedulingSupported => HwSch.Value;

    /// <summary>"AMD Ryzen 7 7840HS w/ Radeon 780M Graphics"; null si no se pudo leer.</summary>
    public static string? CpuName => Cpu.Value;

    /// <summary>Gráficas de verdad (sin el adaptador por software), la dedicada primero.</summary>
    public static IReadOnlyList<string> GpuNames => Gpus.Value;

    private static string? ReadCpu()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0
                ? string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadGpus()
    {
        var adapters = new List<(string Name, ulong Memory)>();
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out var factory) != 0)
                return [];
            try
            {
                for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0; i++)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var desc) == 0 && (desc.Flags & SoftwareAdapter) == 0)
                            adapters.Add((desc.Description.Trim(), (ulong)desc.DedicatedVideoMemory));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Error("No se pudieron leer las tarjetas gráficas", ex);
        }
        return adapters.OrderByDescending(a => a.Memory)
            .Select(a => a.Name)
            .Where(name => name.Length > 0)
            .Distinct()
            .ToList();
    }

    private static bool? ReadHwSch()
    {
        bool? supported = null;
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out var factory) != 0)
                return null;
            try
            {
                for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0; i++)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var desc) != 0 || (desc.Flags & SoftwareAdapter) != 0)
                            continue;
                        var open = new OpenAdapterFromLuid { LuidLow = desc.LuidLow, LuidHigh = desc.LuidHigh };
                        if (D3DKMTOpenAdapterFromLuid(ref open) != 0)
                            continue;
                        try
                        {
                            var buffer = Marshal.AllocHGlobal(sizeof(uint));
                            try
                            {
                                Marshal.WriteInt32(buffer, 0);
                                var query = new QueryAdapterInfo { Adapter = open.Adapter, Type = Wddm27Caps, Data = buffer, Size = sizeof(uint) };
                                if (D3DKMTQueryAdapterInfo(ref query) != 0)
                                    continue;
                                // Primer bit: HwSchSupported.
                                supported = (Marshal.ReadInt32(buffer) & 1) != 0 || supported == true;
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(buffer);
                            }
                        }
                        finally
                        {
                            var close = new CloseAdapter { Adapter = open.Adapter };
                            D3DKMTCloseAdapter(ref close);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Error("No se pudo saber si la gráfica admite la programación por hardware", ex);
        }
        return supported;
    }

    // ── D3DKMT ──────────────────────────────────────────────────────────────

    private const int Wddm27Caps = 70; // KMTQAITYPE_WDDM_2_7_CAPS

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid { public uint LuidLow; public int LuidHigh; public uint Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo { public uint Adapter; public int Type; public IntPtr Data; public uint Size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter { public uint Adapter; }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid info);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo info);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter info);

    // ── DXGI ────────────────────────────────────────────────────────────────

    private const uint SoftwareAdapter = 2; // DXGI_ADAPTER_FLAG_SOFTWARE

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // IDXGIObject
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        // IDXGIFactory
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        // IDXGIFactory1
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // IDXGIObject
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        // IDXGIAdapter
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        // IDXGIAdapter1
        [PreserveSig] int GetDesc1(out AdapterDesc1 desc);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IDXGIFactory1 factory);
}
