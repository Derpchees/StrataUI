using System.Windows;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Velocidad de internet (clic en su medidor de la barra): lo que pasa ahora y la prueba de
/// velocidad completa (<see cref="SpeedTest"/>), con el último resultado guardado.
/// </summary>
public partial class NetSpeedFlyout : FlyoutWindow
{
    /// <summary>Hay un panel abierto (si no, al terminar la prueba se avisa con una notificación).</summary>
    public static bool IsShowing { get; private set; }

    public NetSpeedFlyout()
    {
        InitializeComponent();
        NetSpeed.Subscribe();
        NetSpeed.Changed += UpdateLive;
        SpeedTest.Changed += UpdateTest;
        IsShowing = true;
        Closed += (_, _) =>
        {
            IsShowing = false;
            NetSpeed.Unsubscribe();
            NetSpeed.Changed -= UpdateLive;
            SpeedTest.Changed -= UpdateTest;
        };
        UpdateLive();
        UpdateTest();
    }

    private void UpdateLive() =>
        LiveText.Text = Loc.F("Ahora: ↓ {0}  ↑ {1}", NetSpeed.Format(NetSpeed.DownBytesPerSecond), NetSpeed.Format(NetSpeed.UpBytesPerSecond));

    private void UpdateTest()
    {
        var phase = SpeedTest.Current;
        bool running = SpeedTest.Running;
        var last = Settings.Current.LastSpeedTest;
        bool fresh = phase is not SpeedTest.Phase.Idle and not SpeedTest.Phase.Failed;

        // Durante la prueba, lo medido y lo que se está midiendo; si no, el último resultado.
        double? down = fresh ? SpeedTest.DownMbps : last?.DownMbps;
        double? up = fresh ? SpeedTest.UpMbps : last?.UpMbps;
        if (phase == SpeedTest.Phase.Download)
            down = SpeedTest.LiveValue;
        if (phase == SpeedTest.Phase.Upload)
            up = SpeedTest.LiveValue;
        DownText.Text = down is null ? "—" : SpeedTest.Mbps(down);
        UpText.Text = up is null ? "—" : SpeedTest.Mbps(up);
        double? ping = fresh ? SpeedTest.PingMs ?? (phase == SpeedTest.Phase.Ping ? SpeedTest.LiveValue : null) : last?.PingMs;
        double? jitter = fresh ? SpeedTest.JitterMs : last?.JitterMs;
        PingText.Text = ping is null ? "" : Loc.F("Ping {0} · variación {1}", SpeedTest.Ms(ping), SpeedTest.Ms(jitter));

        TestProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        TestProgress.Value = SpeedTest.Progress;
        StatusText.Text = phase switch
        {
            SpeedTest.Phase.Ping => Loc.T("Midiendo el ping…"),
            SpeedTest.Phase.Download => Loc.T("Midiendo la bajada…"),
            SpeedTest.Phase.Upload => Loc.T("Midiendo la subida…"),
            SpeedTest.Phase.Failed => Loc.T("No se pudo completar la prueba. Revisa la conexión."),
            _ when last != null => Loc.F("Última prueba: {0:g}", last.When) + (last.Server is { } server ? " · " + Loc.F("servidor {0}", server) : ""),
            _ => Loc.T("Mide la bajada, la subida y el ping de tu conexión."),
        };
        TestButton.Content = running ? Loc.T("Cancelar") : Loc.T("Probar velocidad");
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (SpeedTest.Running)
            SpeedTest.Cancel();
        else
            SpeedTest.Start();
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e)
    {
        CloseFlyout();
        ManagedShell.Common.Helpers.ShellHelper.StartTaskManager();
    }
}
