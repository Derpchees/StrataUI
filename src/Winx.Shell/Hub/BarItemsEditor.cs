using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Hub;

/// <summary>
/// Editor de "Qué lleva cada barra": una fila por barra abierta con sus elementos como fichas que
/// se arrastran (ver <see cref="ChipDrag"/>), + para agregar y × para quitar. Guarda las listas de
/// todas las barras en <see cref="WinxSettings.BarItems"/> y se rehace cuando las barras cambian
/// (también si se ordenaron arrastrando en la barra misma).
/// </summary>
public sealed class BarItemsEditor : UserControl
{
    private static readonly string[] PositionNames = [Loc.T("Abajo"), Loc.T("Arriba"), Loc.T("Izquierda"), Loc.T("Derecha")];

    private readonly StackPanel _list = new();
    private readonly ChipDrag _drag;
    private readonly List<(string Id, WrapPanel Panel)> _barPanels = new();
    private string _builtFor = "";

    public BarItemsEditor()
    {
        Content = _list;
        _drag = new ChipDrag(this, () => _barPanels.Select(b => (Panel)b.Panel),
            (chip, panel) => !panel.Children.OfType<FrameworkElement>().Any(c => Equals(c.Tag, chip.Tag)));
        _drag.Committed += Save;
        Loaded += (_, _) =>
        {
            Settings.Changed += OnSettingsChanged;
            Build();
        };
        Unloaded += (_, _) => Settings.Changed -= OnSettingsChanged;
    }

    /// <summary>Vuelve a lo de siempre en todas las barras.</summary>
    public void Reset()
    {
        Settings.Update(s => s.BarItems.Clear());
        Build();
    }

    private void OnSettingsChanged()
    {
        if (BarsKey() != _builtFor)
            Build();
    }

    private static string BarsKey() =>
        string.Join(";", App.Current.Taskbars.Select(t => $"{t.BarId}:{t.Position}:{string.Join(",", t.Items)}"));

    private void Build()
    {
        _builtFor = BarsKey();
        _list.Children.Clear();
        _barPanels.Clear();
        var screens = Forms.Screen.AllScreens.OrderBy(sc => sc.Bounds.X).ThenBy(sc => sc.Bounds.Y).Select(sc => sc.DeviceName).ToList();
        var bars = App.Current.Taskbars
            .OrderBy(t => screens.IndexOf(BarLayout.DeviceOf(t.BarId)))
            .ThenBy(t => BarLayout.IsSecond(t.BarId))
            .ToList();
        foreach (var bar in bars)
        {
            int monitor = screens.IndexOf(BarLayout.DeviceOf(bar.BarId));
            string title = screens.Count > 1 ? Loc.F("Monitor {0}", monitor + 1) : Loc.T("Barra");
            title += " · " + PositionNames[(int)bar.Position];
            if (BarLayout.IsSecond(bar.BarId))
                title += " · " + Loc.T("segunda barra");

            var chips = new WrapPanel { MinHeight = 34 };
            foreach (var item in bar.Items)
                chips.Children.Add(Chip(item));
            _barPanels.Add((bar.BarId, chips));

            var add = new Wpf.Ui.Controls.Button
            {
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Add24 },
                Padding = new Thickness(8, 5, 8, 5),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = Loc.T("Agregar a esta barra"),
            };
            add.Click += (_, _) => ShowAddMenu(add, chips);

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = title });
            header.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Text = bar.Role.ShowsApps ? Loc.T("Lleva las ventanas abiertas") : Loc.T("Sin ventanas abiertas"),
            });

            var row = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(add);
            row.Children.Add(chips);

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(row);
            _list.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(16, 12, 12, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
                Child = content,
            });
        }
    }

    /// <summary>Ficha de un elemento: ícono, nombre y × para quitarlo (la bandeja no se quita, solo se mueve).</summary>
    private FrameworkElement Chip(BarItem item)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = BarLayout.Icon(item), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) });
        content.Children.Add(new TextBlock { Text = BarLayout.Name(item), VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("CardTitle") });
        var chip = new Border { Style = (Style)FindResource("BarChip"), Tag = item, Child = content, ToolTip = Loc.T("Arrastra para moverlo") };
        if (item != BarItem.Tray)
        {
            var remove = new Wpf.Ui.Controls.Button
            {
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss16, FontSize = 11 },
                Padding = new Thickness(4),
                Margin = new Thickness(6, 0, 0, 0),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = Loc.T("Quitar de esta barra"),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            remove.Click += (_, _) =>
            {
                if (chip.Parent is Panel panel)
                    panel.Children.Remove(chip);
                Save();
            };
            content.Children.Add(remove);
        }
        else
        {
            content.Children.Add(new Border { Width = 8 });
        }
        _drag.Attach(chip);
        return chip;
    }

    /// <summary>Menú del + : los elementos que esa barra aún no lleva (la bandeja va en una sola barra: se arrastra).</summary>
    private void ShowAddMenu(FrameworkElement anchor, WrapPanel chips)
    {
        var present = chips.Children.OfType<FrameworkElement>().Select(c => (BarItem)c.Tag).ToHashSet();
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var item in Enum.GetValues<BarItem>().Where(i => i != BarItem.Tray && !present.Contains(i)))
        {
            var entry = new MenuItem
            {
                Header = BarLayout.Name(item),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = BarLayout.Icon(item) },
            };
            entry.Click += (_, _) =>
            {
                // Inicio va delante; lo demás, antes del reloj si lo hay (como en Windows).
                int index = item == BarItem.Start ? 0
                    : chips.Children.OfType<FrameworkElement>().ToList().FindIndex(c => (BarItem)c.Tag == BarItem.Clock) is int clock and >= 0 ? clock
                    : chips.Children.Count;
                chips.Children.Insert(index, Chip(item));
                Save();
            };
            menu.Items.Add(entry);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.T("Ya lleva todos los elementos"), IsEnabled = false });
        menu.IsOpen = true;
    }

    /// <summary>Guarda lo que muestra el editor para todas las barras (así ninguna queda con los de siempre y la bandeja repetida).</summary>
    private void Save()
    {
        var layout = _barPanels.ToDictionary(
            b => b.Id,
            b => b.Panel.Children.OfType<FrameworkElement>().Select(c => (BarItem)c.Tag).ToList());
        Settings.Update(s =>
        {
            foreach (var (id, items) in layout)
                s.BarItems[id] = items;
        });
    }
}
