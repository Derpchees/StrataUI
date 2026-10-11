using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Winx.Shell.Services.Bluetooth;

/// <summary>
/// Conecta/desconecta dispositivos de audio Bluetooth (audífonos, bocinas). Windows no tiene API
/// pública para esto; se envía KSPROPERTY_ONESHOT_RECONNECT/DISCONNECT al filtro de audio del
/// dispositivo, igual que el botón "Conectar" de Configuración (técnica usada por ToothTray).
/// </summary>
internal static class BluetoothAudio
{
    private static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint OneShotReconnect = 0;
    private const uint OneShotDisconnect = 1;
    private const uint KsPropertyTypeGet = 1;

    /// <summary>True si se encontró al menos un endpoint de audio de ese dispositivo.</summary>
    public static bool IsAudioDevice(ulong address) => FindFilterIds(address).Any();

    public static bool Connect(ulong address) => SendOneShot(address, OneShotReconnect);

    public static bool Disconnect(ulong address) => SendOneShot(address, OneShotDisconnect);

    private static bool SendOneShot(ulong address, uint property)
    {
        bool sent = false;
        foreach (var filterId in FindFilterIds(address))
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(enumerator.GetDevice(filterId, out var device));
                var iid = typeof(IKsControl).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var control));
                var request = new KsProperty { Set = KsPropSetIdBtAudio, Id = property, Flags = KsPropertyTypeGet };
                int hr = ((IKsControl)control).KsProperty(ref request, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                sent |= hr >= 0;
                Marshal.ReleaseComObject(control);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
            catch (COMException ex)
            {
                Log.Error($"KS oneshot falló en {filterId}", ex);
            }
        }
        return sent;
    }

    /// <summary>IDs de los filtros KS de audio cuyo identificador contiene la dirección Bluetooth.</summary>
    private static IEnumerable<string> FindFilterIds(ulong address)
    {
        string mac = address.ToString("x12");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var enumerator = new MMDeviceEnumerator();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All))
        {
            using var _ = endpoint; // objeto COM: se libera en cada vuelta
            try
            {
                var topology = endpoint.DeviceTopology;
                if (topology.ConnectorCount == 0)
                    continue;
                var filterId = topology.GetConnector(0).ConnectedToDeviceId;
                if (filterId != null && filterId.Contains(mac, StringComparison.OrdinalIgnoreCase))
                    ids.Add(filterId);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Endpoints sin topología (virtuales, desconectados): se ignoran.
            }
        }
        return ids;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                                   [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsMethod(IntPtr method, uint methodLength, IntPtr data, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsEvent(IntPtr evt, uint eventLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
