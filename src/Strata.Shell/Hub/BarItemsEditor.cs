using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub;

/// <summary>
/// "Qué lleva la barra" del monitor elegido (<see cref="ScreenPicker"/>): un interruptor por
/// elemento en cada barra de ese monitor. El orden se cambia arrastrando en la barra misma. Al
/// guardar se escriben las listas de todas las barras (<see cref="StrataSettings.BarItems"/>); la
/// bandeja va en una sola barra, así que activarla aquí la quita de la otra.
/// </summary>
public sealed class BarItemsEditor : UserControl
{
    private static readonly string[] PositionNames = [Loc.T("Abajo"), Loc.T("Arriba"), Loc.T("Izquierda"), Loc.T("Derecha")];

    /// <summary>Orden en que se listan (el de la barra se elige arrastrando).</summary>
    private static readonly BarItem[] ListOrder =
    [
        BarItem.Start, BarItem.Tray, BarItem.Media, BarItem.Network, BarItem.Bluetooth, BarItem.Volume, BarItem.Brightness,
        BarItem.Battery, BarItem.Cpu, BarItem.Gpu, BarItem.Ram, BarItem.Fps, BarItem.FreeRam, BarItem.Clock,
    ];

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

    /// <summary>Vuelve a lo de siempre en las barras de este monitor.</summary>
    public void Reset()
    {
        string device = ScreenPicker.Device;
        Settings.Update(s =>
        {
            foreach (var key in s.BarItems.Keys.Where(k => BarLayout.DeviceOf(k) == device).ToList())
                s.BarItems.Remove(key);
        });
        Build();
    }

    /// <summary>Si las barras cambiaron (también al ordenarlas arrastrando en la barra), se rehace.</summary>
    private void OnChanged()
    {
        if (BarsKey() != _builtFor)
            Build();
    }

    private static string BarsKey() =>
        ScreenPicker.Device + "#" +
        string.Join(";", App.Current.Taskbars.Select(t => $"{t.BarId}:{t.Position}:{string.Join(",", t.Items.Select(i => $"{i}/{t.ZoneOf(i)}"))}"));

    private void Build()
    {
        _builtFor = BarsKey();
        _list.Children.Clear();
        var bars = App.Current.Taskbars
            .Where(t => t.Device == ScreenPicker.Device)
            .OrderBy(t => BarLayout.IsSecond(t.BarId))
            .ToList();
        if (bars.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Text = Loc.T("Este monitor no tiene barra (se activa más abajo, en Posición)."),
            });
            return;
        }
        foreach (var bar in bars)
        {
            var rows = new StackPanel();
            if (bars.Count > 1)
            {
                rows.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("CardTitle"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                    Text = (BarLayout.IsSecond(bar.BarId) ? Loc.T("Segunda barra") : Loc.T("Barra"))
                           + " · " + PositionNames[(int)bar.Position],
                });
            }
            foreach (var item in ListOrder)
                rows.Children.Add(Row(bar, item));
            _list.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(18, 10, 18, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
                Child = rows,
            });
        }
    }

    private FrameworkElement Row(Taskbar bar, BarItem item)
    {
        var toggle = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = bar.Has(item), HorizontalAlignment = HorizontalAlignment.Right };
        if (item == BarItem.Tray && bar.Has(item))
        {
            // La bandeja no se apaga: se mueve activándola en otra barra.
            toggle.IsEnabled = false;
            toggle.ToolTip = Loc.T("La bandeja va en una sola barra: actívala en otra para moverla");
        }
        toggle.Click += (_, _) => Set(bar, item, toggle.IsChecked == true);

        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = BarLayout.Icon(item), FontSize = 18, Margin = new Thickness(0, 0, 14, 0) });
        label.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = BarLayout.Name(item), VerticalAlignment = VerticalAlignment.Center });

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (item != BarItem.Start && bar.Has(item))
            right.Children.Add(ZonePicker(bar, item));
        right.Children.Add(toggle);

        var row = new Grid { MinHeight = 40 };
        row.Children.Add(label);
        row.Children.Add(right);
        return row;
    }

    /// <summary>Dónde va el elemento a lo largo de la barra: izquierda, centro o derecha (arriba/abajo en vertical).</summary>
    private static FrameworkElement ZonePicker(Taskbar bar, BarItem item)
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
            box.Items.Add(BarLayout.ZoneName(zone, bar.IsVertical));
        box.SelectedIndex = Array.IndexOf(zones, bar.ZoneOf(item));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
                Settings.Update(s => BarLayout.SetZone(bar.BarId, item, zones[box.SelectedIndex], s));
        };
        return box;
    }

    /// <summary>
    /// Agrega o quita el elemento de esa barra y guarda las listas de todas (así ninguna queda con
    /// los de siempre y la bandeja repetida). Inicio va delante; lo demás, antes del reloj.
    /// </summary>
    private static void Set(Taskbar bar, BarItem item, bool on)
    {
        var layout = App.Current.Taskbars.ToDictionary(t => t.BarId, t => t.Items.ToList());
        var items = layout[bar.BarId];
        items.Remove(item);
        if (on)
        {
            int clock = items.IndexOf(BarItem.Clock);
            items.Insert(item == BarItem.Start ? 0 : clock >= 0 ? clock : items.Count, item);
            if (item == BarItem.Tray)
                foreach (var (id, other) in layout.Where(p => p.Key != bar.BarId))
                    other.Remove(BarItem.Tray);
        }
        Settings.Update(s =>
        {
            foreach (var (id, list) in layout)
                s.BarItems[id] = list;
        });
    }
}
