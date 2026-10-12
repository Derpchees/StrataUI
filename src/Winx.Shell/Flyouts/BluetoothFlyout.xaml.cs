using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Windows.Devices.Radios;
using Winx.Shell.Services;
using Winx.Shell.Services.Bluetooth;
using Wpf.Ui.Controls;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Flyouts;

public partial class BluetoothFlyout : FlyoutWindow
{
    private Radio? _radio;
    private List<DeviceRow> _rows = new();

    public BluetoothFlyout()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _radio = await BluetoothService.GetRadioAsync();
            if (_radio is null)
            {
                RadioSwitch.IsEnabled = false;
                ShowHint(Loc.T("Este equipo no tiene Bluetooth."));
                return;
            }
            RadioSwitch.IsChecked = _radio.State == RadioState.On;
            await LoadDevicesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo inicializar Bluetooth", ex);
            ShowHint(Loc.T("No se pudo acceder a Bluetooth."));
        }
    }

    private void ShowHint(string text)
    {
        Loading.Visibility = Visibility.Collapsed;
        Hint.Text = text;
        Hint.Visibility = Visibility.Visible;
    }

    private async Task LoadDevicesAsync()
    {
        Hint.Visibility = Visibility.Collapsed;
        Loading.Visibility = Visibility.Visible;
        var devices = await BluetoothService.GetPairedDevicesAsync();
        bool radioOn = _radio?.State == RadioState.On;
        _rows = devices.Select(d => new DeviceRow(d, radioOn)).ToList();
        DeviceList.ItemsSource = _rows;
        Loading.Visibility = Visibility.Collapsed;
        if (_rows.Count == 0)
            ShowHint(Loc.T("No hay dispositivos emparejados."));
        else if (!radioOn)
            ShowHint(Loc.T("Bluetooth está desactivado."));
    }

    private async void RadioSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_radio is null)
            return;
        bool on = RadioSwitch.IsChecked == true;
        RadioSwitch.IsEnabled = false;
        var result = await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
        RadioSwitch.IsEnabled = true;
        if (result != RadioAccessStatus.Allowed)
            RadioSwitch.IsChecked = !on;
        await LoadDevicesAsync();
    }

    private void Device_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceRow row })
            return;
        bool expand = !row.IsExpanded;
        _rows.ForEach(r => r.IsExpanded = false);
        row.IsExpanded = expand;
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceRow row })
            return;
        bool connect = !row.Device.Connected;
        row.Status = connect ? Loc.T("Conectando…") : Loc.T("Desconectando…");
        bool ok = await BluetoothService.SetAudioConnectedAsync(row.Device, connect);
        if (!ok)
        {
            row.Status = Loc.T("No se pudo completar la acción");
            return;
        }
        await Task.Delay(connect ? 4000 : 1500); // la conexión de audio tarda unos segundos
        await LoadDevicesAsync();
    }

    private async void Unpair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceRow row })
            return;
        row.Status = Loc.T("Quitando…");
        if (await BluetoothService.UnpairAsync(row.Device))
            await LoadDevicesAsync();
        else
            row.Status = Loc.T("No se pudo quitar el dispositivo");
    }

    public sealed class DeviceRow(BtDevice device, bool radioOn) : INotifyPropertyChanged
    {
        private bool _expanded;
        private string _status = device.Connected ? Loc.T("Conectado") : Loc.T("Emparejado");

        public BtDevice Device { get; } = device;
        public string Name => Device.Name;

        public SymbolRegular Symbol => Device.Kind switch
        {
            BtKind.Audio => SymbolRegular.HeadphonesSoundWave24,
            BtKind.Input => SymbolRegular.Keyboard24,
            BtKind.Phone => SymbolRegular.Phone24,
            BtKind.Computer => SymbolRegular.Laptop24,
            _ => SymbolRegular.Bluetooth24,
        };

        public string ActionText => Device.Connected ? Loc.T("Desconectar") : Loc.T("Conectar");
        // Conectar/desconectar solo está disponible para audio; el resto lo gestiona Windows al usarlos.
        public Visibility ActionVisibility => Device.CanConnect && radioOn ? Visibility.Visible : Visibility.Collapsed;

        public string Status
        {
            get => _status;
            set { _status = value; Notify(); }
        }

        public bool IsExpanded
        {
            get => _expanded;
            set { _expanded = value; Notify(nameof(ExpandedVisibility)); }
        }

        public Visibility ExpandedVisibility => _expanded ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
