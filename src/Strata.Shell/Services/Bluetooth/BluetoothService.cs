using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Bluetooth;

public enum BtKind { Audio, Input, Phone, Computer, Other }

/// <param name="Battery">Carga en % (solo conectados que la informan).</param>
public sealed record BtDevice(string Id, ulong Address, string Name, BtKind Kind, bool Connected, bool CanConnect, int? Battery = null);

/// <summary>Lo que el dispositivo pide al emparejar: confirmar, ver un PIN o escribirlo.</summary>
public enum BtPrompt { ConfirmPin, DisplayPin, ProvidePin }

/// <summary>Radio Bluetooth y dispositivos emparejados (WinRT), más conexión de audio vía KS.</summary>
public static class BluetoothService
{
    // Propiedades de los extremos de asociación (AEP): las mismas que lee Configuración de Windows.
    public const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    public const string IsPairedKey = "System.Devices.Aep.IsPaired";
    public const string AddressKey = "System.Devices.Aep.DeviceAddress";
    public const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    // DEVPKEY_Bluetooth_Battery: la carga que muestra Configuración (en el nodo del dispositivo, no en el AEP).
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LeProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    public static async Task<Radio?> GetRadioAsync()
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            return null;
        return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
    }

    public static async Task<IReadOnlyList<BtDevice>> GetPairedDevicesAsync()
    {
        var devices = new Dictionary<ulong, BtDevice>();
        string[] properties = [IsConnectedKey, ContainerKey];

        // Bluetooth clásico (audio, teclados, teléfonos…)
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), properties))
        {
            using var device = await BluetoothDevice.FromIdAsync(info.Id);
            if (device is null)
                continue;
            var kind = KindOf(device.ClassOfDevice.MajorClass);
            bool isAudio = kind == BtKind.Audio || BluetoothAudio.IsAudioDevice(device.BluetoothAddress);
            devices[device.BluetoothAddress] = new BtDevice(info.Id, device.BluetoothAddress, Name(device.Name, info.Name),
                isAudio ? BtKind.Audio : kind, IsConnected(info, device.ConnectionStatus), isAudio);
        }

        // Bluetooth LE (mouses, relojes…); si ya está como clásico, no se duplica.
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), properties))
        {
            using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device is null)
                continue;
            if (devices.TryGetValue(device.BluetoothAddress, out var classic))
            {
                // Dispositivo doble (clásico y LE): conectado si lo está por cualquiera de los dos.
                if (!classic.Connected && IsConnected(info, device.ConnectionStatus))
                    devices[device.BluetoothAddress] = classic with { Connected = true };
                continue;
            }
            // Teclados y mouses LE son subcategorías de HID.
            var kind = device.Appearance.Category == BluetoothLEAppearanceCategories.HumanInterfaceDevice
                ? BtKind.Input
                : BtKind.Other;
            devices[device.BluetoothAddress] = new BtDevice(info.Id, device.BluetoothAddress, Name(device.Name, info.Name),
                kind, IsConnected(info, device.ConnectionStatus), false);
        }

        foreach (var (address, device) in devices.Where(d => d.Value.Connected).ToList())
            devices[address] = device with { Battery = await BatteryAsync(device.Id) };

        return devices.Values
            .OrderByDescending(d => d.Connected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// ConnectionStatus de WinRT solo dice "conectado" si hay un enlace abierto por esta app (con
    /// audífonos y mouses suele decir que no): la propiedad de AEP es la que muestra Windows.
    /// </summary>
    private static bool IsConnected(DeviceInformation info, BluetoothConnectionStatus status) =>
        Bool(info.Properties, IsConnectedKey) ?? status == BluetoothConnectionStatus.Connected;

    /// <summary>Carga de la batería que informa el dispositivo (audífonos, mouses…), o null.</summary>
    private static async Task<int?> BatteryAsync(string aepId)
    {
        try
        {
            var endpoint = await DeviceInformation.CreateFromIdAsync(aepId, [ContainerKey], DeviceInformationKind.AssociationEndpoint);
            if (!endpoint.Properties.TryGetValue(ContainerKey, out var value) || value is not Guid container)
                return null;
            // El dispositivo y sus servicios comparten contenedor: cualquiera de ellos puede llevar la carga.
            var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"",
                [BatteryKey], DeviceInformationKind.Device);
            foreach (var node in nodes)
                if (node.Properties.TryGetValue(BatteryKey, out var battery) && battery is not null)
                    return Math.Clamp(Convert.ToInt32(battery), 0, 100);
        }
        catch (Exception ex)
        {
            Log.Info($"Bluetooth: no se pudo leer la batería ({ex.Message})");
        }
        return null;
    }

    public static bool? Bool(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is bool b ? b : null;

    /// <summary>Dirección del dispositivo de un extremo de AEP ("aa:bb:cc:dd:ee:ff"), o 0.</summary>
    public static ulong AddressOf(DeviceInformation info) =>
        info.Properties.TryGetValue(AddressKey, out var value) && value is string text
        && ulong.TryParse(text.Replace(":", "").Replace("-", ""), System.Globalization.NumberStyles.HexNumber, null, out ulong address)
            ? address
            : 0;

    /// <summary>Tipo de un dispositivo cercano, por la clase que anuncia (solo el clásico la tiene).</summary>
    public static BtKind KindOf(DeviceInformation info) =>
        info.Properties.TryGetValue(CodMajorKey, out var value) && value is not null
            ? KindOf((BluetoothMajorClass)Convert.ToInt32(value))
            : BtKind.Other;

    private static BtKind KindOf(BluetoothMajorClass major) => major switch
    {
        BluetoothMajorClass.AudioVideo => BtKind.Audio,
        BluetoothMajorClass.Peripheral => BtKind.Input,
        BluetoothMajorClass.Phone => BtKind.Phone,
        BluetoothMajorClass.Computer => BtKind.Computer,
        _ => BtKind.Other,
    };

    /// <summary>
    /// Vigila los dispositivos Bluetooth (clásicos y LE, emparejados o no) mientras el panel está
    /// abierto: los cercanos sin emparejar para ofrecerlos y los cambios de conexión de los
    /// emparejados. Con IssueInquiry, Windows busca los clásicos de forma activa.
    /// </summary>
    public static DeviceWatcher CreateWatcher() => DeviceInformation.CreateWatcher(
        $"(System.Devices.Aep.ProtocolId:=\"{ClassicProtocol}\" AND System.Devices.Aep.Bluetooth.IssueInquiry:=System.StructuredQueryType.Boolean#True)"
        + $" OR System.Devices.Aep.ProtocolId:=\"{LeProtocol}\"",
        [IsConnectedKey, IsPairedKey, AddressKey, CodMajorKey],
        DeviceInformationKind.AssociationEndpoint);

    public static string Name(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? (string.IsNullOrWhiteSpace(fallback) ? Loc.T("Dispositivo") : fallback) : primary;

    /// <summary>Conecta o desconecta un dispositivo de audio. Devuelve false si no se pudo.</summary>
    public static Task<bool> SetAudioConnectedAsync(BtDevice device, bool connect) =>
        Task.Run(() => connect ? BluetoothAudio.Connect(device.Address) : BluetoothAudio.Disconnect(device.Address));

    public static async Task<bool> UnpairAsync(BtDevice device)
    {
        var info = await DeviceInformation.CreateFromIdAsync(device.Id);
        var result = await info.Pairing.UnpairAsync();
        return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
    }

    /// <summary>
    /// Empareja un dispositivo cercano sin la ventana de Windows. Si pide confirmar o escribir un PIN,
    /// <paramref name="ask"/> lo muestra (desde cualquier hilo) y devuelve si se aceptó y el PIN
    /// escrito; con <see cref="BtPrompt.DisplayPin"/> solo se informa (se escribe en el dispositivo).
    /// </summary>
    public static async Task<DevicePairingResultStatus> PairAsync(string id, Func<BtPrompt, string, Task<(bool Accept, string? Pin)>> ask)
    {
        var info = await DeviceInformation.CreateFromIdAsync(id);
        if (info.Pairing.IsPaired)
            return DevicePairingResultStatus.AlreadyPaired;
        var custom = info.Pairing.Custom;

        async void OnRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                    args.Accept();
                    break;
                case DevicePairingKinds.DisplayPin:
                    _ = ask(BtPrompt.DisplayPin, args.Pin);
                    args.Accept();
                    break;
                case DevicePairingKinds.ConfirmPinMatch or DevicePairingKinds.ProvidePin:
                    using (var deferral = args.GetDeferral())
                    {
                        var prompt = args.PairingKind == DevicePairingKinds.ProvidePin ? BtPrompt.ProvidePin : BtPrompt.ConfirmPin;
                        var (accept, pin) = await ask(prompt, args.Pin);
                        if (accept && prompt == BtPrompt.ProvidePin)
                            args.Accept(pin ?? "");
                        else if (accept)
                            args.Accept();
                    }
                    break;
            }
        }

        custom.PairingRequested += OnRequested;
        try
        {
            var result = await custom.PairAsync(
                DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin
                | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin);
            Log.Info($"Bluetooth: emparejar {info.Name}: {result.Status}");
            return result.Status;
        }
        finally
        {
            custom.PairingRequested -= OnRequested;
        }
    }
}
