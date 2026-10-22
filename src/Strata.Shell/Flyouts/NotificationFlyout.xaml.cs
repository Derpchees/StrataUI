using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Strata.Shell.Services;
using Strata.Shell.Services.Notifications;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Centro de notificaciones de Strata UI (la campana): las notificaciones agrupadas por app y "No
/// molestar". Un grupo con varias se ve plegado (la más reciente con la pila detrás) hasta que se
/// despliega; queda desplegado mientras el panel siga abierto.
/// </summary>
public partial class NotificationFlyout : FlyoutWindow
{
    private readonly HashSet<string> _expanded = new();
    private List<NotificationGroup> _groups = [];

    public NotificationFlyout()
    {
        InitializeComponent();
        DndSwitch.IsChecked = Settings.Current.NotificationDoNotDisturb;
        NotificationService.Changed += Refresh;
        Closed += (_, _) => NotificationService.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        _groups = NotificationGroup.Build(NotificationService.Items, _expanded);
        NotificationList.ItemsSource = _groups;
        bool any = NotificationService.Items.Count > 0;
        EmptyText.Visibility = any || NotificationService.AccessDenied ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        DeniedPanel.Visibility = NotificationService.AccessDenied ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && (Controls.RichText.IsInsideLink(d) || d is System.Windows.Media.Visual && IsInsideButton(d)))
            return;
        if (sender is not FrameworkElement { DataContext: StrataNotification item })
            return;
        // Un clic en la pila la despliega, como en el celular; ya desplegada, abre la notificación.
        if (_groups.FirstOrDefault(g => g.Items.Contains(item)) is { IsStacked: true } group)
        {
            SetExpanded(group, true);
            return;
        }
        CloseFlyout();
        NotificationService.Activate(item);
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationGroup group })
            SetExpanded(group, !group.IsExpanded);
    }

    private void SetExpanded(NotificationGroup group, bool expanded)
    {
        if (expanded)
            _expanded.Add(group.Key);
        else
            _expanded.Remove(group.Key);
        group.IsExpanded = expanded;
    }

    private void DismissGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationGroup group })
            NotificationService.Dismiss(group.Items);
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
