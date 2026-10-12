using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Hub.Pages;

public partial class TaskbarPage : Page
{
    private bool _loading = true;

    public TaskbarPage()
    {
        InitializeComponent();

        var s = Settings.Current;
        Select(AlignmentBox, s.TaskbarAlignment.ToString());
        CenterAppsSwitch.IsChecked = s.TaskbarCenterApps;
        HeightBox.SelectedItem = HeightBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == s.TaskbarHeight.ToString()) ?? HeightBox.Items[1];
        Select(AppsModeBox, s.TaskbarAppsMode.ToString());
        StatusAllSwitch.IsChecked = s.StatusOnAllTaskbars;
        BuildMainScreenBox();
        BuildMonitorList();

        _loading = false;
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    // ── Monitores ───────────────────────────────────────────────────────────

    private static readonly string[] PositionNames = [Loc.T("Abajo"), Loc.T("Arriba"), Loc.T("Izquierda"), Loc.T("Derecha")];

    // En el orden de SecondBarContent.
    private static readonly string[] SecondBarNames =
        [Loc.T("Ninguna"), Loc.T("Apps abiertas"), Loc.T("Área de estado y reloj"), Loc.T("Completa")];

    private static string SecondBarDescription(TaskbarPosition position) =>
        Loc.F("En el borde opuesto ({0}). Con \"Apps abiertas\" o \"Área de estado y reloj\" las dos barras se reparten el contenido; \"Completa\" repite todo.",
              PositionNames[(int)WinxSettings.Opposite(position)]);

    /// <summary>Monitores de izquierda a derecha (así se numeran en toda la página).</summary>
    private static List<Forms.Screen> Screens() =>
        Forms.Screen.AllScreens.OrderBy(sc => sc.Bounds.X).ThenBy(sc => sc.Bounds.Y).ToList();

    private static string NameOf(Forms.Screen screen, int index, int count) =>
        count == 1 ? Loc.T("Monitor")
        : screen.Primary ? Loc.F("Monitor {0} (principal de Windows)", index + 1)
        : Loc.F("Monitor {0}", index + 1);

    private static string MainDevice(List<Forms.Screen> screens) =>
        App.MainScreenDevice(screens.Select(sc => (sc.DeviceName, sc.Primary)));

    private void BuildMainScreenBox()
    {
        var screens = Screens();
        MultiMonitorOptions.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        MainScreenBox.Items.Add(new ComboBoxItem { Content = Loc.T("Igual que Windows"), Tag = null });
        for (int i = 0; i < screens.Count; i++)
            MainScreenBox.Items.Add(new ComboBoxItem { Content = NameOf(screens[i], i, screens.Count), Tag = screens[i].DeviceName });
        string? chosen = Settings.Current.TaskbarMainScreen;
        MainScreenBox.SelectedItem = MainScreenBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == chosen)
                                     ?? MainScreenBox.Items[0];
    }

    /// <summary>
    /// Una tarjeta por monitor: borde de la barra y, salvo en el principal de WINX (que siempre la
    /// lleva), si tiene barra.
    /// </summary>
    private void BuildMonitorList()
    {
        MonitorList.Children.Clear();
        var screens = Screens();
        string main = MainDevice(screens);
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            string device = screen.DeviceName;
            bool isMain = device == main;
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = NameOf(screen, i, screens.Count) });
            header.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Text = $"{screen.Bounds.Width} × {screen.Bounds.Height}"
                       + (isMain && screens.Count > 1 ? " · " + Loc.T("lleva la bandeja del sistema") : ""),
            });

            // Segunda barra en el borde opuesto (se describe según la posición de la primera).
            var secondText = new TextBlock { Style = (Style)FindResource("CardDescription"), Text = SecondBarDescription(Settings.Current.PositionOf(device)) };
            var second = new ComboBox { MinWidth = 200, ItemsSource = SecondBarNames, SelectedIndex = (int)Settings.Current.SecondBarOf(device) };
            second.SelectionChanged += (_, _) =>
            {
                if (_loading || second.SelectedIndex < 0)
                    return;
                var content = (SecondBarContent)second.SelectedIndex;
                Settings.Update(s =>
                {
                    if (content == SecondBarContent.None)
                        s.SecondTaskbars.Remove(device);
                    else
                        s.SecondTaskbars[device] = content;
                });
            };

            var position = new ComboBox { MinWidth = 140, ItemsSource = PositionNames, SelectedIndex = (int)Settings.Current.PositionOf(device) };
            position.SelectionChanged += (_, _) =>
            {
                if (_loading || position.SelectedIndex < 0)
                    return;
                Settings.Update(s => s.TaskbarPositions[device] = (TaskbarPosition)position.SelectedIndex);
                secondText.Text = SecondBarDescription((TaskbarPosition)position.SelectedIndex);
            };

            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(position);
            if (!isMain)
            {
                var visible = new Wpf.Ui.Controls.ToggleSwitch
                {
                    Margin = new Thickness(16, 0, 0, 0),
                    IsChecked = !Settings.Current.TaskbarHiddenScreens.Contains(device),
                    ToolTip = Loc.T("Mostrar la barra en este monitor"),
                };
                visible.Click += (_, _) =>
                {
                    bool show = visible.IsChecked == true;
                    position.IsEnabled = second.IsEnabled = show;
                    Settings.Update(s =>
                    {
                        s.TaskbarHiddenScreens.Remove(device);
                        if (!show)
                            s.TaskbarHiddenScreens.Add(device);
                    });
                };
                position.IsEnabled = second.IsEnabled = visible.IsChecked == true;
                controls.Children.Add(visible);
            }

            MonitorList.Children.Add(new Wpf.Ui.Controls.CardControl
            {
                Style = (Style)FindResource("HubCard"),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Desktop24 },
                Header = header,
                Content = controls,
            });

            var secondHeader = new StackPanel();
            secondHeader.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("Segunda barra") });
            secondHeader.Children.Add(secondText);
            MonitorList.Children.Add(new Wpf.Ui.Controls.CardControl
            {
                Style = (Style)FindResource("HubCard"),
                Margin = new Thickness(40, 0, 0, 12),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowBidirectionalUpDown24 },
                Header = secondHeader,
                Content = second,
            });
        }
    }

    private void MainScreen_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MainScreenBox.SelectedItem is not ComboBoxItem item)
            return;
        var device = (string?)item.Tag;
        // El principal de WINX siempre tiene barra: si estaba oculta, se vuelve a mostrar.
        Settings.Update(s =>
        {
            s.TaskbarMainScreen = device;
            if (device != null)
                s.TaskbarHiddenScreens.Remove(device);
        });
        _loading = true;
        BuildMonitorList();
        _loading = false;
    }

    private void AppsMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AppsModeBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.Update(s => s.TaskbarAppsMode = Enum.Parse<TaskbarAppsMode>(tag));
    }

    private void StatusAll_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.StatusOnAllTaskbars = StatusAllSwitch.IsChecked == true);

    // ── Barra ───────────────────────────────────────────────────────────────

    private void Alignment_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AlignmentBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.Update(s => s.TaskbarAlignment = Enum.Parse<TaskbarAlignment>(tag));
    }

    private void CenterApps_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.TaskbarCenterApps = CenterAppsSwitch.IsChecked == true);

    private void Height_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HeightBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        Settings.Update(s => s.TaskbarHeight = int.Parse(tag));
    }
}
