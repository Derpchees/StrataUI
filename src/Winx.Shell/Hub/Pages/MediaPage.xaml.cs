using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;

namespace Winx.Shell.Hub.Pages;

/// <summary>Control multimedia (--hub Media): el módulo en la barra y sus controles.</summary>
public partial class MediaPage : Page
{
    private readonly bool _loading = true;

    public MediaPage()
    {
        InitializeComponent();
        MediaSwitch.IsChecked = AnyBarHas(BarItem.Media);
        MediaControlsSwitch.IsChecked = Settings.Current.ShowMediaControls;
        _loading = false;
    }

    private static bool AnyBarHas(BarItem item) => App.Current.Taskbars.Any(t => t.Has(item));

    /// <summary>
    /// Agrega multimedia a las barras con el reloj (o a la principal) o lo quita de todas; dónde va
    /// exactamente se elige en Barra de tareas › Qué lleva cada barra.
    /// </summary>
    private void Media_Click(object sender, RoutedEventArgs e)
    {
        bool on = MediaSwitch.IsChecked == true;
        var bars = App.Current.Taskbars.ToList();
        var targets = bars.Where(t => t.Has(BarItem.Clock)).ToList();
        if (targets.Count == 0)
            targets = bars.Where(t => t.IsMain).ToList();
        Settings.Update(s =>
        {
            foreach (var bar in bars)
            {
                var items = bar.Items.ToList();
                items.Remove(BarItem.Media);
                if (on && targets.Contains(bar))
                    items.Insert(items.Contains(BarItem.Start) ? 1 : 0, BarItem.Media);
                s.BarItems[bar.BarId] = items;
            }
        });
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            Settings.Update(s => s.ShowMediaControls = MediaControlsSwitch.IsChecked == true);
    }
}
