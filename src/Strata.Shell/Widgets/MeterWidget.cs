using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Widgets;

/// <summary>
/// Medidor grande para el escritorio (CPU, GPU o FPS): un arco de 270° como un velocímetro, del color
/// de acento y con un brillo suave, que se mueve con suavidad; el número grande en el centro y debajo
/// el nombre y la temperatura (o, en el de FPS, el juego que se mide). Blanco con sombra, como el
/// reloj. Los datos son los de los medidores de la barra (<see cref="SystemMonitor"/> y las lecturas
/// avanzadas de <see cref="Sensors"/>).
/// </summary>
public sealed class MeterWidget : Grid, IWidgetContent
{
    private const double StartAngle = 135, Sweep = 270;

    private readonly WidgetKind _kind;
    private readonly Path _track = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Path _value = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Run _number = new("—");
    private readonly Run _unit = new();
    private readonly TextBlock _label = new() { HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9 };
    // Hasta dos líneas: los avisos ("Activa las lecturas avanzadas") no caben en una dentro del arco.
    private readonly TextBlock _detail = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private double _size = 160;
    /// <summary>Brillo del arco, del color de acento (se toma en cada lectura: el acento puede cambiar).</summary>
    private readonly DropShadowEffect _glow = new() { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 };

    /// <summary>Lo lleno del arco (0 a 1); se anima.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(double), typeof(MeterWidget), new PropertyMetadata(0.0, (d, _) => ((MeterWidget)d).Draw()));

    public double Fill
    {
        get => (double)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public MeterWidget(WidgetConfig config)
    {
        _kind = config.Kind;
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _track.Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        _value.SetResourceReference(Shape.StrokeProperty, "StrataAccentBrush");
        _value.Effect = _glow;
        Children.Add(_track);
        Children.Add(_value);

        var number = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
        number.Inlines.Add(_number);
        number.Inlines.Add(_unit);
        _unit.FontWeight = FontWeights.Normal;
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(number);
        center.Children.Add(_label);
        center.Children.Add(_detail);
        Children.Add(center);
        foreach (var text in new[] { number, _label, _detail })
        {
            text.FontFamily = font;
            text.Foreground = Brushes.White;
        }
        center.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };
        _label.Text = _kind switch { WidgetKind.Gpu => "GPU", WidgetKind.Fps => "FPS", _ => "CPU" };

        Apply(config);
        Loaded += (_, _) =>
        {
            SystemMonitor.Subscribe();
            SystemMonitor.Changed += Update;
            Sensors.Changed += Update;
            // Temperaturas y FPS: con el permiso ya dado, las lecturas empiezan solas (sin preguntar).
            Sensors.StartQuietly();
            Update();
        };
        Unloaded += (_, _) =>
        {
            SystemMonitor.Unsubscribe();
            SystemMonitor.Changed -= Update;
            Sensors.Changed -= Update;
        };
    }

    public void Apply(WidgetConfig config)
    {
        _size = 160 * config.Scale;
        Width = Height = _size;
        _track.StrokeThickness = _value.StrokeThickness = 12 * config.Scale;
        _number.FontSize = 46 * config.Scale;
        _unit.FontSize = 20 * config.Scale;
        _label.FontSize = 14 * config.Scale;
        _label.Margin = new Thickness(0, -6 * config.Scale, 0, 0);
        _detail.FontSize = 12 * config.Scale;
        _detail.MaxWidth = _size * 0.62;
        _detail.MaxHeight = _detail.FontSize * 2.8;
        Draw();
    }

    private void Update()
    {
        if (TryFindResource("StrataAccentBrush") is SolidColorBrush accent && _glow.Color != accent.Color)
            _glow.Color = accent.Color;
        double? fill;
        if (_kind == WidgetKind.Fps)
        {
            var target = Sensors.TargetProcess(Settings.Current.FpsScreen);
            var fps = target is { } pid ? Sensors.FpsOf(pid) : null;
            _number.Text = Sensors.Starting ? "…" : fps?.Shown.ToString() ?? "—";
            _unit.Text = "";
            _detail.Text = !Sensors.Running ? Loc.T("Activa las lecturas avanzadas")
                : fps is null ? Loc.T("Sin juego en pantalla")
                : Sensors.ProcessName(target!.Value);
            fill = fps is { } f ? f.Shown / (double)RefreshRate() : 0;
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
            fill = percent / 100;
        }
        Animate(Math.Clamp(fill ?? 0, 0, 1));
    }

    private void Animate(double to)
    {
        if (Settings.Current.PanelAnimation == PanelAnimation.None)
        {
            BeginAnimation(FillProperty, null);
            Fill = to;
            return;
        }
        BeginAnimation(FillProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>El arco de fondo entero y el de valor hasta donde llega <see cref="Fill"/>.</summary>
    private void Draw()
    {
        double inset = _track.StrokeThickness / 2 + 2;
        double radius = _size / 2 - inset;
        var center = new Point(_size / 2, _size / 2);
        _track.Data = Arc(center, radius, Sweep);
        _value.Data = Fill > 0.002 ? Arc(center, radius, Sweep * Fill) : null;
    }

    private static Geometry Arc(Point center, double radius, double sweep)
    {
        Point At(double degrees)
        {
            double rad = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = At(StartAngle), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(StartAngle + sweep), new Size(radius, radius), 0, sweep > 180,
                                           SweepDirection.Clockwise, true));
        var geometry = new PathGeometry { Figures = { figure } };
        geometry.Freeze();
        return geometry;
    }

    // ── Frecuencia del monitor (el arco del de FPS llega al máximo con ella) ─

    private static int RefreshRate()
    {
        var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettings(Settings.Current.FpsScreen, -1, ref mode) && mode.DisplayFrequency > 1
            ? mode.DisplayFrequency
            : 60;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode devMode);
}
