using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.EditMode;

/// <summary>Los widgets que se pueden agregar: los incluidos y los importados.</summary>
internal static class WidgetCatalog
{
    public sealed record Entry(SymbolRegular Icon, string Name, Func<WidgetConfig> Create);

    public static List<Entry> All()
    {
        var list = new List<Entry>
        {
            new(SymbolRegular.Clock24, Loc.T("Reloj"), () => new WidgetConfig { Kind = WidgetKind.Clock, ShowDate = false }),
            new(SymbolRegular.CalendarClock24, Loc.T("Reloj con fecha"), () => new WidgetConfig { Kind = WidgetKind.Clock, ShowDate = true }),
            new(SymbolRegular.MusicNote224, Loc.T("Visualizador de música"), () => new WidgetConfig { Kind = WidgetKind.Visualizer }),
            new(SymbolRegular.DeveloperBoard24, Loc.T("Medidor de CPU"), () => new WidgetConfig { Kind = WidgetKind.Cpu }),
            new(SymbolRegular.Gauge24, Loc.T("Medidor de GPU"), () => new WidgetConfig { Kind = WidgetKind.Gpu }),
            new(SymbolRegular.Games24, Loc.T("Contador de FPS"), () => new WidgetConfig { Kind = WidgetKind.Fps }),
            new(SymbolRegular.ArrowBidirectionalUpDown24, Loc.T("Velocidad de internet"), () => new WidgetConfig { Kind = WidgetKind.NetSpeed }),
            new(SymbolRegular.Ram20, Loc.T("Medidor de RAM"), () => new WidgetConfig { Kind = WidgetKind.Ram }),
            new(SymbolRegular.Battery524, Loc.T("Batería"), () => new WidgetConfig { Kind = WidgetKind.Battery }),
            new(SymbolRegular.PlayCircle24, Loc.T("Reproductor"), () => new WidgetConfig { Kind = WidgetKind.Media }),
            new(SymbolRegular.CalendarMonth24, Loc.T("Calendario"), () => new WidgetConfig { Kind = WidgetKind.Calendar }),
            new(SymbolRegular.HardDrive20, Loc.T("Discos"), () => new WidgetConfig { Kind = WidgetKind.Disks }),
        };
        foreach (var module in ModuleStore.All<ModuleBase>(ModuleKind.Widget))
            list.Add(new(SymbolRegular.PuzzlePiece24, module.Name, () => new WidgetConfig { Kind = WidgetKind.Custom, Module = module.Id }));
        return list;
    }

    public static SymbolRegular IconOf(WidgetConfig widget) => widget.Kind switch
    {
        WidgetKind.Visualizer => SymbolRegular.MusicNote224,
        WidgetKind.Custom => SymbolRegular.PuzzlePiece24,
        WidgetKind.Cpu => SymbolRegular.DeveloperBoard24,
        WidgetKind.Gpu => SymbolRegular.Gauge24,
        WidgetKind.Fps => SymbolRegular.Games24,
        WidgetKind.NetSpeed => SymbolRegular.ArrowBidirectionalUpDown24,
        WidgetKind.Ram => SymbolRegular.Ram20,
        WidgetKind.Battery => SymbolRegular.Battery524,
        WidgetKind.Media => SymbolRegular.PlayCircle24,
        WidgetKind.Calendar => SymbolRegular.CalendarMonth24,
        WidgetKind.Disks => SymbolRegular.HardDrive20,
        _ => SymbolRegular.Clock24,
    };

    public static string NameOf(WidgetConfig widget) => widget.Kind switch
    {
        WidgetKind.Visualizer => Loc.T("Visualizador de música"),
        WidgetKind.Cpu => Loc.T("Medidor de CPU"),
        WidgetKind.Gpu => Loc.T("Medidor de GPU"),
        WidgetKind.Fps => Loc.T("Contador de FPS"),
        WidgetKind.NetSpeed => Loc.T("Velocidad de internet"),
        WidgetKind.Ram => Loc.T("Medidor de RAM"),
        WidgetKind.Battery => Loc.T("Batería"),
        WidgetKind.Media => Loc.T("Reproductor"),
        WidgetKind.Calendar => Loc.T("Calendario"),
        WidgetKind.Disks => Loc.T("Discos"),
        WidgetKind.Custom => ModuleStore.Get<WidgetModule>(ModuleKind.Widget, widget.Module)?.Name ?? Loc.T("Widget importado"),
        _ => widget.ShowDate ? Loc.T("Reloj con fecha") : Loc.T("Reloj"),
    };

    /// <summary>Tamaño aproximado (DIP) de un widget que todavía no tiene ventana.</summary>
    public static Size EstimatedSize(WidgetConfig widget)
    {
        var size = widget.Kind switch
        {
            WidgetKind.Clock => new Size(widget.ShowDate ? 300 : 240, widget.ShowDate ? 150 : 110),
            WidgetKind.Visualizer => new Size(380, 150),
            WidgetKind.Cpu or WidgetKind.Gpu or WidgetKind.Fps or WidgetKind.Ram or WidgetKind.Battery => new Size(190, 120),
            WidgetKind.NetSpeed => new Size(240, 120),
            WidgetKind.Media => new Size(400, 130),
            WidgetKind.Calendar => new Size(290, 330),
            WidgetKind.Disks => new Size(310, 160),
            _ => new Size(260, 150),
        };
        return new Size(size.Width * widget.Scale, size.Height * widget.Scale);
    }
}

/// <summary>
/// Biblioteca del modo edición: los elementos de barra y los widgets, para arrastrarlos a una barra
/// o al escritorio de cualquier monitor. Con un clic se agregan al monitor de esta capa (en el Hub
/// solo se arrastran). Los que ese monitor ya tiene se ven atenuados: arrastrarlos los mueve.
/// </summary>
internal sealed class EditLibrary : UserControl
{
    private readonly string? _device;
    private readonly WrapPanel _items = new();

    public EditLibrary(string? device)
    {
        _device = device;
        var content = new StackPanel();
        content.Children.Add(Section(Loc.T("Elementos de barra")));
        content.Children.Add(Hint(device is null
            ? Loc.T("Arrástralos a una barra de cualquier monitor, o a un borde sin barra para crearla.")
            : Loc.T("Arrástralos a una barra o a un borde sin barra; con un clic van a este monitor.")));
        content.Children.Add(_items);
        content.Children.Add(Section(Loc.T("Widgets")));
        content.Children.Add(Hint(Loc.T("Arrástralos al escritorio.")));
        var widgets = new WrapPanel();
        foreach (var entry in WidgetCatalog.All())
            widgets.Children.Add(WidgetChip(entry));
        content.Children.Add(widgets);
        Content = content;
        BuildItems();
        Loaded += (_, _) => EditSession.Changed += BuildItems;
        Unloaded += (_, _) => EditSession.Changed -= BuildItems;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 10, 0, 2),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 0, 6),
    };

    private void BuildItems()
    {
        _items.Children.Clear();
        var here = _device != null && EditSession.Layouts.TryGetValue(_device, out var layout)
            ? layout.Bars.SelectMany(b => b.Items).ToHashSet()
            : [];
        foreach (var item in BarLayout.All)
        {
            bool present = here.Contains(item);
            var chip = Chip(BarLayout.Icon(item), BarLayout.Name(item), present);
            chip.ToolTip = present ? Loc.T("Ya está en este monitor: arrástralo para moverlo") : BarLayout.Name(item);
            chip.MouseLeftButtonDown += (_, e) =>
                EditDrag.Press(chip, new ItemPayload(item, present ? _device : null), e, onClick: () => AddHere(item));
            _items.Children.Add(chip);
        }
    }

    /// <summary>Clic: el elemento va a la barra del reloj de este monitor (o a la primera; si no hay, crea una abajo).</summary>
    private void AddHere(BarItem item)
    {
        if (_device is null)
            return;
        var layout = EditSession.LayoutOf(_device);
        if (layout.Bars.Any(b => b.Items.Contains(item)))
            return;
        var target = layout.Bars.FirstOrDefault(b => b.Items.Contains(BarItem.Clock)) ?? BarLayout.InCreationOrder(layout).FirstOrDefault();
        EditSession.PlaceItem(_device, target?.Edge ?? TaskbarPosition.Bottom, item, null, BarZone.End);
    }

    private FrameworkElement WidgetChip(WidgetCatalog.Entry entry)
    {
        var chip = Chip(entry.Icon, entry.Name, dimmed: false);
        chip.MouseLeftButtonDown += (_, e) =>
        {
            var widget = entry.Create();
            var size = WidgetCatalog.EstimatedSize(widget);
            EditDrag.Press(chip, new WidgetPayload(widget, size, new Vector(size.Width / 2, size.Height / 2)), e, onClick: () =>
            {
                if (_device != null)
                    EditSession.PlaceWidget(widget, _device, 0.5, 0.3);
            });
        };
        return chip;
    }

    private static Border Chip(SymbolRegular icon, string name, bool dimmed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SymbolIcon { Symbol = icon, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        if (dimmed)
            row.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Checkmark16, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var chip = new Border
        {
            Child = row,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll,
            Opacity = dimmed ? 0.55 : 1,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
        chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        return chip;
    }
}
