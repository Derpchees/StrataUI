using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;

namespace Strata.Shell.Hub.Pages;

/// <summary>Control multimedia (--hub Media) del monitor elegido: el módulo en su barra y sus controles.</summary>
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

    private static List<Taskbar> Bars() =>
        App.Current.Taskbars.Where(t => t.Device == ScreenPicker.Device).ToList();

    private void OnScreenChanged()
    {
        _loading = true;
        Load();
        _loading = false;
    }

    private void Load()
    {
        var bars = Bars();
        MediaSwitch.IsEnabled = bars.Count > 0;
        MediaSwitch.IsChecked = bars.Any(t => t.Has(BarItem.Media));
        MediaControlsSwitch.IsChecked = Settings.Current.For(ScreenPicker.Device).ShowMediaControls;
    }

    /// <summary>
    /// Agrega multimedia a la barra de este monitor (la del reloj, o la primera) o lo quita de sus
    /// barras; su lugar en la barra se cambia arrastrándolo.
    /// </summary>
    private void Media_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        bool on = MediaSwitch.IsChecked == true;
        var layout = App.Current.Taskbars.ToDictionary(t => t.BarId, t => t.Items.ToList());
        var bars = Bars();
        foreach (var bar in bars)
            layout[bar.BarId].Remove(BarItem.Media);
        if (on && (bars.FirstOrDefault(t => t.Has(BarItem.Clock)) ?? bars.FirstOrDefault()) is { } target)
        {
            var items = layout[target.BarId];
            items.Insert(items.Contains(BarItem.Start) ? 1 : 0, BarItem.Media);
        }
        Settings.Update(s =>
        {
            foreach (var (id, list) in layout)
                s.BarItems[id] = list;
        });
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            Settings.UpdateScreen(ScreenPicker.Device, s => s.ShowMediaControls = MediaControlsSwitch.IsChecked == true);
    }
}
