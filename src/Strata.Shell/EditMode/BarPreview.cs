using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell.WindowsTasks;
using Strata.Shell.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Media = Strata.Shell.Services.Media.MediaService;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Strata.Shell.EditMode;

/// <summary>
/// Una barra del borrador dibujada como la real (tema, grosor, íconos de las apps y de la bandeja,
/// hora), sin datos en vivo. Cada elemento se arrastra a otra posición, barra o monitor; clic
/// derecho en él o en la barra para las demás opciones.
/// </summary>
internal sealed class BarPreview
{
    private readonly string _device;
    private readonly BarConfig _bar;
    private readonly bool _vertical;
    private readonly double _thickness;
    private readonly StackPanel _startZone, _centerZone, _endZone, _apps;
    private readonly Dictionary<BarItem, FrameworkElement> _chips = new();

    public FrameworkElement Root { get; }
    public TaskbarPosition Edge => _bar.Edge;

    public BarPreview(string device, BarConfig bar, ScreenLayout layout)
    {
        _device = device;
        _bar = bar;
        _vertical = BarLayout.IsVertical(bar.Edge);
        _thickness = Taskbar.ReservedSize(_vertical, device);
        var orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal;
        _startZone = new StackPanel { Orientation = orientation };
        _centerZone = new StackPanel { Orientation = orientation };
        _endZone = new StackPanel { Orientation = orientation };
        _apps = new StackPanel { Orientation = orientation };
        BarZonesPanel.SetZone(_startZone, BarPanelZone.Start);
        BarZonesPanel.SetZone(_centerZone, BarPanelZone.Center);
        BarZonesPanel.SetZone(_endZone, BarPanelZone.End);
        BarZonesPanel.SetZone(_apps, BarPanelZone.Apps);

        bool left = WindowsTaskbarPrefs.Alignment(Settings.Current.For(device)) == TaskbarAlignment.Left;
        if (_vertical)
            _apps.VerticalAlignment = left ? VerticalAlignment.Top : VerticalAlignment.Center;
        else
            _apps.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        var zones = new BarZonesPanel { Orientation = orientation, AppsCentered = !left };
        zones.Children.Add(_startZone);
        zones.Children.Add(_apps);
        zones.Children.Add(_centerZone);
        zones.Children.Add(_endZone);

        foreach (var item in bar.Items)
        {
            var chip = Chip(item);
            _chips[item] = chip;
            ZonePanelOf(item).Children.Add(chip);
        }
        UpdateZoneVisibility(false);

        // La barra con el aspecto de su monitor: color propio, sin fondo o el del tema (sobre un velo
        // que imita el desenfoque, que aquí no hay).
        var theme = ThemeManager.ThemeFor(device);
        double m = theme.Floating ? ThemeCatalog.FloatingMargin : 0;
        var margin = bar.Edge switch
        {
            TaskbarPosition.Top => new Thickness(m, m, m, 0),
            TaskbarPosition.Left => new Thickness(m, m, 0, m),
            TaskbarPosition.Right => new Thickness(0, m, m, m),
            _ => new Thickness(m, 0, m, m),
        };
        double inner = _thickness - (_vertical ? margin.Left + margin.Right : margin.Top + margin.Bottom);
        var radius = new CornerRadius(theme.Floating ? inner / 2 : 0);
        var surface = new Border
        {
            Margin = margin,
            CornerRadius = radius,
            BorderThickness = theme.Floating ? new Thickness(1) : bar.Edge switch
            {
                TaskbarPosition.Top => new Thickness(0, 0, 0, 1),
                TaskbarPosition.Left => new Thickness(0, 0, 1, 0),
                TaskbarPosition.Right => new Thickness(1, 0, 0, 0),
                _ => new Thickness(0, 1, 0, 0),
            },
            Child = zones,
        };
        surface.SetResourceReference(Border.BorderBrushProperty, "StrataTaskbarEdgeBrush");
        if (ThemeManager.ClearBarFor(device))
            surface.Background = Brushes.Transparent;
        else if (ThemeManager.BarColorFor(device) is { } color)
            surface.Background = new SolidColorBrush(color);
        else
            surface.SetResourceReference(Border.BackgroundProperty, "StrataTaskbarBackgroundBrush");
        var veil = new Border
        {
            Margin = margin,
            CornerRadius = radius,
            Background = new SolidColorBrush(ThemeManager.IsLightFor(device) ? Color.FromArgb(150, 240, 240, 240) : Color.FromArgb(150, 24, 24, 24)),
            Visibility = ThemeManager.ClearBarFor(device) ? Visibility.Collapsed : Visibility.Visible,
        };
        var root = new Grid { Background = Brushes.Transparent };
        if (_vertical)
            root.Width = _thickness;
        else
            root.Height = _thickness;
        root.Children.Add(veil);
        root.Children.Add(surface);
        root.ContextMenu = BarMenu(layout);
        Root = root;
    }

    private StackPanel ZonePanelOf(BarItem item) => !BarLayout.Zoned(item) ? _apps : BarLayout.ZoneOf(_bar, item) switch
    {
        BarZone.Start => _startZone,
        BarZone.Center => _centerZone,
        _ => _endZone,
    };

    /// <summary>Mientras se arrastra, las zonas vacías se ven como un lugar donde soltar.</summary>
    public void ShowDropZones(bool show) => UpdateZoneVisibility(show);

    private void UpdateZoneVisibility(bool dropping)
    {
        foreach (var panel in new[] { _startZone, _centerZone, _endZone })
        {
            bool empty = panel.Children.Count == 0;
            panel.Visibility = empty && !dropping ? Visibility.Collapsed : Visibility.Visible;
            if (empty && dropping)
            {
                panel.MinWidth = _vertical ? 0 : 56;
                panel.MinHeight = _vertical ? 56 : 0;
                panel.Margin = new Thickness(4);
                panel.SetResourceReference(Panel.BackgroundProperty, "StrataHoverBrush");
            }
            else
            {
                panel.ClearValue(FrameworkElement.MinWidthProperty);
                panel.ClearValue(FrameworkElement.MinHeightProperty);
                panel.ClearValue(FrameworkElement.MarginProperty);
                panel.ClearValue(Panel.BackgroundProperty);
            }
        }
        _apps.Visibility = _apps.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Soltar ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Dónde caería un elemento con el puntero en ese punto de la pantalla (null si no está sobre
    /// esta barra): antes del primero de su zona que quede después del puntero.
    /// </summary>
    public BarTarget? TargetAt(Point screen, BarItem item, Visual relativeTo)
    {
        var bar = ScreenRect(Root, 0);
        if (!bar.Contains(screen))
            return null;
        if (!BarLayout.Zoned(item))
            return new BarTarget(Edge, null, BarZone.End, Local(_apps.IsVisible ? _apps : Root, relativeTo));

        BarZone? zone = null;
        foreach (var (candidate, panel) in new[] { (BarZone.Start, _startZone), (BarZone.Center, _centerZone), (BarZone.End, _endZone) })
            if (ScreenRect(panel, 12).Contains(screen))
                zone = candidate;
        // Fuera de las zonas: por tercios de la barra.
        zone ??= Along(screen, bar) switch
        {
            < 1 / 3.0 => BarZone.Start,
            < 2 / 3.0 => BarZone.Center,
            _ => BarZone.End,
        };
        var target = zone.Value;

        FrameworkElement? before = null, after = null;
        int index = -1, last = -1;
        for (int i = 0; i < _bar.Items.Count; i++)
        {
            var other = _bar.Items[i];
            if (other == item || BarLayout.ZoneOf(_bar, other) != target || !_chips.TryGetValue(other, out var chip) || !chip.IsVisible)
                continue;
            var rect = ScreenRect(chip, 0);
            double center = _vertical ? rect.Top + rect.Height / 2 : rect.Left + rect.Width / 2;
            if ((_vertical ? screen.Y : screen.X) < center)
            {
                index = i;
                before = chip;
                break;
            }
            last = i;
            after = chip;
        }
        if (index < 0)
            index = last >= 0 ? last + 1 : _bar.Items.Count;

        // Una línea donde caería (o la zona entera si está vacía).
        Rect marker;
        if ((before ?? after) is { } reference)
        {
            var r = Local(reference, relativeTo);
            bool leading = before != null;
            marker = _vertical
                ? new Rect(r.Left, (leading ? r.Top : r.Bottom) - 1.5, r.Width, 3)
                : new Rect((leading ? r.Left : r.Right) - 1.5, r.Top, 3, r.Height);
        }
        else
        {
            marker = Local(target switch { BarZone.Start => _startZone, BarZone.Center => _centerZone, _ => _endZone }, relativeTo);
        }
        return new BarTarget(Edge, index, target, marker);
    }

    private double Along(Point screen, Rect bar) =>
        _vertical ? (screen.Y - bar.Top) / Math.Max(1, bar.Height) : (screen.X - bar.Left) / Math.Max(1, bar.Width);

    internal static Rect ScreenRect(FrameworkElement element, double inflate)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null)
            return Rect.Empty;
        var a = element.PointToScreen(new Point(0, 0));
        var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        var rect = new Rect(a, b);
        rect.Inflate(inflate, inflate);
        return rect;
    }

    internal static Rect Local(FrameworkElement element, Visual relativeTo)
    {
        if (!element.IsVisible)
            return Rect.Empty;
        try
        {
            return element.TransformToVisual(relativeTo).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty; // en otra ventana
        }
    }

    // ── Elementos ───────────────────────────────────────────────────────────

    private FrameworkElement Chip(BarItem item)
    {
        var chip = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Child = ItemVisual(item),
            ToolTip = BarLayout.Name(item),
            Cursor = Cursors.SizeAll,
        };
        if (_vertical)
            chip.Margin = new Thickness(4, 1, 4, 1);
        else
            chip.Margin = new Thickness(1, 4, 1, 4);
        chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "StrataHoverBrush");
        chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        chip.MouseLeftButtonDown += (_, e) => EditDrag.Press(chip, new ItemPayload(item, _device), e);
        chip.ContextMenu = ItemMenu(item);
        return chip;
    }

    private static Style IconStyle => (Style)Application.Current.FindResource("IconText");

    private static TextBlock Glyph(int code, double size = 15) => new()
    {
        Style = IconStyle,
        FontSize = size,
        Text = ((char)code).ToString(),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static TextBlock Text(string text, double size, bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "StrataForegroundBrush");
        return block;
    }

    /// <summary>Celda del tamaño de un botón de estado de la barra (30 de ancho; en vertical, 36×30).</summary>
    private FrameworkElement Cell(UIElement content, double length = 30) => new Border
    {
        Width = _vertical ? Math.Max(36, length) : length,
        Height = _vertical ? 30 : _thickness - 8,
        Child = content,
    };

    private FrameworkElement ItemVisual(BarItem item)
    {
        switch (item)
        {
            case BarItem.Start:
                return Cell(StartIcons.Create(Settings.Current.StartIcon), _vertical ? 36 : 44);
            case BarItem.Apps:
                return AppsVisual();
            case BarItem.Tray:
                return TrayVisual();
            case BarItem.Media:
            {
                var cover = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Child = Glyph(0xEC4F, 13) };
                cover.SetResourceReference(Border.BackgroundProperty, "StrataSurfaceBrush");
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                row.Children.Add(cover);
                if (!_vertical)
                {
                    var title = Text(Media.HasSession && Media.Title.Length > 0 ? Media.Title : BarLayout.Name(item), 12, bold: true);
                    title.MaxWidth = 150;
                    title.Margin = new Thickness(8, 0, 2, 0);
                    title.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(title);
                }
                return new Border { Height = _vertical ? 40 : _thickness - 8, Child = row };
            }
            case BarItem.Network:
                return Cell(Glyph(SystemStatus.GetNetwork().Glyph));
            case BarItem.Bluetooth:
                return Cell(Glyph(0xE702));
            case BarItem.Brightness:
                return Cell(Glyph(0xE706));
            case BarItem.Volume:
                return Cell(Glyph(0xE767));
            case BarItem.Battery:
                return Cell(Glyph(SystemStatus.GetBattery()?.Glyph ?? (char)0xE83F));
            case BarItem.Notifications:
                return Cell(Glyph(0xEA8F));
            case BarItem.FreeRam:
            {
                var icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(Control.ForegroundProperty, "StrataForegroundBrush");
                return Cell(icon);
            }
            case BarItem.Cpu or BarItem.Gpu or BarItem.Ram or BarItem.Fps:
                return MeterVisual(item);
            case BarItem.Clock:
                return ClockVisual();
            default:
                return Cell(new Wpf.Ui.Controls.SymbolIcon { Symbol = BarLayout.Icon(item) });
        }
    }

    private FrameworkElement MeterVisual(BarItem item)
    {
        string name = item switch { BarItem.Cpu => "CPU", BarItem.Gpu => "GPU", BarItem.Ram => "RAM", _ => "FPS" };
        double? percent = item switch
        {
            BarItem.Cpu => SystemMonitor.CpuPercent,
            BarItem.Gpu => SystemMonitor.GpuPercent,
            BarItem.Ram => SystemMonitor.RamTotal > 0 ? SystemMonitor.RamPercent : null,
            _ => null,
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        if (item == BarItem.Fps)
        {
            stack.Children.Add(Text("—", 11, bold: true));
            stack.Children.Add(Text("FPS", 10));
        }
        else
        {
            string value = percent is { } p ? $"{Math.Round(p)}%" : "--%";
            if (_vertical)
            {
                stack.Children.Add(Text(name, 10));
                stack.Children.Add(Text(value, 11, bold: true));
            }
            else
            {
                stack.Children.Add(Text(name + " " + value, 11, bold: true));
            }
        }
        return new Border { MinWidth = 40, Height = _vertical ? 40 : _thickness - 8, Child = stack };
    }

    private FrameworkElement ClockVisual()
    {
        var now = DateTime.Now;
        var s = Settings.Current.For(_device);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = _vertical ? new Thickness(2, 6, 2, 6) : new Thickness(10, 0, 10, 0) };
        var time = Text(now.ToString(s.ShowSeconds ? "T" : "t"), _vertical ? 11 : 12);
        time.HorizontalAlignment = _vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        stack.Children.Add(time);
        if (s.ShowDate)
        {
            var date = Text(_vertical ? now.ToString(Taskbar.ShortDateWithoutYear()) : now.ToString("d"), _vertical ? 11 : 12);
            date.HorizontalAlignment = time.HorizontalAlignment;
            stack.Children.Add(date);
        }
        return new Border { Height = _vertical ? double.NaN : _thickness - 8, Child = stack };
    }

    /// <summary>Las ventanas abiertas de este monitor (sus íconos), o huecos si no hay ninguna.</summary>
    private FrameworkElement AppsVisual()
    {
        var panel = new StackPanel { Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal };
        var icons = OpenWindowIcons().Take(_vertical ? 6 : 10).ToList();
        int count = Math.Max(icons.Count, 3);
        for (int i = 0; i < count; i++)
        {
            UIElement content;
            if (i < icons.Count)
            {
                content = new Image { Source = icons[i], Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            }
            else
            {
                var gap = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Opacity = 0.5 };
                gap.SetResourceReference(Border.BackgroundProperty, "StrataHoverBrush");
                content = gap;
            }
            var grid = new Grid { Width = _vertical ? 40 : 44, Height = _vertical ? 40 : _thickness - 8 };
            grid.Children.Add(content);
            if (i < icons.Count)
            {
                var line = new Border
                {
                    Width = _vertical ? 3 : 14,
                    Height = _vertical ? 14 : 3,
                    CornerRadius = new CornerRadius(1.5),
                    HorizontalAlignment = _vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                    VerticalAlignment = _vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom,
                    Margin = new Thickness(1),
                    Opacity = 0.6,
                };
                line.SetResourceReference(Border.BackgroundProperty, "StrataForegroundBrush");
                grid.Children.Add(line);
            }
            panel.Children.Add(grid);
        }
        return panel;
    }

    private IEnumerable<ImageSource> OpenWindowIcons()
    {
        IntPtr monitor = ManagedShell.AppBar.AppBarScreen.FromAllScreens().FirstOrDefault(sc => sc.DeviceName == _device)?.HMonitor ?? IntPtr.Zero;
        try
        {
            return App.Current.Shell.Tasks.GroupedWindows.OfType<ApplicationWindow>()
                .Where(w => w.ShowInTaskbar && w.HMonitor == monitor && w.Icon != null)
                .GroupBy(w => w.WinFileName)
                .Select(g => g.First().Icon!)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>La flecha de los ocultos y los íconos anclados de la bandeja.</summary>
    private FrameworkElement TrayVisual()
    {
        var panel = new StackPanel { Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal };
        panel.Children.Add(Cell(Glyph(0xE70E, 11), 28));
        var icons = App.Current.Shell.NotificationArea.PinnedIcons.OfType<ShellNotifyIcon>()
            .Where(i => i.Icon != null && !TrayIcon.IsReplacedByStrata(i)).Take(8).ToList();
        var wrap = _vertical ? (Panel)new WrapPanel { Width = _thickness - 8, HorizontalAlignment = HorizontalAlignment.Center } : new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var icon in icons)
            wrap.Children.Add(new Border
            {
                Width = 28,
                Height = _vertical ? 28 : _thickness - 8,
                Child = new Image { Source = icon.Icon, Width = 16, Height = 16 },
            });
        panel.Children.Add(wrap);
        return panel;
    }

    // ── Menús ───────────────────────────────────────────────────────────────

    private ContextMenu ItemMenu(BarItem item)
    {
        var menu = new ContextMenu();
        if (BarLayout.Zoned(item))
        {
            var position = new MenuItem { Header = Loc.T("Posición en la barra") };
            foreach (var zone in new[] { BarZone.Start, BarZone.Center, BarZone.End })
            {
                var entry = Entry(BarLayout.ZoneName(zone, _vertical), () => EditSession.SetZone(_device, Edge, item, zone));
                entry.IsCheckable = true;
                entry.IsChecked = BarLayout.ZoneOf(_bar, item) == zone;
                position.Items.Add(entry);
            }
            menu.Items.Add(position);
        }
        var move = new MenuItem { Header = Loc.T("Mover a otra barra") };
        foreach (var edge in Edges.Where(e => e != Edge))
            move.Items.Add(Entry(BarLayout.BarName(edge), () => EditSession.PlaceItem(_device, edge, item, null, BarLayout.ZoneOf(_bar, item))));
        menu.Items.Add(move);
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("Quitar"), () => EditSession.RemoveItem(_device, item)));
        return menu;
    }

    private ContextMenu BarMenu(ScreenLayout layout)
    {
        var menu = new ContextMenu();
        var add = new MenuItem { Header = Loc.T("Agregar elemento") };
        foreach (var item in BarLayout.All.Where(i => !_bar.Items.Contains(i)))
        {
            bool elsewhere = layout.Bars.Any(b => b.Items.Contains(item));
            string header = elsewhere ? Loc.F("{0} (está en otra barra)", BarLayout.Name(item)) : BarLayout.Name(item);
            add.Items.Add(Entry(header, () => EditSession.PlaceItem(_device, Edge, item, null, BarZone.End)));
        }
        add.IsEnabled = add.Items.Count > 0;
        menu.Items.Add(add);
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("Quitar esta barra"), () => EditSession.RemoveBar(_device, Edge)));
        return menu;
    }

    internal static readonly TaskbarPosition[] Edges = [TaskbarPosition.Bottom, TaskbarPosition.Top, TaskbarPosition.Left, TaskbarPosition.Right];

    internal static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Modo edición: \"{header}\" falló", ex);
            }
        };
        return entry;
    }
}
