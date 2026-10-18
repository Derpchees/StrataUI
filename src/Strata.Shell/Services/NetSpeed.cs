using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services;

/// <summary>
/// Velocidad de internet en vivo (lo que baja y sube por segundo) para el medidor de la barra y el
/// widget. Cuenta solo los adaptadores con puerta de enlace, los que salen a internet: así el
/// tráfico de una VPN (Tailscale) o de un adaptador virtual (Hyper-V) no se suma dos veces. Un solo
/// temporizador, que corre mientras alguien lo muestra.
/// </summary>
public static class NetSpeed
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static DispatcherTimer? _timer;
    private static int _users;
    private static bool _sampling;
    private static long _lastDown, _lastUp;
    private static readonly Stopwatch Clock = new();

    /// <summary>Bytes por segundo de bajada; null hasta la segunda lectura.</summary>
    public static double? DownBytesPerSecond { get; private set; }

    /// <summary>Bytes por segundo de subida; null hasta la segunda lectura.</summary>
    public static double? UpBytesPerSecond { get; private set; }

    /// <summary>Adaptadores que se están midiendo ("Wi-Fi", "Ethernet").</summary>
    public static string Adapters { get; private set; } = "";

    /// <summary>Hay una lectura nueva (en el hilo de la UI).</summary>
    public static event Action? Changed;

    public static void Subscribe()
    {
        if (_users++ > 0)
            return;
        DownBytesPerSecond = UpBytesPerSecond = null;
        Clock.Reset();
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
            long down = 0, up = 0;
            var names = new List<string>();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!Counts(adapter))
                        continue;
                    var stats = adapter.GetIPStatistics();
                    down += stats.BytesReceived;
                    up += stats.BytesSent;
                    names.Add(adapter.Name);
                }
            }
            catch (NetworkInformationException ex)
            {
                Log.Info($"No se pudo leer el tráfico de red: {ex.Message}");
            }
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _sampling = false;
                double seconds = Clock.Elapsed.TotalSeconds;
                bool first = !Clock.IsRunning || string.Join(", ", names) != Adapters;
                Clock.Restart();
                Adapters = string.Join(", ", names);
                // Al cambiar de adaptador (o en la primera lectura) no hay con qué comparar.
                if (!first && seconds > 0.2 && down >= _lastDown && up >= _lastUp)
                {
                    DownBytesPerSecond = (down - _lastDown) / seconds;
                    UpBytesPerSecond = (up - _lastUp) / seconds;
                }
                (_lastDown, _lastUp) = (down, up);
                Changed?.Invoke();
            });
        });
    }

    private static readonly string[] VirtualAdapters =
    [
        "ZeroTier", "Tailscale", "WireGuard", "OpenVPN", "TAP-Windows", "Wintun", "Hamachi", "Radmin VPN", "NordLynx",
        "vEthernet", "Hyper-V", "VMware", "VirtualBox", "Npcap",
    ];

    /// <summary>El adaptador sale a internet: conectado, no es de bucle, túnel ni VPN y tiene puerta de enlace.</summary>
    private static bool Counts(NetworkInterface adapter)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up
            || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;
        // VPN y adaptadores virtuales (ZeroTier pone puerta de enlace): su tráfico ya va por el físico.
        string name = adapter.Name + " " + adapter.Description;
        if (VirtualAdapters.Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase)))
            return false;
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)
                                                                       && !g.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    /// <summary>
    /// "850 KB/s", "12.4 MB/s" (en bytes, como los navegadores y Steam) o, con <see cref="StrataSettings.NetSpeedInBits"/>,
    /// "6.8 Mb/s" (en bits, como los planes de internet y las pruebas de velocidad).
    /// </summary>
    public static string Format(double? bytesPerSecond, bool compact = false, bool? bits = null)
    {
        bool inBits = bits ?? Settings.Current.NetSpeedInBits;
        string kUnit = inBits ? "kb/s" : "KB/s", mUnit = inBits ? "Mb/s" : "MB/s";
        if (bytesPerSecond is not { } value)
            return compact ? "—" : "— " + kUnit;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        double k = (inBits ? value * 8 / 1000 : value / 1024), m = inBits ? k / 1000 : k / 1024;
        if (m >= 1)
            return compact ? m.ToString("0.#", culture) + "M" : m.ToString(m >= 100 ? "0" : "0.0", culture) + " " + mUnit;
        return k.ToString("0", culture) + (compact ? "K" : " " + kUnit);
    }

    /// <summary>Lo que muestra el tooltip del medidor.</summary>
    public static string Description() =>
        Loc.F("Bajada: {0}", Format(DownBytesPerSecond)) + "\n" + Loc.F("Subida: {0}", Format(UpBytesPerSecond))
        + (Adapters.Length > 0 ? "\n" + Adapters : "");
}
