using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Notifications;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Notificaciones: en el monitor elegido arriba, si los avisos salen ahí y en qué esquina; abajo,
/// el centro de notificaciones y los avisos para todos.
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
        DndSwitch.IsChecked = s.NotificationDoNotDisturb;
        DurationBox.SelectedItem = DurationBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == s.NotificationSeconds.ToString()) ?? DurationBox.Items[1];
        LoadScreen();
        UpdateState();
        _loading = false;

        NotificationService.Changed += UpdateState;
        ScreenPicker.Changed += OnScreenChanged;
        Unloaded += (_, _) =>
        {
            NotificationService.Changed -= UpdateState;
            ScreenPicker.Changed -= OnScreenChanged;
        };
    }

    private static string Device => ScreenPicker.Device;

    /// <summary>Monitor donde salen ahora los avisos (el elegido o, si no está conectado, el principal de Strata UI).</summary>
    private static string BannerDevice()
    {
        var chosen = Settings.Current.NotificationScreen;
        return chosen != null && Forms.Screen.AllScreens.Any(s => s.DeviceName == chosen) ? chosen : ThemeManager.MainDevice;
    }

    private void OnScreenChanged()
    {
        _loading = true;
        LoadScreen();
        _loading = false;
    }

    private void LoadScreen()
    {
        bool here = BannerDevice() == Device;
        HereSwitch.IsChecked = here;
        // Salen en un solo monitor: se mueven activándolo en otro.
        HereSwitch.IsEnabled = !here;
        if (FindName(Settings.Current.For(Device).NotificationCorner.ToString()) is RadioButton corner)
            corner.IsChecked = true;
        UpdateCornerText();
    }

    private void UpdateState()
    {
        Options.IsEnabled = ScreenOptions.IsEnabled = Settings.Current.NotificationsEnabled;
        Options.Opacity = ScreenOptions.Opacity = Options.IsEnabled ? 1 : 0.5;
        DeniedBar.IsOpen = Settings.Current.NotificationsEnabled && NotificationService.AccessDenied;
    }

    private void UpdateCornerText() => CornerText.Text = Settings.Current.For(Device).NotificationCorner switch
    {
        NotificationCorner.TopLeft => Loc.T("Arriba a la izquierda"),
        NotificationCorner.TopRight => Loc.T("Arriba a la derecha"),
        NotificationCorner.BottomLeft => Loc.T("Abajo a la izquierda"),
        _ => Loc.T("Abajo a la derecha"),
    };

    private void Here_Click(object sender, RoutedEventArgs e)
    {
        if (HereSwitch.IsChecked == true)
            Settings.Update(s => s.NotificationScreen = Device);
        OnScreenChanged();
    }

    private void Enabled_Click(object sender, RoutedEventArgs e)
    {
        Settings.Update(s => s.NotificationsEnabled = EnabledSwitch.IsChecked == true);
        UpdateState();
    }

    private void Banners_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationBanners = BannersSwitch.IsChecked == true);

    private void Dnd_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationDoNotDisturb = DndSwitch.IsChecked == true);

    private void Duration_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && DurationBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.Update(s => s.NotificationSeconds = int.Parse(tag));
    }

    private void Corner_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
            return;
        Settings.UpdateScreen(Device, s => s.NotificationCorner = Enum.Parse<NotificationCorner>(tag));
        UpdateCornerText();
    }

    private void Test_Click(object sender, RoutedEventArgs e) => NotificationService.ShowTest();
}
