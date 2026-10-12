using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Hub.Pages;

public partial class TaskbarPage : Page
{
    private readonly bool _loading = true;

    public TaskbarPage()
    {
        InitializeComponent();

        var s = Settings.Current;
        AlignmentBox.SelectedIndex = s.TaskbarAlignment == TaskbarAlignment.Left ? 1 : 0;
        HeightBox.SelectedItem = HeightBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == s.TaskbarHeight.ToString()) ?? HeightBox.Items[1];
        SecondsSwitch.IsChecked = s.ShowSeconds;
        DateSwitch.IsChecked = s.ShowDate;
        NetworkSwitch.IsChecked = s.ShowNetworkModule;
        BluetoothSwitch.IsChecked = s.ShowBluetoothModule;
        VolumeSwitch.IsChecked = s.ShowVolumeModule;
        BatterySwitch.IsChecked = s.ShowBatteryModule;
        PerMonitorSwitch.IsChecked = s.TaskbarWindowsPerMonitor;
        BuildMonitorList();

        _loading = false;
    }

    // ── Monitores ───────────────────────────────────────────────────────────

    private static readonly string[] PositionNames = [Loc.T("Abajo"), Loc.T("Arriba"), Loc.T("Izquierda"), Loc.T("Derecha")];

    /// <summary>Una tarjeta por monitor: borde de la barra y, en los secundarios, si lleva barra.</summary>
    private void BuildMonitorList()
    {
        var screens = System.Windows.Forms.Screen.AllScreens.OrderBy(sc => sc.Bounds.X).ThenBy(sc => sc.Bounds.Y).ToList();
        PerMonitorCard.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            string device = screen.DeviceName;
            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardTitle"),
                Text = screens.Count > 1 ? (screen.Primary ? Loc.F("Monitor {0} (principal)", i + 1) : Loc.F("Monitor {0}", i + 1)) : Loc.T("Monitor"),
            });
            header.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Text = $"{screen.Bounds.Width} × {screen.Bounds.Height}"
                       + (screen.Primary ? " · " + Loc.T("lleva la bandeja del sistema") : ""),
            });

            var position = new ComboBox { MinWidth = 140, ItemsSource = PositionNames, SelectedIndex = (int)Settings.Current.PositionOf(device) };
            position.SelectionChanged += (_, _) =>
            {
                if (!_loading && position.SelectedIndex >= 0)
                    Settings.Update(s => s.TaskbarPositions[device] = (TaskbarPosition)position.SelectedIndex);
            };

            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(position);
            if (!screen.Primary)
            {
                // El principal siempre tiene barra (bandeja, tecla Windows).
                var visible = new Wpf.Ui.Controls.ToggleSwitch
                {
                    Margin = new Thickness(16, 0, 0, 0),
                    IsChecked = !Settings.Current.TaskbarHiddenScreens.Contains(device),
                    ToolTip = Loc.T("Mostrar la barra en este monitor"),
                };
                visible.Click += (_, _) =>
                {
                    bool show = visible.IsChecked == true;
                    position.IsEnabled = show;
                    Settings.Update(s =>
                    {
                        s.TaskbarHiddenScreens.Remove(device);
                        if (!show)
                            s.TaskbarHiddenScreens.Add(device);
                    });
                };
                position.IsEnabled = visible.IsChecked == true;
                controls.Children.Add(visible);
            }

            MonitorList.Children.Add(new Wpf.Ui.Controls.CardControl
            {
                Style = (Style)FindResource("HubCard"),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Desktop24 },
                Header = header,
                Content = controls,
            });
        }
    }

    private void PerMonitor_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.TaskbarWindowsPerMonitor = PerMonitorSwitch.IsChecked == true);

    private void Alignment_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        Settings.Update(s => s.TaskbarAlignment =
            AlignmentBox.SelectedIndex == 1 ? TaskbarAlignment.Left : TaskbarAlignment.Center);
    }

    private void Height_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HeightBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        Settings.Update(s => s.TaskbarHeight = int.Parse(tag));
    }

    private void Clock_Changed(object sender, RoutedEventArgs e) => Settings.Update(s =>
    {
        s.ShowSeconds = SecondsSwitch.IsChecked == true;
        s.ShowDate = DateSwitch.IsChecked == true;
    });

    private void Modules_Changed(object sender, RoutedEventArgs e) => Settings.Update(s =>
    {
        s.ShowNetworkModule = NetworkSwitch.IsChecked == true;
        s.ShowBluetoothModule = BluetoothSwitch.IsChecked == true;
        s.ShowVolumeModule = VolumeSwitch.IsChecked == true;
        s.ShowBatteryModule = BatterySwitch.IsChecked == true;
    });
}
