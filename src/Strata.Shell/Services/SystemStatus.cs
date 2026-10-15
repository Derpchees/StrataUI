using System.Net.NetworkInformation;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services;

/// <summary>
/// Glifos (Segoe Fluent Icons) que resumen red y batería. Se calculan una vez para todas las
/// barras y se recalculan cuando Windows avisa de un cambio (más una revisión lenta de respaldo),
/// en lugar de recorrer los adaptadores de red cada pocos segundos en cada barra.
/// </summary>
public static class SystemStatus
{
    private const char WifiGlyph = '';
    private const char EthernetGlyph = '';
    // Batería de Fluent: Battery0..Battery10 y BatteryCharging0..10 son consecutivos.
    private const int BatteryZeroGlyph = 0xEBA0;
    private const int BatteryChargingZeroGlyph = 0xEBAB;

    public record NetworkInfo(char Glyph, bool Connected, string Description);

    /// <param name="Charging">La batería está cargando.</param>
    /// <param name="PluggedIn">Conectado a la corriente (puede estar llena y sin cargar).</param>
    /// <param name="Remaining">Tiempo que queda con batería, si Windows lo sabe.</param>
    public record BatteryInfo(char Glyph, int Percent, bool Charging, bool PluggedIn, TimeSpan? Remaining);

    private static NetworkInfo? _network;
    private static BatteryInfo? _battery;
    private static bool _batteryRead;
    private static System.Windows.Threading.DispatcherTimer? _fallback;

    /// <summary>Cambió la red o la batería (se invoca en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>Empieza a escuchar los avisos de Windows (al iniciar Strata UI).</summary>
    public static void Initialize()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => Invalidate();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Invalidate();
        SystemEvents.PowerModeChanged += (_, _) => Invalidate();
        // Respaldo: el porcentaje de batería baja sin que Windows avise de cada punto.
        _fallback = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _fallback.Tick += (_, _) => Invalidate();
        _fallback.Start();
    }

    public static void Shutdown() => _fallback?.Stop();

    private static void Invalidate()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        dispatcher.BeginInvoke(() =>
        {
            var network = ReadNetwork();
            var battery = ReadBattery();
            bool changed = network != _network || battery != _battery;
            _network = network;
            _battery = battery;
            _batteryRead = true;
            if (changed)
                Changed?.Invoke();
        });
    }

    public static NetworkInfo GetNetwork() => _network ??= ReadNetwork();

    /// <summary>Devuelve null en equipos sin batería.</summary>
    public static BatteryInfo? GetBattery()
    {
        if (!_batteryRead)
        {
            _battery = ReadBattery();
            _batteryRead = true;
        }
        return _battery;
    }

    private static NetworkInfo ReadNetwork()
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();

        if (!NetworkInterface.GetIsNetworkAvailable() || up.Count == 0)
            return new NetworkInfo(WifiGlyph, false, Loc.T("Sin conexión"));

        var wifi = up.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        return wifi != null
            ? new NetworkInfo(WifiGlyph, true, $"Wi-Fi: {wifi.Name}")
            : new NetworkInfo(EthernetGlyph, true, $"Ethernet: {up[0].Name}");
    }

    private static BatteryInfo? ReadBattery()
    {
        var power = Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
            || power.BatteryChargeStatus == Forms.BatteryChargeStatus.Unknown)
            return null;

        int percent = (int)Math.Round(power.BatteryLifePercent * 100);
        int step = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        bool pluggedIn = power.PowerLineStatus == Forms.PowerLineStatus.Online;
        bool charging = power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.Charging) || (pluggedIn && percent < 100);
        char glyph = (char)((charging ? BatteryChargingZeroGlyph : BatteryZeroGlyph) + step);
        TimeSpan? remaining = !pluggedIn && power.BatteryLifeRemaining > 0 ? TimeSpan.FromSeconds(power.BatteryLifeRemaining) : null;
        return new BatteryInfo(glyph, percent, charging, pluggedIn, remaining);
    }
}
