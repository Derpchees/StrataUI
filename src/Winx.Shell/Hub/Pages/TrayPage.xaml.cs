using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Winx.Shell.Controls;
using Winx.Shell.Services;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using Winx.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Hub.Pages;

/// <summary>
/// Elementos de la barra: qué lleva cada barra y en qué orden (se arrastran), y las opciones de
/// cada elemento agrupadas bajo él. Se abre con --hub Tray.
/// </summary>
public partial class TrayPage : Page
{
    /// <summary>Fila de la lista: un ícono de la bandeja con nombre legible.</summary>
    public sealed record TrayIconItem(ShellNotifyIcon Icon, string Name, string Detail)
    {
        public ImageSource? Image => Icon.Icon;
        public bool IsPinned => Icon.IsPinned;
    }

    private readonly NotifyCollectionChangedEventHandler _onIconsChanged;
    private readonly ChipDrag _drag;
    private readonly List<(string Id, WrapPanel Panel)> _barPanels = new();
    private bool _loading = true;

    public TrayPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        SecondsSwitch.IsChecked = s.ShowSeconds;
        DateSwitch.IsChecked = s.ShowDate;
        MediaControlsSwitch.IsChecked = s.ShowMediaControls;
        BatteryPercentSwitch.IsChecked = s.ShowBatteryPercent;
        OsdSwitch.IsChecked = s.ShowOsd;
        TemperaturesSwitch.IsChecked = s.ShowTemperatures;
        SensorsStartupSwitch.IsChecked = s.SensorsAtStartup;
        BuildFpsScreens();

        _drag = new ChipDrag(this, () => _barPanels.Select(b => (Panel)b.Panel),
            (chip, panel) => !panel.Children.OfType<FrameworkElement>().Any(c => Equals(c.Tag, chip.Tag)));
        _drag.Committed += SaveLayout;
        BuildBars();
        UpdateSensors();
        _loading = false;

        _onIconsChanged = (_, _) => Dispatcher.BeginInvoke(Refresh);
        Loaded += (_, _) =>
        {
            App.Current.Shell.NotificationArea.TrayIcons.CollectionChanged += _onIconsChanged;
            Sensors.Changed += UpdateSensors;
            Settings.Changed += OnSettingsChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            App.Current.Shell.NotificationArea.TrayIcons.CollectionChanged -= _onIconsChanged;
            Sensors.Changed -= UpdateSensors;
            Settings.Changed -= OnSettingsChanged;
        };
    }

    // ── Qué lleva cada barra ────────────────────────────────────────────────

    private static readonly string[] PositionNames = [Loc.T("Abajo"), Loc.T("Arriba"), Loc.T("Izquierda"), Loc.T("Derecha")];

    private string _builtFor = "";

    /// <summary>Si las barras se recrearon (otro monitor, otra posición, la bandeja), se rehace el editor.</summary>
    private void OnSettingsChanged()
    {
        if (BarsKey() != _builtFor)
            BuildBars();
    }

    private static string BarsKey() =>
        string.Join(";", App.Current.Taskbars.Select(t => $"{t.BarId}:{t.Position}:{string.Join(",", t.Items)}"));

    /// <summary>Una fila por barra abierta (monitores de izquierda a derecha, primero la barra y luego la segunda).</summary>
    private void BuildBars()
    {
        _builtFor = BarsKey();
        BarList.Children.Clear();
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
                chips.Children.Add(Chip(item, bar.BarId));
            _barPanels.Add((bar.BarId, chips));

            var add = new Wpf.Ui.Controls.Button
            {
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Add24 },
                Padding = new Thickness(8, 5, 8, 5),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = Loc.T("Agregar a esta barra"),
            };
            string barId = bar.BarId;
            add.Click += (_, _) => ShowAddMenu(add, barId, chips);

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
            BarList.Children.Add(new Border
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
    private FrameworkElement Chip(BarItem item, string barId)
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
                SaveLayout();
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
    private void ShowAddMenu(FrameworkElement anchor, string barId, WrapPanel chips)
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
                chips.Children.Insert(index, Chip(item, barId));
                SaveLayout();
            };
            menu.Items.Add(entry);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.T("Ya lleva todos los elementos"), IsEnabled = false });
        menu.IsOpen = true;
    }

    /// <summary>Guarda lo que muestra el editor para todas las barras (así ninguna queda con los de siempre y la bandeja repetida).</summary>
    private void SaveLayout()
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

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        Settings.Update(s => s.BarItems.Clear());
        BuildBars();
    }

    // ── Opciones ────────────────────────────────────────────────────────────

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.Update(s =>
        {
            s.ShowSeconds = SecondsSwitch.IsChecked == true;
            s.ShowDate = DateSwitch.IsChecked == true;
            s.ShowMediaControls = MediaControlsSwitch.IsChecked == true;
            s.ShowBatteryPercent = BatteryPercentSwitch.IsChecked == true;
            s.ShowOsd = OsdSwitch.IsChecked == true;
            s.ShowTemperatures = TemperaturesSwitch.IsChecked == true;
            s.SensorsAtStartup = SensorsStartupSwitch.IsChecked == true;
        });
    }

    private void BuildFpsScreens()
    {
        var screens = Forms.Screen.AllScreens.OrderBy(sc => sc.Bounds.X).ThenBy(sc => sc.Bounds.Y).ToList();
        FpsScreenBox.Items.Add(new ComboBoxItem { Content = Loc.T("El principal de WINX"), Tag = null });
        for (int i = 0; i < screens.Count; i++)
        {
            string name = screens.Count == 1 ? Loc.T("Monitor") : Loc.F("Monitor {0}", i + 1);
            FpsScreenBox.Items.Add(new ComboBoxItem
            {
                Content = $"{name} ({screens[i].Bounds.Width} × {screens[i].Bounds.Height})",
                Tag = screens[i].DeviceName,
            });
        }
        string? chosen = Settings.Current.FpsScreen;
        FpsScreenBox.SelectedItem = FpsScreenBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == chosen)
                                    ?? FpsScreenBox.Items[0];
    }

    private void FpsScreen_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && FpsScreenBox.SelectedItem is ComboBoxItem item)
            Settings.Update(s => s.FpsScreen = (string?)item.Tag);
    }

    private void UpdateSensors()
    {
        SensorsState.Text = Sensors.Starting ? Loc.T("Iniciando… acepta el permiso de Windows.")
            : Sensors.Running ? Loc.T("Activas: temperaturas y FPS hasta que se cierre WINX.")
            : Loc.T("Detenidas. También se inician con un clic en el contador de FPS.");
        SensorsButton.Content = Sensors.Running ? Loc.T("Detener") : Loc.T("Iniciar");
        SensorsButton.IsEnabled = !Sensors.Starting;
        PawnIoBar.IsOpen = !Sensors.PawnIoInstalled;
    }

    private async void Sensors_Click(object sender, RoutedEventArgs e)
    {
        if (Sensors.Running)
            Sensors.Stop();
        else
            await Sensors.StartAsync();
    }

    // ── Íconos de la bandeja ────────────────────────────────────────────────

    private void Refresh()
    {
        var items = App.Current.Shell.NotificationArea.TrayIcons
            .Where(icon => !TrayIcon.IsReplacedByWinx(icon))
            .Select(ToItem)
            .OrderByDescending(item => item.IsPinned)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        IconList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TrayIconItem ToItem(ShellNotifyIcon icon)
    {
        string? description = null;
        if (!string.IsNullOrEmpty(icon.Path) && File.Exists(icon.Path))
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(icon.Path).FileDescription;
            }
            catch (IOException)
            {
            }
        }

        string fileName = string.IsNullOrEmpty(icon.Path) ? "" : Path.GetFileName(icon.Path);
        string title = icon.Title?.Split('\n')[0].Trim() ?? "";
        string name = FirstNonEmpty(description, title, fileName, Loc.T("Ícono sin nombre"));
        string detail = title != name && title.Length > 0 ? title : fileName;
        return new TrayIconItem(icon, name, detail);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(v => !string.IsNullOrWhiteSpace(v))!;

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: TrayIconItem item } toggle)
            TrayPromotion.SetPinned(item.Icon, toggle.IsChecked == true);
    }
}
