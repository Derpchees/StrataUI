using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Strata.Shell.Services;
using Strata.Shell.Services.Media;
using Strata.Shell.Services.Widgets;

namespace Strata.Shell.Widgets;

/// <summary>
/// Visualizador de música: barras que siguen lo que suena (<see cref="AudioSpectrum"/>), con el color
/// de acento, y la canción debajo si una app la anuncia. Solo se redibuja mientras hay sonido; en
/// silencio queda una línea quieta (y no gasta nada).
/// </summary>
public sealed class VisualizerWidget : FrameworkElement, IWidgetContent
{
    private readonly float[] _levels = new float[AudioSpectrum.Bands];
    private readonly float[] _shown = new float[AudioSpectrum.Bands];
    private double _scale = 1;
    private bool _animating;
    private FormattedText? _title;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new();
    private static readonly TimeSpan Playing = TimeSpan.FromMilliseconds(33), Idle = TimeSpan.FromMilliseconds(500);

    public VisualizerWidget(WidgetConfig config)
    {
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.45, Color = Colors.Black };
        Apply(config);
        Loaded += (_, _) =>
        {
            AudioSpectrum.Subscribe();
            _timer.Interval = Idle;
            _timer.Tick += OnFrame;
            _timer.Start();
            MediaService.Changed += UpdateTitle;
            UpdateTitle();
        };
        Unloaded += (_, _) =>
        {
            AudioSpectrum.Unsubscribe();
            _timer.Stop();
            _timer.Tick -= OnFrame;
            MediaService.Changed -= UpdateTitle;
        };
    }

    public void Apply(WidgetConfig config)
    {
        _scale = config.Scale;
        Width = AudioSpectrum.Bands * 9 * _scale;
        Height = 150 * _scale;
        UpdateTitle();
        InvalidateVisual();
    }

    private void UpdateTitle()
    {
        string text = !MediaService.HasSession || MediaService.Title.Length == 0 ? ""
            : MediaService.Artist.Length > 0 ? $"{MediaService.Title} · {MediaService.Artist}" : MediaService.Title;
        _title = text.Length == 0 ? null : new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            14 * _scale, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, Width),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        InvalidateVisual();
    }

    /// <summary>
    /// Unas 30 veces por segundo mientras suena algo (y un momento después, para que las barras
    /// bajen); en silencio solo se revisa cada medio segundo si volvió el sonido.
    /// </summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        bool sound = AudioSpectrum.HasSound;
        bool moving = sound || _shown.Any(v => v > 0.01f);
        if (!moving)
        {
            if (_animating)
            {
                _animating = false;
                _timer.Interval = Idle;
                Array.Clear(_shown);
                InvalidateVisual();
            }
            return;
        }
        if (!_animating)
        {
            _animating = true;
            _timer.Interval = Playing;
        }
        AudioSpectrum.Read(_levels);
        for (int i = 0; i < _shown.Length; i++)
            _shown[i] += ((sound ? _levels[i] : 0) - _shown[i]) * 0.35f;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double titleHeight = _title is null ? 0 : _title.Height + 8 * _scale;
        double area = Height - titleHeight;
        double slot = Width / _levels.Length;
        double barWidth = Math.Max(2, slot * 0.62);
        var accent = (TryFindResource("StrataAccentBrush") as SolidColorBrush)?.Color ?? Colors.White;
        var brush = new LinearGradientBrush(Color.FromArgb(0xFF, accent.R, accent.G, accent.B), Colors.White, new Point(0, 1), new Point(0, 0));
        brush.Freeze();
        for (int i = 0; i < _shown.Length; i++)
        {
            double height = Math.Max(3 * _scale, _shown[i] * area);
            double x = i * slot + (slot - barWidth) / 2;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, area - height, barWidth, height), barWidth / 2, barWidth / 2);
        }
        if (_title != null)
            dc.DrawText(_title, new Point(0, area + 8 * _scale));
    }
}
