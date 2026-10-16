using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Strata.Shell.Services;
using Strata.Shell.Services.Notifications;

namespace Strata.Shell.Flyouts;

/// <summary>Centro de notificaciones de Strata UI (la campana): solo notificaciones y "No molestar".</summary>
public partial class NotificationFlyout : FlyoutWindow
{
    public NotificationFlyout()
    {
        InitializeComponent();
        NotificationList.ItemsSource = NotificationService.Items;
        DndSwitch.IsChecked = Settings.Current.NotificationDoNotDisturb;
        NotificationService.Changed += Refresh;
        Closed += (_, _) => NotificationService.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        bool any = NotificationService.Items.Count > 0;
        EmptyText.Visibility = any || NotificationService.AccessDenied ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        DeniedPanel.Visibility = NotificationService.AccessDenied ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInsideButton(d))
            return;
        if (sender is FrameworkElement { DataContext: StrataNotification item })
        {
            CloseFlyout();
            NotificationService.Activate(item);
        }
    }

    private static bool IsInsideButton(DependencyObject d)
    {
        for (var current = d; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Button)
                return true;
        return false;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StrataNotification item })
            NotificationService.Dismiss(item);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => NotificationService.ClearAll();

    private void Dnd_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationDoNotDisturb = DndSwitch.IsChecked == true);

    private void Privacy_Click(object sender, RoutedEventArgs e)
    {
        CloseFlyout();
        Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
    }
}
