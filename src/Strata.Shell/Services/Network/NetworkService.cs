using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Network;

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
            return new ConnectionInfo(false, false, Loc.T("Sin conexión"), Loc.T("No hay ninguna red conectada"), null);

        var level = profile.GetNetworkConnectivityLevel();
        string status = level switch
        {
            NetworkConnectivityLevel.InternetAccess => Loc.T("Conectado, con acceso a Internet"),
            NetworkConnectivityLevel.ConstrainedInternetAccess => Loc.T("Conectado, acceso limitado"),
            NetworkConnectivityLevel.LocalAccess => Loc.T("Conectado, sin Internet"),
            _ => Loc.T("Sin conexión"),
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

        try
        {
            var connect = (string.IsNullOrEmpty(password)
                ? _adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic)
                : _adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic,
                                        new PasswordCredential { Password = password })).AsTask();

            // ConnectAsync a veces tarda mucho o no vuelve aunque Windows ya se conectó (espera la IP u
            // otros pasos): mientras tanto se mira si el adaptador ya está en esa red. El panel se quedaba
            // en "Conectando…" con la conexión hecha.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
            while (await Task.WhenAny(connect, Task.Delay(1000)) != connect)
            {
                if (await IsConnectedToAsync(network.Ssid))
                    return WifiConnectResult.Connected;
                if (DateTime.UtcNow > deadline)
                {
                    Log.Info($"Wi-Fi: {network.Ssid} no respondió a tiempo");
                    return WifiConnectResult.Failed;
                }
            }
            var result = await connect;
            if (result.ConnectionStatus != WiFiConnectionStatus.Success)
                Log.Info($"Wi-Fi: {network.Ssid} → {result.ConnectionStatus}");
            return result.ConnectionStatus switch
            {
                WiFiConnectionStatus.Success => WifiConnectResult.Connected,
                WiFiConnectionStatus.InvalidCredential when string.IsNullOrEmpty(password) => WifiConnectResult.NeedsPassword,
                WiFiConnectionStatus.InvalidCredential => WifiConnectResult.WrongPassword,
                // Algunas veces informa un error aunque la conexión quedó hecha.
                _ when await IsConnectedToAsync(network.Ssid) => WifiConnectResult.Connected,
                _ => WifiConnectResult.Failed,
            };
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo conectar a {network.Ssid}", ex);
            return await IsConnectedToAsync(network.Ssid) ? WifiConnectResult.Connected : WifiConnectResult.Failed;
        }
    }

    /// <summary>El adaptador Wi-Fi ya está conectado a esa red.</summary>
    private async Task<bool> IsConnectedToAsync(string ssid)
    {
        try
        {
            if (_adapter is null)
                return false;
            var profile = await _adapter.NetworkAdapter.GetConnectedProfileAsync();
            return profile?.WlanConnectionProfileDetails?.GetConnectedSsid() == ssid;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Disconnect() => _adapter?.Disconnect();
}
