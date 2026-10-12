using System.Net.NetworkInformation;
using Forms = System.Windows.Forms;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Services;

/// <summary>Glifos (Segoe Fluent Icons) que resumen red y batería.</summary>
public static class SystemStatus
{
    private const char WifiGlyph = '';
    private const char EthernetGlyph = '';
    private const int BatteryZeroGlyph = 0xE850; // Battery0..Battery9 son consecutivos
    private const char BatteryFullGlyph = '';

    public record NetworkInfo(char Glyph, bool Connected, string Description);

    public record BatteryInfo(char Glyph, int Percent, bool Charging);

    public static NetworkInfo GetNetwork()
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

    /// <summary>Devuelve null en equipos sin batería.</summary>
    public static BatteryInfo? GetBattery()
    {
        var power = Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
            || power.BatteryChargeStatus == Forms.BatteryChargeStatus.Unknown)
            return null;

        int percent = (int)Math.Round(power.BatteryLifePercent * 100);
        int step = Math.Clamp(percent / 10, 0, 10);
        char glyph = step == 10 ? BatteryFullGlyph : (char)(BatteryZeroGlyph + step);
        return new BatteryInfo(glyph, percent, power.PowerLineStatus == Forms.PowerLineStatus.Online);
    }
}
