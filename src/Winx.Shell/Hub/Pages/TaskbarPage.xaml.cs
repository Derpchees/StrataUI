using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;

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

        _loading = false;
    }

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
