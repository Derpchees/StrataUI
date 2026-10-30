using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Widgets;

/// <summary>
/// Espacio de los discos del equipo: una barra por unidad (las fijas, no las de red ni las USB) con lo
/// libre y el total. Se relee una vez por minuto con el reloj compartido (leer el espacio de una unidad
/// local es una llamada rápida).
/// </summary>
public sealed class DisksWidget : StackPanel, IWidgetContent
{
    private readonly StackPanel _rows = new();
    private double _scale = 1;
    private string _last = "";

    public DisksWidget(WidgetConfig config)
    {
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };
        Children.Add(_rows);
        Apply(config);
        Loaded += (_, _) =>
        {
            Taskbar.ClockTick += Update;
            Taskbar.StartSharedClock();
            Update();
        };
        Unloaded += (_, _) => Taskbar.ClockTick -= Update;
    }

    public void Apply(WidgetConfig config)
    {
        _scale = config.Scale;
        Width = 280 * _scale;
        _last = "";
        Update();
    }

    private void Update()
    {
        var drives = new List<(string Name, long Free, long Total)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady && drive.TotalSize > 0)
                {
                    string label = drive.VolumeLabel;
                    string letter = drive.Name.TrimEnd('\\');
                    drives.Add((label.Length > 0 ? $"{label} ({letter})" : letter, drive.AvailableFreeSpace, drive.TotalSize));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Una unidad que se desconecta mientras se lee: se omite.
            }
        }
        // Lo mismo que hace un minuto (lo normal): no se rearma nada.
        string key = string.Join("|", drives.Select(d => $"{d.Name}:{d.Free / (1L << 28)}:{d.Total}"));
        if (key == _last)
            return;
        _last = key;
        _rows.Children.Clear();
        if (drives.Count == 0)
        {
            _rows.Children.Add(Text(Loc.T("Sin discos"), 14, 1));
            return;
        }
        foreach (var (name, free, total) in drives)
            _rows.Children.Add(Row(name, free, total));
    }

    private FrameworkElement Row(string name, long free, long total)
    {
        double used = 1 - free / (double)total;
        // El detalle a la derecha y el nombre en lo que queda (con puntos suspensivos si no cabe).
        var header = new DockPanel();
        var detail = Text(Loc.F("{0} libres de {1}", Size(free), Size(total)), 12, 0.8);
        detail.Margin = new Thickness(8 * _scale, 0, 0, 0);
        detail.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(detail, Dock.Right);
        header.Children.Add(detail);
        header.Children.Add(Text(name, 14, 1));

        double height = 6 * _scale;
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        // Casi lleno (más del 90 %): en rojo, como Windows.
        if (used > 0.9)
            fill.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x4A, 0x4A));
        else
            fill.SetResourceReference(Border.BackgroundProperty, "StrataAccentBrush");
        var track = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 4 * _scale, 0, 0),
            Child = fill,
        };
        track.SizeChanged += (_, e) => fill.Width = Math.Max(height, e.NewSize.Width * used);
        return new StackPanel { Margin = new Thickness(0, 0, 0, 10 * _scale), Children = { header, track } };
    }

    private TextBlock Text(string text, double size, double opacity) => new()
    {
        Text = text,
        FontSize = size * _scale,
        Opacity = opacity,
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static string Size(long bytes)
    {
        double gb = bytes / 1073741824.0;
        return gb >= 1000
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0.0} TB", gb / 1024)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0} GB", gb);
    }
}
