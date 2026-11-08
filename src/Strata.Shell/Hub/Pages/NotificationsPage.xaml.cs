using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Notifications;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Notificaciones: una prueba de dónde salen los avisos, y el centro de notificaciones y los avisos
/// para todos.
/// </summary>
public partial class NotificationsPage : Page
{
    private bool _loading = true;

    public NotificationsPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        EnabledSwitch.IsChecked = s.NotificationsEnabled;
        BannersSwitch.IsChecked = s.NotificationBanners;
        SoundsSwitch.IsChecked = s.NotificationSounds;
        MuteWindowsSoundsSwitch.IsChecked = s.MuteWindowsNotificationSounds;
        DndSwitch.IsChecked = s.NotificationDoNotDisturb;
        DurationBox.SelectedItem = DurationBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == s.NotificationSeconds.ToString()) ?? DurationBox.Items[1];
        UpdateState();
        _loading = false;

        NotificationService.Changed += UpdateState;
        Unloaded += (_, _) =>
        {
            NotificationService.Changed -= UpdateState;
        };
    }

    private void UpdateState()
    {
        Options.IsEnabled = ScreenOptions.IsEnabled = Settings.Current.NotificationsEnabled;
        Options.Opacity = ScreenOptions.Opacity = Options.IsEnabled ? 1 : 0.5;
        DeniedBar.IsOpen = Settings.Current.NotificationsEnabled && NotificationService.AccessDenied;
    }

    private void Enabled_Click(object sender, RoutedEventArgs e)
    {
        Settings.Update(s => s.NotificationsEnabled = EnabledSwitch.IsChecked == true);
        UpdateState();
    }

    private void Banners_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationBanners = BannersSwitch.IsChecked == true);

    private void Sounds_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationSounds = SoundsSwitch.IsChecked == true);

    private void MuteWindowsSounds_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.MuteWindowsNotificationSounds = MuteWindowsSoundsSwitch.IsChecked == true);

    private void Dnd_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationDoNotDisturb = DndSwitch.IsChecked == true);

    private void Duration_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && DurationBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.Update(s => s.NotificationSeconds = int.Parse(tag));
    }

    private void Test_Click(object sender, RoutedEventArgs e) => NotificationService.ShowTest();
}
