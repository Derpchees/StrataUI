using System.Windows;
using Strata.Shell.Services.Brightness;

namespace Strata.Shell.Flyouts;

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
            ShowNightLight();
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

    /// <summary>Luz nocturna: solo si Windows tiene su valor (en escritorios remotos puede no estar).</summary>
    private void ShowNightLight()
    {
        bool? on = NightLight.IsOn;
        NightLightRow.Visibility = on is null ? Visibility.Collapsed : Visibility.Visible;
        NightLightSwitch.IsChecked = on == true;
    }

    /// <summary>
    /// Se aplica el estado final un momento después del último clic: varios clics seguidos son un
    /// solo cambio para Windows (que además hace una transición de color en cada uno).
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _nightLightDelay;

    private void NightLight_Click(object sender, RoutedEventArgs e)
    {
        if (_nightLightDelay is null)
        {
            _nightLightDelay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _nightLightDelay.Tick += (_, _) =>
            {
                _nightLightDelay.Stop();
                bool wanted = NightLightSwitch.IsChecked == true;
                if (NightLight.IsOn != wanted && !NightLight.Set(wanted))
                    ShowNightLight(); // no se pudo: vuelve a mostrar el estado real
            };
            // Si el panel se cierra antes de que pase la pausa, el cambio se aplica igual.
            Closed += (_, _) =>
            {
                if (_nightLightDelay.IsEnabled)
                {
                    _nightLightDelay.Stop();
                    bool wanted = NightLightSwitch.IsChecked == true;
                    if (NightLight.IsOn != wanted)
                        NightLight.Set(wanted);
                }
            };
        }
        _nightLightDelay.Stop();
        _nightLightDelay.Start();
    }

    private void NightLightSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseFlyout();
        NightLight.OpenSettings();
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
