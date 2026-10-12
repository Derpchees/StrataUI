using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Winx.Shell.Controls;
using Winx.Shell.Services;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Hub.Pages;

/// <summary>Bandeja del sistema: qué íconos se ven siempre y cuáles van a los ocultos (--hub Tray).</summary>
public partial class TrayPage : Page
{
    /// <summary>Fila de la lista: un ícono de la bandeja con nombre legible.</summary>
    public sealed record TrayIconItem(ShellNotifyIcon Icon, string Name, string Detail)
    {
        public ImageSource? Image => Icon.Icon;
        public bool IsPinned => Icon.IsPinned;
    }

    private readonly NotifyCollectionChangedEventHandler _onIconsChanged;

    public TrayPage()
    {
        InitializeComponent();
        _onIconsChanged = (_, _) => Dispatcher.BeginInvoke(Refresh);
        Loaded += (_, _) =>
        {
            App.Current.Shell.NotificationArea.TrayIcons.CollectionChanged += _onIconsChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
            App.Current.Shell.NotificationArea.TrayIcons.CollectionChanged -= _onIconsChanged;
    }

    private void Refresh()
    {
        var items = App.Current.Shell.NotificationArea.TrayIcons
            .Where(icon => !TrayIcon.IsReplacedByWinx(icon))
            .Select(ToItem)
            .OrderByDescending(item => item.IsPinned)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        IconList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TrayIconItem ToItem(ShellNotifyIcon icon)
    {
        string? description = null;
        if (!string.IsNullOrEmpty(icon.Path) && File.Exists(icon.Path))
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(icon.Path).FileDescription;
            }
            catch (IOException)
            {
            }
        }

        string fileName = string.IsNullOrEmpty(icon.Path) ? "" : Path.GetFileName(icon.Path);
        string title = icon.Title?.Split('\n')[0].Trim() ?? "";
        string name = FirstNonEmpty(description, title, fileName, Loc.T("Ícono sin nombre"));
        string detail = title != name && title.Length > 0 ? title : fileName;
        return new TrayIconItem(icon, name, detail);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(v => !string.IsNullOrWhiteSpace(v))!;

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: TrayIconItem item } toggle)
            TrayPromotion.SetPinned(item.Icon, toggle.IsChecked == true);
    }
}
