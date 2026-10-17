using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub;

/// <summary>
/// "Qué lleva la barra" del monitor elegido (<see cref="ScreenPicker"/>): qué bordes tienen barra y
/// un interruptor por elemento en cada una. Cada monitor lleva cada elemento una sola vez, así que
/// activarlo en una barra lo quita de la otra. El orden y el lugar se cambian arrastrando en la
/// barra misma o en el modo edición (<see cref="EditMode.EditSession"/>).
/// </summary>
public sealed class BarItemsEditor : UserControl
{
    private static readonly TaskbarPosition[] Edges = [TaskbarPosition.Bottom, TaskbarPosition.Top, TaskbarPosition.Left, TaskbarPosition.Right];

    private readonly StackPanel _list = new();
    private string _builtFor = "";

    public BarItemsEditor()
    {
        Content = _list;
        Loaded += (_, _) =>
        {
            Settings.Changed += OnChanged;
            ScreenPicker.Changed += Build;
            Build();
        };
        Unloaded += (_, _) =>
        {
            Settings.Changed -= OnChanged;
            ScreenPicker.Changed -= Build;
        };
    }

    private static ScreenLayout? Layout => Settings.Current.Layouts.GetValueOrDefault(ScreenPicker.Device);

    /// <summary>Vuelve a lo de siempre en este monitor (una barra abajo).</summary>
    public void Reset()
    {
        string device = ScreenPicker.Device;
        Settings.Update(s => s.Layouts[device] = BarLayout.DefaultLayout(device == ThemeManager.MainDevice, s));
        Build();
    }

    /// <summary>Si el diseño cambió (también al ordenar arrastrando en la barra o en el modo edición), se rehace.</summary>
    private void OnChanged()
    {
        if (LayoutKey() != _builtFor)
            Build();
    }

    private static string LayoutKey() =>
        ScreenPicker.Device + "#" + (Layout is { } layout
            ? string.Join(";", layout.Bars.Select(b => $"{b.Edge}:{string.Join(",", b.Items.Select(i => $"{i}/{BarLayout.ZoneOf(b, i)}"))}"))
            : "");

    private void Build()
    {
        _builtFor = LayoutKey();
        _list.Children.Clear();
        _list.Children.Add(EdgesRow());
        var layout = Layout;
        if (layout is null || layout.Bars.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Margin = new Thickness(2, 4, 0, 8),
                Text = Loc.T("Este monitor no tiene barra: activa un borde arriba."),
            });
            return;
        }
        foreach (var bar in BarLayout.InCreationOrder(layout))
        {
            var rows = new StackPanel();
            rows.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardTitle"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
                Text = BarLayout.BarName(bar.Edge),
            });
            foreach (var item in BarLayout.All)
                rows.Children.Add(Row(layout, bar, item));
            _list.Children.Add(Card(rows));
        }
    }

    private Border Card(UIElement child) => new()
    {
        Margin = new Thickness(0, 0, 0, 6),
        Padding = new Thickness(18, 10, 18, 10),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
        Child = child,
    };

    /// <summary>Qué bordes de este monitor tienen barra.</summary>
    private FrameworkElement EdgesRow()
    {
        var layout = Layout;
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var edge in Edges)
        {
            bool on = layout?.Bars.Any(b => b.Edge == edge) == true;
            var box = new CheckBox { Content = BarLayout.EdgeName(edge), IsChecked = on, Margin = new Thickness(0, 0, 18, 0) };
            box.Click += (_, _) => SetEdge(edge, box.IsChecked == true);
            row.Children.Add(box);
        }
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardTitle"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
            Text = Loc.T("Bordes con barra"),
        });
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.T("Las barras de arriba y abajo ocupan todo el ancho; las de los lados van entre ellas."),
        });
        return Card(panel);
    }

    /// <summary>Agrega una barra vacía en ese borde o la quita (lo que llevaba sale del monitor).</summary>
    private static void SetEdge(TaskbarPosition edge, bool on)
    {
        string device = ScreenPicker.Device;
        Settings.Update(s =>
        {
            if (!s.Layouts.TryGetValue(device, out var layout))
                s.Layouts[device] = layout = new ScreenLayout();
            layout.Bars.RemoveAll(b => b.Edge == edge);
            if (on)
                layout.Bars.Add(new BarConfig { Edge = edge });
        });
    }

    private FrameworkElement Row(ScreenLayout layout, BarConfig bar, BarItem item)
    {
        bool has = bar.Items.Contains(item);
        var toggle = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = has, HorizontalAlignment = HorizontalAlignment.Right };
        // En otra barra de este monitor: activarlo aquí lo trae.
        if (!has && layout.Bars.Any(b => b.Items.Contains(item)))
            toggle.ToolTip = Loc.T("Ahora está en otra barra: activarlo aquí lo mueve");
        toggle.Click += (_, _) => Set(bar.Edge, item, toggle.IsChecked == true);

        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = BarLayout.Icon(item), FontSize = 18, Margin = new Thickness(0, 0, 14, 0) });
        label.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = BarLayout.Name(item), VerticalAlignment = VerticalAlignment.Center });

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (BarLayout.Zoned(item) && has)
            right.Children.Add(ZonePicker(bar, item));
        right.Children.Add(toggle);

        var row = new Grid { MinHeight = 40 };
        row.Children.Add(label);
        row.Children.Add(right);
        return row;
    }

    /// <summary>Dónde va el elemento a lo largo de la barra: izquierda, centro o derecha (arriba/abajo en vertical).</summary>
    private static FrameworkElement ZonePicker(BarConfig bar, BarItem item)
    {
        BarZone[] zones = [BarZone.Start, BarZone.Center, BarZone.End];
        var box = new ComboBox
        {
            MinWidth = 120,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("Posición en la barra"),
        };
        foreach (var zone in zones)
            box.Items.Add(BarLayout.ZoneName(zone, BarLayout.IsVertical(bar.Edge)));
        box.SelectedIndex = Array.IndexOf(zones, BarLayout.ZoneOf(bar, item));
        string id = BarLayout.Id(ScreenPicker.Device, bar.Edge);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
                Settings.Update(s => BarLayout.SetZone(id, item, zones[box.SelectedIndex], s));
        };
        return box;
    }

    /// <summary>Agrega o quita el elemento de esa barra; al agregarlo sale de las otras barras del monitor.</summary>
    private static void Set(TaskbarPosition edge, BarItem item, bool on)
    {
        string device = ScreenPicker.Device;
        Settings.Update(s =>
        {
            if (!s.Layouts.TryGetValue(device, out var layout))
                return;
            foreach (var bar in layout.Bars)
            {
                bar.Items.Remove(item);
                bar.Zones.Remove(item);
            }
            if (on && layout.Bars.FirstOrDefault(b => b.Edge == edge) is { } target)
                BarLayout.Insert(target.Items, item);
        });
    }
}
