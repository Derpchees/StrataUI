using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Barra de tareas: las apps abiertas del monitor elegido (<see cref="ScreenPicker"/>) y en qué barras
/// aparecen y Alt+Tab. El diseño de las barras se arma en el modo edición,
/// que se abre desde el menú de la barra, del escritorio o del ícono de la bandeja.
/// </summary>
public partial class TaskbarPage : Page
{
    private bool _loading = true;

    public TaskbarPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        Select(AppsModeBox, s.TaskbarAppsMode.ToString());
        AppsModeCard.Visibility = ScreenPicker.Screens().Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        AltTabSwitch.IsChecked = s.AltTabEnabled;
        AltTabMonitorSwitch.IsChecked = s.AltTabPerMonitor;
        AltTabMonitorCard.IsEnabled = s.AltTabEnabled;
        LoadScreen();
        _loading = false;
        Loaded += (_, _) =>
        {
            ScreenPicker.Changed += OnScreenChanged;
        };
        Unloaded += (_, _) =>
        {
            ScreenPicker.Changed -= OnScreenChanged;
        };
    }

    private static string Device => ScreenPicker.Device;

    private void OnScreenChanged()
    {
        _loading = true;
        LoadScreen();
        _loading = false;
    }

    /// <summary>Las opciones de las apps abiertas del monitor elegido.</summary>
    private void LoadScreen()
    {
        var s = Settings.Current.For(Device);
        PreviewsSwitch.IsChecked = s.ShowTaskPreviews;
        CombineSwitch.IsChecked = s.CombineTaskWindows;
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.UpdateScreen(Device, s =>
        {
            s.ShowTaskPreviews = PreviewsSwitch.IsChecked == true;
            s.CombineTaskWindows = CombineSwitch.IsChecked == true;
        });
    }

    // ── Para todos los monitores ────────────────────────────────────────────

    private void AppsMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AppsModeBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.Update(s => s.TaskbarAppsMode = Enum.Parse<TaskbarAppsMode>(tag));
    }

    private void AltTab_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.Update(s =>
        {
            s.AltTabEnabled = AltTabSwitch.IsChecked == true;
            s.AltTabPerMonitor = AltTabMonitorSwitch.IsChecked == true;
        });
        AltTabMonitorCard.IsEnabled = AltTabSwitch.IsChecked == true;
    }
}
