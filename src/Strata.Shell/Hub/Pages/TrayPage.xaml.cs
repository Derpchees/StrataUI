using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Strata.Shell.Controls;
using Strata.Shell.Services;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

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
        LoadScreen();
        Loaded += (_, _) =>
        {
            App.Current.Shell.NotificationArea.TrayIcons.CollectionChanged += _onIconsChanged;
            ScreenPicker.Changed += LoadScreen;
            Settings.Changed += LoadScreen;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            App.Current.Shell.NotificationArea.TrayIcons.CollectionChanged -= _onIconsChanged;
            ScreenPicker.Changed -= LoadScreen;
            Settings.Changed -= LoadScreen;
        };
    }

    // ── En qué monitor va ───────────────────────────────────────────────────

    private void LoadScreen()
    {
        var bars = App.Current.Taskbars.Where(t => t.Device == ScreenPicker.Device).ToList();
        bool here = bars.Any(t => t.IsMain);
        HereSwitch.IsChecked = here;
        // Va en una sola barra: se mueve activándola en otro monitor.
        HereSwitch.IsEnabled = !here && bars.Count > 0;
        HereText.Text = here ? Loc.T("Aquí está. Para moverla, actívala en otro monitor.")
            : bars.Count == 0 ? Loc.T("Este monitor no tiene barra.")
            : Loc.T("Al activarla, la bandeja pasa a la barra de este monitor (con la tecla Windows y lo demás de la barra principal).");
    }

    private void Here_Click(object sender, RoutedEventArgs e)
    {
        var bars = App.Current.Taskbars.Where(t => t.Device == ScreenPicker.Device).ToList();
        var target = bars.FirstOrDefault(t => t.Has(BarItem.Clock)) ?? bars.FirstOrDefault();
        if (HereSwitch.IsChecked != true || target is null)
            return;
        var layout = App.Current.Taskbars.ToDictionary(t => t.BarId, t => t.Items.ToList());
        foreach (var items in layout.Values)
            items.Remove(BarItem.Tray);
        var list = layout[target.BarId];
        int clock = list.IndexOf(BarItem.Clock);
        list.Insert(clock >= 0 ? clock : list.Count, BarItem.Tray);
        Settings.Update(s =>
        {
            foreach (var (id, items) in layout)
                s.BarItems[id] = items;
        });
    }

    private void Refresh()
    {
        var items = App.Current.Shell.NotificationArea.TrayIcons
            .Where(icon => !TrayIcon.IsReplacedByStrata(icon))
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
