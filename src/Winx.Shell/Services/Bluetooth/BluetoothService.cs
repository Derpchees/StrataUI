using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Winx.Shell.Services.Bluetooth;

public enum BtKind { Audio, Input, Phone, Computer, Other }

public sealed record BtDevice(string Id, ulong Address, string Name, BtKind Kind, bool Connected, bool CanConnect);

/// <summary>Radio Bluetooth y dispositivos emparejados (WinRT), más conexión de audio vía KS.</summary>
public static class BluetoothService
{
    public static async Task<Radio?> GetRadioAsync()
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            return null;
        return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
    }

    public static async Task<IReadOnlyList<BtDevice>> GetPairedDevicesAsync()
    {
        var devices = new Dictionary<ulong, BtDevice>();

        // Bluetooth clásico (audio, teclados, teléfonos…)
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
        {
            using var device = await BluetoothDevice.FromIdAsync(info.Id);
            if (device is null)
                continue;
            var kind = device.ClassOfDevice.MajorClass switch
            {
                BluetoothMajorClass.AudioVideo => BtKind.Audio,
                BluetoothMajorClass.Peripheral => BtKind.Input,
                BluetoothMajorClass.Phone => BtKind.Phone,
                BluetoothMajorClass.Computer => BtKind.Computer,
                _ => BtKind.Other,
            };
            bool isAudio = kind == BtKind.Audio || BluetoothAudio.IsAudioDevice(device.BluetoothAddress);
            devices[device.BluetoothAddress] = new BtDevice(info.Id, device.BluetoothAddress, Name(device.Name, info.Name),
                isAudio ? BtKind.Audio : kind, device.ConnectionStatus == BluetoothConnectionStatus.Connected, isAudio);
        }

        // Bluetooth LE (mouses, relojes…); si ya está como clásico, no se duplica.
        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)))
        {
            using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device is null || devices.ContainsKey(device.BluetoothAddress))
                continue;
            // Teclados y mouses LE son subcategorías de HID.
            var kind = device.Appearance.Category == BluetoothLEAppearanceCategories.HumanInterfaceDevice
                ? BtKind.Input
                : BtKind.Other;
            devices[device.BluetoothAddress] = new BtDevice(info.Id, device.BluetoothAddress, Name(device.Name, info.Name),
                kind, device.ConnectionStatus == BluetoothConnectionStatus.Connected, false);
        }

        return devices.Values
            .OrderByDescending(d => d.Connected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string Name(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? (string.IsNullOrWhiteSpace(fallback) ? "Dispositivo" : fallback) : primary;

    /// <summary>Conecta o desconecta un dispositivo de audio. Devuelve false si no se pudo.</summary>
    public static Task<bool> SetAudioConnectedAsync(BtDevice device, bool connect) =>
        Task.Run(() => connect ? BluetoothAudio.Connect(device.Address) : BluetoothAudio.Disconnect(device.Address));

    public static async Task<bool> UnpairAsync(BtDevice device)
    {
        var info = await DeviceInformation.CreateFromIdAsync(device.Id);
        var result = await info.Pairing.UnpairAsync();
        return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
    }
}
