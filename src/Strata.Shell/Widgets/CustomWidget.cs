using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Media;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Widgets;

/// <summary>
/// Widget importado (<see cref="WidgetModule"/>): líneas de texto con marcadores ({time}, {song}…)
/// que se rellenan con el reloj compartido, la música y la batería. Solo escucha lo que usa: un
/// widget sin {song} no se entera de la música, y solo marca segundos si alguna hora los muestra.
/// </summary>
public sealed partial class CustomWidget : Border, IWidgetContent
{
    private readonly WidgetModule _module;
    private readonly StackPanel _lines = new();
    private readonly List<(WidgetLine Line, TextBlock Block)> _blocks = new();
    private readonly bool _usesMedia, _usesBattery, _usesSeconds;
    private DispatcherTimer? _secondTimer;

    private static readonly char[] Separators = ['·', '|', '-', '–', '—', ',', '/', '•', ' '];

    [GeneratedRegex(@"\{(time|date|song|artist|album|battery|user)(?::([^}]*))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    public CustomWidget(WidgetConfig config, WidgetModule module)
    {
        _module = module;
        Child = _lines;
        if (module.Background is { } background)
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        if (module.Shadow)
            _lines.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };

        foreach (var line in module.Lines)
        {
            var block = new TextBlock
            {
                FontFamily = new FontFamily((line.Font is { Length: > 0 } font ? font + ", " : "") + "Segoe UI Variable Display, Segoe UI"),
                FontWeight = Weight(line.Weight),
                Opacity = line.Opacity,
                TextAlignment = line.Align?.ToLowerInvariant() switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center },
                TextWrapping = TextWrapping.NoWrap,
            };
            if (line.Color == "accent")
                block.SetResourceReference(TextBlock.ForegroundProperty, "StrataAccentBrush");
            else
                block.Foreground = line.Color is { } color ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) : Brushes.White;
            _blocks.Add((line, block));
            _lines.Children.Add(block);

            foreach (Match m in Token().Matches(line.Text))
            {
                string name = m.Groups[1].Value.ToLowerInvariant();
                _usesMedia |= name is "song" or "artist" or "album";
                _usesBattery |= name == "battery";
                _usesSeconds |= name is "time" or "date" && m.Groups[2].Value.Contains('s');
            }
        }
        Apply(config);

        Loaded += (_, _) =>
        {
            if (_usesSeconds)
            {
                _secondTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Refresh(), Dispatcher);
                _secondTimer.Start();
            }
            else
            {
                Taskbar.ClockTick += Refresh;
                Taskbar.StartSharedClock();
            }
            if (_usesMedia)
                MediaService.Changed += OnStatusChanged;
            if (_usesBattery)
                SystemStatus.Changed += OnStatusChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _secondTimer?.Stop();
            _secondTimer = null;
            Taskbar.ClockTick -= Refresh;
            MediaService.Changed -= OnStatusChanged;
            SystemStatus.Changed -= OnStatusChanged;
        };
    }

    /// <summary>Los avisos de la música llegan de otros hilos.</summary>
    private void OnStatusChanged() => Dispatcher.BeginInvoke(Refresh);

    public void Apply(WidgetConfig config)
    {
        CornerRadius = new CornerRadius(_module.CornerRadius * config.Scale);
        Padding = new Thickness(_module.Padding * config.Scale);
        foreach (var (line, block) in _blocks)
        {
            block.FontSize = line.Size * config.Scale;
            block.Margin = new Thickness(0, line.Spacing * config.Scale, 0, 0);
        }
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (line, block) in _blocks)
        {
            bool any = false, filled = false, empty = false;
            string text = Token().Replace(line.Text, m =>
            {
                any = true;
                string value = Value(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Success ? m.Groups[2].Value : null);
                filled |= value.Length > 0;
                empty |= value.Length == 0;
                return value;
            });
            // "{time} · {battery}" sin batería: sin el separador colgando al final.
            block.Text = empty ? text.Trim().Trim(Separators).Trim() : text;
            // Una línea de puros marcadores vacíos (sin música, sin batería) no ocupa lugar.
            block.Visibility = any && !filled ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    internal static string Value(string token, string? format)
    {
        var culture = CultureInfo.CurrentCulture;
        var now = DateTime.Now;
        switch (token)
        {
            case "time":
                return Format(now, format ?? culture.DateTimeFormat.ShortTimePattern, culture);
            case "date":
                if (format != null)
                    return Format(now, format, culture);
                string date = $"{now.ToString("dddd", culture)}, {now.ToString(culture.DateTimeFormat.MonthDayPattern, culture)}";
                return char.ToUpper(date[0], culture) + date[1..];
            case "song":
                return MediaService.HasSession ? MediaService.Title : "";
            case "artist":
                return MediaService.HasSession ? MediaService.Artist : "";
            case "album":
                return MediaService.HasSession ? MediaService.Album : "";
            case "battery":
                return SystemStatus.GetBattery() is { } battery ? battery.Percent + "%" : "";
            case "user":
                return Environment.UserName;
            default:
                return "";
        }
    }

    /// <summary>Un formato mal escrito en el módulo no rompe el widget: se muestra tal cual.</summary>
    private static string Format(DateTime now, string format, CultureInfo culture)
    {
        try
        {
            return now.ToString(format, culture);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private static FontWeight Weight(string? name) => name?.ToLowerInvariant() switch
    {
        "thin" => FontWeights.Thin,
        "light" => FontWeights.Light,
        "semibold" => FontWeights.SemiBold,
        "bold" => FontWeights.Bold,
        "black" => FontWeights.Black,
        _ => FontWeights.Normal,
    };

    /// <summary>Lo que se muestra si el módulo de un widget ya no está (se quitó del Hub).</summary>
    internal static FrameworkElement Missing(string? id) => new TextBlock
    {
        Text = Loc.F("Falta el widget importado \"{0}\"", id ?? "?"),
        Foreground = Brushes.White,
        FontSize = 14,
        Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black },
    };
}
