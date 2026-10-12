using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Winx.Shell.Services.Notifications;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Hub.Pages;

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
        BuildScreenBox();
        if (FindName(s.NotificationCorner.ToString()) is RadioButton corner)
            corner.IsChecked = true;
        UpdateCornerText();
        UpdateState();
        _loading = false;

        NotificationService.Changed += UpdateState;
        Unloaded += (_, _) => NotificationService.Changed -= UpdateState;
    }

    private void UpdateState()
    {
        Options.IsEnabled = Settings.Current.NotificationsEnabled;
        Options.Opacity = Options.IsEnabled ? 1 : 0.5;
        DeniedBar.IsOpen = Settings.Current.NotificationsEnabled && NotificationService.AccessDenied;
    }

    private void BuildScreenBox()
    {
        var screens = Forms.Screen.AllScreens.OrderBy(sc => sc.Bounds.X).ThenBy(sc => sc.Bounds.Y).ToList();
        ScreenBox.Items.Add(new ComboBoxItem { Content = Loc.T("El principal de WINX"), Tag = null });
        for (int i = 0; i < screens.Count; i++)
        {
            string name = screens.Count == 1 ? Loc.T("Monitor")
                : screens[i].Primary ? Loc.F("Monitor {0} (principal de Windows)", i + 1)
                : Loc.F("Monitor {0}", i + 1);
            ScreenBox.Items.Add(new ComboBoxItem { Content = name, Tag = screens[i].DeviceName });
        }
        string? chosen = Settings.Current.NotificationScreen;
        ScreenBox.SelectedItem = ScreenBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == chosen)
                                 ?? ScreenBox.Items[0];
    }

    private void UpdateCornerText() => CornerText.Text = Settings.Current.NotificationCorner switch
    {
        NotificationCorner.TopLeft => Loc.T("Arriba a la izquierda"),
        NotificationCorner.TopRight => Loc.T("Arriba a la derecha"),
        NotificationCorner.BottomLeft => Loc.T("Abajo a la izquierda"),
        _ => Loc.T("Abajo a la derecha"),
    };

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

    private void Screen_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ScreenBox.SelectedItem is ComboBoxItem item)
            Settings.Update(s => s.NotificationScreen = (string?)item.Tag);
    }

    private void Corner_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag })
            return;
        Settings.Update(s => s.NotificationCorner = Enum.Parse<NotificationCorner>(tag));
        UpdateCornerText();
    }

    private void Test_Click(object sender, RoutedEventArgs e) => NotificationService.ShowTest();
}
