using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Control multimedia (--hub Media) del monitor elegido: sus controles en la barra. El elemento
/// mismo se pone o se quita en el modo edición.
/// </summary>
public partial class MediaPage : Page
{
    private bool _loading = true;

    public MediaPage()
    {
        InitializeComponent();
        Load();
        _loading = false;
        ScreenPicker.Changed += OnScreenChanged;
        Settings.Changed += OnScreenChanged;
        Unloaded += (_, _) =>
        {
            ScreenPicker.Changed -= OnScreenChanged;
            Settings.Changed -= OnScreenChanged;
        };
    }

    private void OnScreenChanged()
    {
        _loading = true;
        Load();
        _loading = false;
    }

    private void Load() =>
        MediaControlsSwitch.IsChecked = Settings.Current.For(ScreenPicker.Device).ShowMediaControls;

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            Settings.UpdateScreen(ScreenPicker.Device, s => s.ShowMediaControls = MediaControlsSwitch.IsChecked == true);
    }
}
