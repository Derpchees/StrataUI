using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Notifications;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Notificaciones: dónde salen los avisos (se elige en el modo edición), una prueba, y el centro de
/// notificaciones y los avisos para todos.
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
        UpdateCornerText();
        UpdateState();
        _loading = false;

        NotificationService.Changed += UpdateState;
        Settings.Changed += UpdateCornerText;
        Unloaded += (_, _) =>
        {
            NotificationService.Changed -= UpdateState;
            Settings.Changed -= UpdateCornerText;
        };
    }

    /// <summary>Monitor donde salen ahora los avisos (el elegido o, si no está conectado, el principal de Strata UI).</summary>
    private static string BannerDevice()
    {
        var chosen = Settings.Current.NotificationScreen;
        return chosen != null && Forms.Screen.AllScreens.Any(s => s.DeviceName == chosen) ? chosen : ThemeManager.MainDevice;
    }

    private void EditLayout_Click(object sender, RoutedEventArgs e) => EditMode.EditSession.Start();

    private void UpdateState()
    {
        Options.IsEnabled = ScreenOptions.IsEnabled = Settings.Current.NotificationsEnabled;
        Options.Opacity = ScreenOptions.Opacity = Options.IsEnabled ? 1 : 0.5;
        DeniedBar.IsOpen = Settings.Current.NotificationsEnabled && NotificationService.AccessDenied;
    }

    /// <summary>"Monitor 2 · Abajo a la derecha": dónde salen ahora (se cambia arrastrando el aviso de ejemplo).</summary>
    private void UpdateCornerText()
    {
        string device = BannerDevice();
        var screens = ScreenPicker.Screens();
        int index = screens.FindIndex(s => s.DeviceName == device);
        string corner = Settings.Current.For(device).NotificationCorner switch
        {
            NotificationCorner.TopLeft => Loc.T("Arriba a la izquierda"),
            NotificationCorner.TopRight => Loc.T("Arriba a la derecha"),
            NotificationCorner.BottomLeft => Loc.T("Abajo a la izquierda"),
            NotificationCorner.TopCenter => Loc.T("Arriba al centro"),
            NotificationCorner.BottomCenter => Loc.T("Abajo al centro"),
            _ => Loc.T("Abajo a la derecha"),
        };
        CornerText.Text = (index >= 0 && screens.Count > 1 ? ScreenPicker.NameOf(screens[index], index, screens.Count) + " · " : "") + corner
                          + "\n" + Loc.T("En el modo edición, arrastra el aviso de ejemplo a otra esquina o a otro monitor.");
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
