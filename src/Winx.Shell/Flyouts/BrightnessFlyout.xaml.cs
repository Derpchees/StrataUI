using System.Windows;
using Winx.Shell.Services.Brightness;

namespace Winx.Shell.Flyouts;

/// <summary>Brillo de cada monitor y, con varios, de todos a la vez.</summary>
public partial class BrightnessFlyout : FlyoutWindow
{
    private bool _syncing;

    public BrightnessFlyout()
    {
        InitializeComponent();
        MonitorList.ItemsSource = BrightnessService.Monitors;
        // "Todos" muestra el promedio también cuando se mueve un monitor solo.
        foreach (var monitor in BrightnessService.Monitors)
            monitor.PropertyChanged += OnMonitorChanged;
        Closed += (_, _) =>
        {
            foreach (var monitor in BrightnessService.Monitors)
                monitor.PropertyChanged -= OnMonitorChanged;
        };
        Loaded += async (_, _) =>
        {
            if (!BrightnessService.Available)
            {
                Loading.Visibility = Visibility.Visible;
                await BrightnessService.RefreshAsync();
                Loading.Visibility = Visibility.Collapsed;
            }
            UpdateLayoutState();
        };
    }

    private void UpdateLayoutState()
    {
        var monitors = BrightnessService.Monitors;
        EmptyText.Visibility = monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AllRow.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (monitors.Count > 1)
        {
            _syncing = true;
            AllSlider.Value = Math.Round(monitors.Average(m => m.Percent));
            AllPercent.Text = ((int)AllSlider.Value).ToString();
            _syncing = false;
        }
    }

    private void OnMonitorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_syncing)
            UpdateLayoutState();
    }

    private void AllSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        AllPercent.Text = ((int)e.NewValue).ToString();
        if (_syncing)
            return;
        _syncing = true;
        foreach (var monitor in BrightnessService.Monitors)
            monitor.Percent = (int)Math.Round(e.NewValue);
        _syncing = false;
    }
}
