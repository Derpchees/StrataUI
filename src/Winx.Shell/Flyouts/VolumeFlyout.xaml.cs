using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Winx.Shell.Services;
using Winx.Shell.Services.Audio;
using Wpf.Ui.Controls;

namespace Winx.Shell.Flyouts;

public partial class VolumeFlyout : FlyoutWindow
{
    private readonly AudioService _audio = App.Current.Audio;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private List<AppRow> _rows = new();
    private bool _updatingMaster;

    public VolumeFlyout()
    {
        InitializeComponent();
        _audio.Changed += UpdateMaster;
        _audio.DevicesChanged += UpdateDevices;
        _refreshTimer.Tick += (_, _) => RefreshApps();
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _audio.Changed -= UpdateMaster;
            _audio.DevicesChanged -= UpdateDevices;
            DisposeRows();
        };

        UpdateMaster();
        UpdateDevices();
        RefreshApps();
        _refreshTimer.Start();
    }

    private void UpdateMaster()
    {
        _updatingMaster = true;
        MasterSlider.Value = Math.Round(_audio.Volume * 100);
        _updatingMaster = false;
        MasterPercent.Text = $"{MasterSlider.Value:0}";
        MasterMuteIcon.Symbol = _audio.Mute ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;
        MasterSlider.IsEnabled = MasterMuteButton.IsEnabled = _audio.HasDevice;
    }

    private void UpdateDevices()
    {
        var devices = _audio.GetOutputDevices();
        DeviceList.ItemsSource = devices;
        CurrentDeviceText.Text = devices.FirstOrDefault(d => d.IsDefault)?.Name ?? "Sin dispositivo";
    }

    /// <summary>Reconstruye la lista solo si cambiaron las apps, para no interrumpir un arrastre.</summary>
    private void RefreshApps()
    {
        var apps = _audio.GetApps();
        AppsSummaryText.Text = apps.Count switch
        {
            0 => "Ninguna usando el audio",
            1 => "1 aplicación",
            _ => $"{apps.Count} aplicaciones",
        };
        bool sameApps = apps.Select(a => a.Key).SequenceEqual(_rows.Select(r => r.App.Key));
        if (AppsPanel.Visibility != Visibility.Visible || sameApps)
        {
            // Solo se necesitaba el resumen o la lista no cambió: se liberan las sesiones nuevas
            // y se refrescan los valores de las filas existentes.
            foreach (var app in apps)
                app.Dispose();
            if (sameApps)
                foreach (var row in _rows)
                    row.Sync();
            return;
        }
        DisposeRows();
        _rows = apps.Select(a => new AppRow(a)).ToList();
        AppList.ItemsSource = _rows;
        NoAppsText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DisposeRows()
    {
        foreach (var row in _rows)
            row.App.Dispose();
        _rows.Clear();
    }

    private static void ToggleSection(FrameworkElement section, UIElement chevron)
    {
        bool expand = section.Visibility != Visibility.Visible;
        section.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        if (chevron.RenderTransform is RotateTransform rotate)
            rotate.Angle = expand ? 180 : 0;
    }

    private void DevicesHeader_Click(object sender, RoutedEventArgs e) => ToggleSection(DeviceList, DevicesChevron);

    private void AppsHeader_Click(object sender, RoutedEventArgs e)
    {
        ToggleSection(AppsPanel, AppsChevron);
        DisposeRows(); // fuerza a reconstruir con el estado actual al expandir
        AppList.ItemsSource = null;
        RefreshApps();
    }

    private void MasterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MasterPercent.Text = $"{e.NewValue:0}";
        if (_updatingMaster)
            return;
        _audio.Volume = (float)(e.NewValue / 100);
        if (_audio.Mute && e.NewValue > 0)
            _audio.Mute = false;
    }

    private void MasterMute_Click(object sender, RoutedEventArgs e) => _audio.Mute = !_audio.Mute;

    private void Device_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AudioDevice device } || device.IsDefault)
            return;
        try
        {
            _audio.SetDefaultDevice(device.Id);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo cambiar al dispositivo {device.Name}", ex);
        }
    }

    /// <summary>Fila de volumen de una aplicación.</summary>
    public sealed class AppRow : INotifyPropertyChanged
    {
        private double _volume;
        private bool _mute;

        public AppRow(AudioApp app)
        {
            App = app;
            _volume = Math.Round(app.Volume * 100);
            _mute = app.Mute;
            ToggleMute = new RelayCommand(() =>
            {
                App.Mute = !App.Mute;
                Sync();
            });
        }

        public AudioApp App { get; }
        public string Name => App.Name;
        public ImageSource? Icon => App.Icon;
        public ICommand ToggleMute { get; }

        // El ícono de la app hace de botón de silencio: atenuado y con indicador cuando está silenciada.
        public double IconOpacity => _mute ? 0.4 : 1;
        public Visibility MutedVisibility => _mute ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackIconVisibility => App.Icon is null ? Visibility.Visible : Visibility.Collapsed;

        private void NotifyMute()
        {
            OnPropertyChanged(nameof(IconOpacity));
            OnPropertyChanged(nameof(MutedVisibility));
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (Math.Abs(_volume - value) < 0.5)
                    return;
                _volume = value;
                App.Volume = (float)(value / 100);
                if (_mute && value > 0)
                {
                    App.Mute = false;
                    _mute = false;
                    NotifyMute();
                }
                OnPropertyChanged();
            }
        }

        /// <summary>Relee el estado (por si otra app, como el mezclador de Windows, lo cambió).</summary>
        public void Sync()
        {
            try
            {
                _volume = Math.Round(App.Volume * 100);
                _mute = App.Mute;
            }
            catch (Exception)
            {
                return; // la sesión desapareció; la próxima actualización la quitará
            }
            OnPropertyChanged(nameof(Volume));
            NotifyMute();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
