using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;

namespace Strata.Shell.Hub.Pages;

/// <summary>Paneles (--hub Flyouts): qué avisos en pantalla (OSD) muestra el monitor elegido.</summary>
public partial class FlyoutsPage : Page
{
    private bool _loading = true;

    public FlyoutsPage()
    {
        InitializeComponent();
        Load();
        _loading = false;
        ScreenPicker.Changed += OnScreenChanged;
        Unloaded += (_, _) => ScreenPicker.Changed -= OnScreenChanged;
    }

    private void OnScreenChanged()
    {
        _loading = true;
        Load();
        _loading = false;
    }

    private void Load()
    {
        var s = Settings.Current.For(ScreenPicker.Device);
        VolumeSwitch.IsChecked = s.OsdVolume;
        MuteSwitch.IsChecked = s.OsdMute;
        BrightnessSwitch.IsChecked = s.OsdBrightness;
        CapsSwitch.IsChecked = s.OsdCapsLock;
        NumSwitch.IsChecked = s.OsdNumLock;
        ScrollSwitch.IsChecked = s.OsdScrollLock;
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.UpdateScreen(ScreenPicker.Device, s =>
        {
            s.OsdVolume = VolumeSwitch.IsChecked == true;
            s.OsdMute = MuteSwitch.IsChecked == true;
            s.OsdBrightness = BrightnessSwitch.IsChecked == true;
            s.OsdCapsLock = CapsSwitch.IsChecked == true;
            s.OsdNumLock = NumSwitch.IsChecked == true;
            s.OsdScrollLock = ScrollSwitch.IsChecked == true;
        });
    }
}
