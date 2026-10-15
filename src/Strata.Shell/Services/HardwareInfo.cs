using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>Modelo del procesador y de las tarjetas gráficas (para los tooltips de los medidores).</summary>
public static class HardwareInfo
{
    private static readonly Lazy<string?> Cpu = new(ReadCpu);
    private static readonly Lazy<IReadOnlyList<string>> Gpus = new(ReadGpus);

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
