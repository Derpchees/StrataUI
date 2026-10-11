using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace Winx.Shell.Services.Network;

public sealed record ConnectionInfo(bool Connected, bool IsWifi, string Name, string Status, string? Address);

public sealed record WifiNetwork(WiFiAvailableNetwork Network, string Ssid, int Bars, bool Secured, bool Connected);

public enum WifiAccess { Available, NoAdapter, Denied }

public enum WifiConnectResult { Connected, NeedsPassword, WrongPassword, Failed }

/// <summary>Estado de la conexión y control del Wi-Fi mediante las APIs WinRT de Windows.</summary>
public sealed class NetworkService
{
    private WiFiAdapter? _adapter;

    public static ConnectionInfo GetConnection()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile is null)
            return new ConnectionInfo(false, false, "Sin conexión", "No hay ninguna red conectada", null);

        var level = profile.GetNetworkConnectivityLevel();
        string status = level switch
        {
            NetworkConnectivityLevel.InternetAccess => "Conectado, con acceso a Internet",
            NetworkConnectivityLevel.ConstrainedInternetAccess => "Conectado, acceso limitado",
            NetworkConnectivityLevel.LocalAccess => "Conectado, sin Internet",
            _ => "Sin conexión",
        };
        bool wifi = profile.IsWlanConnectionProfile;
        string name = wifi
            ? profile.WlanConnectionProfileDetails?.GetConnectedSsid() ?? profile.ProfileName
            : profile.ProfileName;
        return new ConnectionInfo(level != NetworkConnectivityLevel.None, wifi, name, status,
                                  GetAddress(profile.NetworkAdapter?.NetworkAdapterId));
    }

    private static string? GetAddress(Guid? adapterId)
    {
        if (adapterId is null)
            return null;
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => Guid.TryParse(n.Id, out var id) && id == adapterId);
        return nic?.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
    }

    public async Task<WifiAccess> InitializeWifiAsync()
    {
        if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed)
            return WifiAccess.Denied;
        _adapter = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
        return _adapter is null ? WifiAccess.NoAdapter : WifiAccess.Available;
    }

    public static async Task<Radio?> GetWifiRadioAsync()
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            return null;
        return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
    }

    /// <summary>Busca redes; agrupa por nombre quedándose con la señal más fuerte.</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync()
    {
        if (_adapter is null)
            return [];
        await _adapter.ScanAsync();
        var connectedSsid = (await _adapter.NetworkAdapter.GetConnectedProfileAsync())
            ?.WlanConnectionProfileDetails?.GetConnectedSsid();

        return _adapter.NetworkReport.AvailableNetworks
            .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
            .GroupBy(n => n.Ssid)
            .Select(g => g.OrderByDescending(n => n.SignalBars).First())
            .Select(n => new WifiNetwork(n, n.Ssid, n.SignalBars,
                n.SecuritySettings.NetworkAuthenticationType is not (NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None),
                n.Ssid == connectedSsid))
            .OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.Bars)
            .ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<WifiConnectResult> ConnectAsync(WifiNetwork network, string? password)
    {
        if (_adapter is null)
            return WifiConnectResult.Failed;

        var result = string.IsNullOrEmpty(password)
            ? await _adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic)
            : await _adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic,
                                          new PasswordCredential { Password = password });

        return result.ConnectionStatus switch
        {
            WiFiConnectionStatus.Success => WifiConnectResult.Connected,
            WiFiConnectionStatus.InvalidCredential when string.IsNullOrEmpty(password) => WifiConnectResult.NeedsPassword,
            WiFiConnectionStatus.InvalidCredential => WifiConnectResult.WrongPassword,
            _ => WifiConnectResult.Failed,
        };
    }

    public void Disconnect() => _adapter?.Disconnect();
}
