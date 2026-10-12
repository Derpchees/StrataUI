using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;

namespace Winx.Shell.Hub.Pages;

/// <summary>Paneles (--hub Flyouts): opciones de los paneles de sonido, brillo, red, Bluetooth y batería.</summary>
public partial class FlyoutsPage : Page
{
    private readonly bool _loading = true;

    public FlyoutsPage()
    {
        InitializeComponent();
        OsdSwitch.IsChecked = Settings.Current.ShowOsd;
        BatteryPercentSwitch.IsChecked = Settings.Current.ShowBatteryPercent;
        _loading = false;
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.Update(s =>
        {
            s.ShowOsd = OsdSwitch.IsChecked == true;
            s.ShowBatteryPercent = BatteryPercentSwitch.IsChecked == true;
        });
    }
}
