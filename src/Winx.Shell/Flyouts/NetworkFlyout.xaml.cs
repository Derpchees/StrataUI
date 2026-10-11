using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Devices.Radios;
using Winx.Shell.Services;
using Winx.Shell.Services.Network;
using Wpf.Ui.Controls;
using PasswordBox = System.Windows.Controls.PasswordBox;

namespace Winx.Shell.Flyouts;

public partial class NetworkFlyout : FlyoutWindow
{
    private readonly NetworkService _network = new();
    private Radio? _wifiRadio;
    private List<WifiRow> _rows = new();

    public NetworkFlyout()
    {
        InitializeComponent();
        UpdateConnection();
        Loaded += async (_, _) => await InitializeWifiAsync();
    }

    private void UpdateConnection()
    {
        var info = NetworkService.GetConnection();
        ConnectionIcon.Symbol = !info.Connected ? SymbolRegular.PlugDisconnected24
            : info.IsWifi ? SymbolRegular.Wifi124
            : SymbolRegular.PlugConnected24;
        ConnectionName.Text = info.Name;
        ConnectionStatus.Text = info.Status;
        ConnectionAddress.Text = info.Address is null ? "" : $"IP: {info.Address}";
        ConnectionAddress.Visibility = info.Address is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task InitializeWifiAsync()
    {
        try
        {
            _wifiRadio = await NetworkService.GetWifiRadioAsync();
            var access = await _network.InitializeWifiAsync();
            if (access == WifiAccess.NoAdapter || _wifiRadio is null)
            {
                ShowWifiHint("Este equipo no tiene adaptador Wi-Fi.");
                WifiSwitch.IsEnabled = ScanButton.IsEnabled = false;
                return;
            }
            if (access == WifiAccess.Denied)
            {
                ShowWifiHint("Windows no permite a WINX buscar redes. Activa Ubicación en Privacidad › Ubicación (Windows lo exige para escanear Wi-Fi).");
                return;
            }

            WifiSwitch.IsChecked = _wifiRadio.State == RadioState.On;
            if (_wifiRadio.State == RadioState.On)
                await ScanAsync();
            else
                ShowWifiHint("Wi-Fi desactivado.");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo inicializar el Wi-Fi", ex);
            ShowWifiHint("No se pudo acceder al Wi-Fi.");
        }
    }

    private void ShowWifiHint(string text)
    {
        WifiHint.Text = text;
        WifiHint.Visibility = Visibility.Visible;
        NetworkList.ItemsSource = null;
    }

    private async Task ScanAsync()
    {
        WifiHint.Visibility = Visibility.Collapsed;
        ScanProgress.Visibility = Visibility.Visible;
        ScanButton.IsEnabled = false;
        try
        {
            _rows = (await _network.ScanAsync()).Select(n => new WifiRow(n)).ToList();
            NetworkList.ItemsSource = _rows;
            if (_rows.Count == 0)
                ShowWifiHint("No se encontraron redes.");
        }
        catch (Exception ex)
        {
            Log.Error("Falló la búsqueda de redes", ex);
            ShowWifiHint("No se pudo buscar redes.");
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async void WifiSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_wifiRadio is null)
            return;
        bool on = WifiSwitch.IsChecked == true;
        WifiSwitch.IsEnabled = false;
        var result = await _wifiRadio.SetStateAsync(on ? RadioState.On : RadioState.Off);
        WifiSwitch.IsEnabled = true;
        if (result != RadioAccessStatus.Allowed)
        {
            WifiSwitch.IsChecked = !on;
            return;
        }
        if (on)
        {
            await Task.Delay(1500); // el adaptador tarda un momento en estar listo
            await ScanAsync();
        }
        else
        {
            ShowWifiHint("Wi-Fi desactivado.");
        }
        UpdateConnection();
    }

    private void Network_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WifiRow row })
            return;
        bool expand = !row.IsExpanded;
        _rows.ForEach(r => r.IsExpanded = false);
        row.IsExpanded = expand;
    }

    private void PasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: WifiRow row } box)
            row.PasswordBox = box;
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { DataContext: WifiRow row })
            _ = ConnectAsync(row);
    }

    private async void NetworkAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WifiRow row })
            return;
        if (row.Network.Connected)
        {
            _network.Disconnect();
            await Task.Delay(1000);
            UpdateConnection();
            await ScanAsync();
        }
        else
        {
            await ConnectAsync(row);
        }
    }

    private async Task ConnectAsync(WifiRow row)
    {
        row.Message = "Conectando…";
        var result = await _network.ConnectAsync(row.Network, row.PasswordBox?.Password);
        switch (result)
        {
            case WifiConnectResult.Connected:
                UpdateConnection();
                await ScanAsync();
                break;
            case WifiConnectResult.NeedsPassword:
                row.NeedsPassword = true;
                row.Message = "Escribe la contraseña de la red.";
                row.PasswordBox?.Focus();
                break;
            case WifiConnectResult.WrongPassword:
                row.Message = "Contraseña incorrecta.";
                break;
            default:
                row.Message = "No se pudo conectar.";
                break;
        }
    }

    /// <summary>Fila de una red Wi-Fi, expandible para conectar.</summary>
    public sealed class WifiRow(WifiNetwork network) : INotifyPropertyChanged
    {
        private bool _expanded;
        private bool _needsPassword;
        private string? _message;

        public WifiNetwork Network { get; } = network;
        public PasswordBox? PasswordBox { get; set; }

        public string Ssid => Network.Ssid;
        public string Detail => Network.Connected ? "Conectada" : Network.Secured ? "Segura" : "Abierta";
        public Visibility LockVisibility => Network.Secured ? Visibility.Visible : Visibility.Collapsed;
        public string ActionText => Network.Connected ? "Desconectar" : "Conectar";

        public SymbolRegular SignalSymbol => Network.Bars switch
        {
            >= 4 => SymbolRegular.Wifi124,
            3 => SymbolRegular.Wifi224,
            2 => SymbolRegular.Wifi324,
            _ => SymbolRegular.Wifi424,
        };

        public bool IsExpanded
        {
            get => _expanded;
            set { _expanded = value; Notify(nameof(ExpandedVisibility)); }
        }

        public bool NeedsPassword
        {
            get => _needsPassword;
            set { _needsPassword = value; Notify(nameof(PasswordVisibility)); }
        }

        public string? Message
        {
            get => _message;
            set { _message = value; Notify(); Notify(nameof(MessageVisibility)); }
        }

        public Visibility ExpandedVisibility => _expanded ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PasswordVisibility => _needsPassword ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MessageVisibility => string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
