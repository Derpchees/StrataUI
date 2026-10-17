using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Widgets;

/// <summary>
/// Medidor grande para el escritorio (CPU, GPU, FPS o velocidad de internet): el número grande y
/// debajo el nombre y la temperatura (en el de FPS, el juego que se mide; en el de internet, la subida). Blanco con sombra, como el reloj. Los datos
/// son los de los medidores de la barra (<see cref="SystemMonitor"/> y las lecturas avanzadas de
/// <see cref="Sensors"/>).
/// </summary>
public sealed class MeterWidget : StackPanel, IWidgetContent
{
    private readonly WidgetKind _kind;
    private readonly Run _number = new("—");
    private readonly Run _unit = new();
    private readonly TextBlock _label = new() { HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9 };
    // Hasta dos líneas: los avisos ("Activa las lecturas avanzadas") no caben en una.
    private readonly TextBlock _detail = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public MeterWidget(WidgetConfig config)
    {
        _kind = config.Kind;
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        var number = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
        number.Inlines.Add(_number);
        number.Inlines.Add(_unit);
        _unit.FontWeight = FontWeights.Normal;
        Children.Add(number);
        Children.Add(_label);
        Children.Add(_detail);
        foreach (var text in new[] { number, _label, _detail })
        {
            text.FontFamily = font;
            text.Foreground = Brushes.White;
        }
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };
        _label.Text = _kind switch { WidgetKind.Gpu => "GPU", WidgetKind.Fps => "FPS", WidgetKind.NetSpeed => "↓ " + Loc.T("Internet"), _ => "CPU" };

        Apply(config);
        Loaded += (_, _) =>
        {
            if (_kind == WidgetKind.NetSpeed)
            {
                NetSpeed.Subscribe();
                NetSpeed.Changed += Update;
                SpeedTest.Changed += Update;
                Update();
                return;
            }
            SystemMonitor.Subscribe();
            SystemMonitor.Changed += Update;
            Sensors.Changed += Update;
            // Temperaturas y FPS: con el permiso ya dado, las lecturas empiezan solas (sin preguntar).
            Sensors.StartQuietly();
            Update();
        };
        Unloaded += (_, _) =>
        {
            if (_kind == WidgetKind.NetSpeed)
            {
                NetSpeed.Unsubscribe();
                NetSpeed.Changed -= Update;
                SpeedTest.Changed -= Update;
                return;
            }
            SystemMonitor.Unsubscribe();
            SystemMonitor.Changed -= Update;
            Sensors.Changed -= Update;
        };
    }

    public void Apply(WidgetConfig config)
    {
        // Ancho fijo: el widget no cambia de tamaño (ni se corre) cuando el número pasa de 9 a 10.
        Width = (_kind == WidgetKind.NetSpeed ? 200 : 140) * config.Scale;
        Margin = new Thickness(8 * config.Scale);
        _number.FontSize = 54 * config.Scale;
        _unit.FontSize = 22 * config.Scale;
        _label.FontSize = 14 * config.Scale;
        _label.Margin = new Thickness(0, -8 * config.Scale, 0, 0);
        _detail.FontSize = 12 * config.Scale;
        _detail.MaxHeight = _detail.FontSize * 2.8;
    }

    /// <summary>
    /// Mientras corre la prueba de velocidad (y medio minuto después), el widget la muestra en vez
    /// de la velocidad de ahora: lo que va midiendo y, al final, el resultado.
    /// </summary>
    private bool UpdateSpeedTest()
    {
        var phase = SpeedTest.Current;
        var last = Settings.Current.LastSpeedTest;
        if (SpeedTest.Running)
        {
            bool ping = phase == SpeedTest.Phase.Ping;
            _number.Text = Math.Round(SpeedTest.LiveValue).ToString();
            _unit.Text = ping ? " ms" : " Mbps";
            _label.Text = phase switch
            {
                SpeedTest.Phase.Ping => Loc.T("Prueba: ping"),
                SpeedTest.Phase.Download => Loc.T("Prueba: bajada"),
                _ => Loc.T("Prueba: subida"),
            };
            _detail.Text = $"{Math.Round(SpeedTest.Progress * 100)} %";
            return true;
        }
        if (phase == SpeedTest.Phase.Done && last != null && DateTime.Now - last.When < TimeSpan.FromSeconds(30))
        {
            _number.Text = Math.Round(last.DownMbps).ToString();
            _unit.Text = " Mbps";
            _label.Text = "↓ " + Loc.T("Prueba de velocidad");
            _detail.Text = "↑ " + SpeedTest.Mbps(last.UpMbps) + " · " + SpeedTest.Ms(last.PingMs);
            return true;
        }
        return false;
    }

    private void Update()
    {
        if (_kind == WidgetKind.NetSpeed && UpdateSpeedTest())
            return;
        if (_kind == WidgetKind.NetSpeed)
        {
            // "12.4" grande con "MB/s" al lado; debajo, la subida.
            _label.Text = "↓ " + Loc.T("Internet");
            string down = NetSpeed.Format(NetSpeed.DownBytesPerSecond);
            int space = down.LastIndexOf(' ');
            _number.Text = space > 0 ? down[..space] : down;
            _unit.Text = space > 0 ? " " + down[(space + 1)..] : "";
            _detail.Text = "↑ " + NetSpeed.Format(NetSpeed.UpBytesPerSecond);
            return;
        }
        if (_kind == WidgetKind.Fps)
        {
            var target = Sensors.TargetProcess(Settings.Current.FpsScreen);
            var fps = target is { } pid ? Sensors.FpsOf(pid) : null;
            _number.Text = Sensors.Starting ? "…" : fps?.Shown.ToString() ?? "—";
            _unit.Text = "";
            _detail.Text = !Sensors.Running ? Loc.T("Activa las lecturas avanzadas")
                : fps is null ? Loc.T("Sin juego en pantalla")
                : Sensors.ProcessName(target!.Value);
        }
        else
        {
            bool cpu = _kind == WidgetKind.Cpu;
            double? percent = cpu ? SystemMonitor.CpuPercent : SystemMonitor.GpuPercent;
            double? temperature = cpu ? Sensors.CpuTemperature : Sensors.GpuTemperature;
            _number.Text = percent is { } p ? Math.Round(p).ToString() : "—";
            _unit.Text = percent is null ? "" : "%";
            // Como en la barra: sin lectura (lecturas avanzadas apagadas o sin sensor), "-- °C".
            _detail.Text = temperature is { } t ? $"{t:0} °C" : "-- °C";
        }
    }
}
