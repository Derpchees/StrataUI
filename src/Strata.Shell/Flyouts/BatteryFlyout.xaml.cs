using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Batería: nivel, si carga o cuánto queda, modo de energía de Windows, ahorro de batería y salud.
/// </summary>
public partial class BatteryFlyout : FlyoutWindow
{
    public BatteryFlyout()
    {
        InitializeComponent();
        SystemStatus.Changed += Refresh;
        Closed += (_, _) => SystemStatus.Changed -= Refresh;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var battery = SystemStatus.GetBattery();
        if (battery is null)
        {
            StateText.Text = Loc.T("Este equipo no tiene batería");
            return;
        }
        BigGlyph.Text = battery.Glyph.ToString();
        PercentText.Text = $"{battery.Percent} %";
        Level.Value = battery.Percent;
        StateText.Text = battery.Charging ? Loc.T("Cargando")
            : battery.PluggedIn ? Loc.T("Conectada, no está cargando")
            : Loc.T("Con batería");

        var report = ReadReport();
        TimeText.Text = TimeLine(battery, report);
        TimeText.Visibility = TimeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HealthText.Text = report is { Full: > 0, Design: > 0 } r
            ? Loc.F("Capacidad: {0}% de la original", (int)Math.Round(100.0 * r.Full / r.Design))
            : "";
        HealthText.Visibility = HealthText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        SaverText.Text = SaverLine();
        UpdateMode();
    }

    private sealed record Report(int Remaining, int Full, int Design, int Rate);

    /// <summary>Capacidades (mWh) y ritmo de carga o descarga (mW) de todas las baterías juntas.</summary>
    private static Report? ReadReport()
    {
        try
        {
            var report = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            return new Report(report.RemainingCapacityInMilliwattHours ?? 0, report.FullChargeCapacityInMilliwattHours ?? 0,
                              report.DesignCapacityInMilliwattHours ?? 0, report.ChargeRateInMilliwatts ?? 0);
        }
        catch (Exception ex)
        {
            Log.Info($"No se pudo leer el informe de batería: {ex.Message}");
            return null;
        }
    }

    /// <summary>"Quedan 2 h 10 min" con batería; "Carga completa en 45 min" cargando.</summary>
    private static string TimeLine(SystemStatus.BatteryInfo battery, Report? report)
    {
        if (battery.Charging)
        {
            // Con un ritmo de carga muy bajo (cargador lento, equipo exigido) la estimación no sirve.
            if (report is { Rate: > 0 } r && r.Full > r.Remaining
                && TimeSpan.FromHours((double)(r.Full - r.Remaining) / r.Rate) is { TotalHours: < 24 } toFull)
                return Loc.F("Carga completa en {0}", Taskbar.Duration(toFull));
            return "";
        }
        if (battery.PluggedIn)
            return "";
        if (battery.Remaining is { } left)
            return Loc.F("Quedan {0}", Taskbar.Duration(left));
        if (report is { Rate: < 0 } d)
            return Loc.F("Quedan {0}", Taskbar.Duration(TimeSpan.FromHours((double)d.Remaining / -d.Rate)));
        return "";
    }

    private static string SaverLine()
    {
        try
        {
            return Windows.System.Power.PowerManager.EnergySaverStatus switch
            {
                Windows.System.Power.EnergySaverStatus.On => Loc.T("Ahorro de batería activado"),
                Windows.System.Power.EnergySaverStatus.Off => Loc.T("Ahorro de batería desactivado"),
                _ => "",
            };
        }
        catch (Exception)
        {
            return "";
        }
    }

    // ── Modo de energía ─────────────────────────────────────────────────────

    private void UpdateMode()
    {
        Guid current = Guid.Empty;
        bool known = PowerGetEffectiveOverlayScheme(out current) == 0;
        foreach (var button in new[] { EfficiencyMode, BalancedMode, PerformanceMode })
            button.IsChecked = known && Guid.Parse((string)button.Tag) == current;
        ModeGrid.IsEnabled = known;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag })
            return;
        uint result = PowerSetActiveOverlayScheme(Guid.Parse(tag));
        if (result != 0)
            Log.Error($"No se pudo cambiar el modo de energía (error {result})");
        else
            Log.Info($"Modo de energía: {tag}");
        UpdateMode();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        CloseFlyout();
        Process.Start(new ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true });
    }

    // Modo de energía (el control deslizante de Windows 10/11): esquemas superpuestos al plan activo.
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid scheme);
}
