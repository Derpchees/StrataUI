using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Windows.Devices.Radios;
using Strata.Shell.Services;
using Strata.Shell.Services.Network;
using Wpf.Ui.Controls;
using PasswordBox = Strata.Shell.Controls.RevealPasswordBox;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Flyouts;

public partial class NetworkFlyout : FlyoutWindow
{
    private readonly NetworkService _network = new();
    private Radio? _wifiRadio;
    private List<WifiRow> _rows = new();
    /// <summary>La red Wi-Fi conectada: va en la tarjeta de arriba, no en la lista.</summary>
    private WifiRow? _current;

    public NetworkFlyout()
    {
        InitializeComponent();
        UpdateConnection();
        Loaded += async (_, _) => await InitializeWifiAsync();
        // La contraseña no se queda en pantalla para la próxima vez que se abra el panel.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _rows.ForEach(r => r.Key = null);
                SetCurrentExpanded(false);
            }
        };
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
                ShowWifiHint(Loc.T("Este equipo no tiene adaptador Wi-Fi."));
                WifiSwitch.IsEnabled = ScanButton.IsEnabled = false;
                return;
            }
            if (access == WifiAccess.Denied)
            {
                ShowWifiHint(Loc.T("Windows no permite a Strata UI buscar redes. Activa Ubicación en Privacidad › Ubicación (Windows lo exige para escanear Wi-Fi)."));
                return;
            }

            WifiSwitch.IsChecked = _wifiRadio.State == RadioState.On;
            if (_wifiRadio.State == RadioState.On)
                await ScanAsync();
            else
                ShowWifiHint(Loc.T("Wi-Fi desactivado."));
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo inicializar el Wi-Fi", ex);
            ShowWifiHint(Loc.T("No se pudo acceder al Wi-Fi."));
        }
    }

    private void ShowWifiHint(string text)
    {
        WifiHint.Text = text;
        WifiHint.Visibility = Visibility.Visible;
        NetworkList.ItemsSource = null;
        SetCurrent(null);
    }

    private void SetCurrent(WifiRow? row)
    {
        SetCurrentExpanded(false);
        _current = row;
        CurrentPanel.DataContext = row;
        ConnectionButton.IsHitTestVisible = row is not null;
        ConnectionChevron.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetCurrentExpanded(bool expand)
    {
        if (_current is null)
            return;
        _current.IsExpanded = expand;
        // El QR ocupa el lugar de la lista de redes (que se esconde mientras tanto): el panel no crece.
        if (expand && WifiHeader.IsVisible)
        {
            double freed = WifiHeader.ActualHeight + WifiHeader.Margin.Top + WifiHeader.Margin.Bottom
                + NetworkArea.ActualHeight + NetworkArea.Margin.Top + NetworkArea.Margin.Bottom;
            CurrentPanel.MinHeight = Math.Max(0, freed - CurrentPanel.Margin.Bottom);
        }
        WifiHeader.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
        NetworkArea.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
        CurrentPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ConnectionChevron.Symbol = expand ? SymbolRegular.ChevronUp24 : SymbolRegular.ChevronDown24;
    }

    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        bool expand = !_current.IsExpanded;
        _rows.ForEach(r => r.IsExpanded = false);
        SetCurrentExpanded(expand);
        if (expand)
            _ = ShowKeyAsync(_current);
    }

    private async Task ScanAsync()
    {
        WifiHint.Visibility = Visibility.Collapsed;
        ScanProgress.Visibility = Visibility.Visible;
        ScanButton.IsEnabled = false;
        try
        {
            var all = (await _network.ScanAsync()).Select(n => new WifiRow(n)).ToList();
            var current = all.FirstOrDefault(r => r.Network.Connected);
            _rows = all.Where(r => r != current).ToList();
            NetworkList.ItemsSource = _rows;
            SetCurrent(current);
            if (all.Count == 0)
                ShowWifiHint(Loc.T("No se encontraron redes."));
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004004))
        {
            // E_ABORT: el panel se cerró mientras buscaba; no es un error.
        }
        catch (Exception ex)
        {
            Log.Error("Falló la búsqueda de redes", ex);
            ShowWifiHint(Loc.T("No se pudo buscar redes."));
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
            ShowWifiHint(Loc.T("Wi-Fi desactivado."));
        }
        UpdateConnection();
    }

    private void Network_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WifiRow row })
            return;
        bool expand = !row.IsExpanded;
        _rows.ForEach(r => r.IsExpanded = false);
        SetCurrentExpanded(false);
        row.IsExpanded = expand;
        if (expand && sender is FrameworkElement button && button.Parent is FrameworkElement item)
            Dispatcher.BeginInvoke(() => item.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // La lista de redes ocupa a lo más la mitad del alto que cabe en la pantalla (el que FlyoutWindow
    // calcula para el monitor donde se abre, en píxeles lógicos): así el panel mide lo mismo con
    // cualquier resolución, escala o relación de aspecto, y el resto se desplaza.
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == MaxHeightProperty && NetworkArea is not null && !double.IsInfinity(MaxHeight))
            NetworkArea.Height = Math.Clamp(MaxHeight * 0.3, 160, 270); // unas 5 redes a la vista
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

    /// <summary>Mientras se espera el UAC de la contraseña: el aviso de Windows le quita el foco al panel.</summary>
    private bool _readingKey;
    protected override bool StayOpenWhenDeactivated => _readingKey;

    /// <summary>
    /// Claves ya leídas en esta sesión (solo en memoria, por SSID): volver a abrir la red conectada
    /// no pide otro UAC. Nunca se escriben a disco.
    /// </summary>
    private static readonly Dictionary<string, string> KnownKeys = new(StringComparer.Ordinal);

    /// <summary>Al abrir la red conectada: su contraseña y el QR (UAC la primera vez).</summary>
    private async Task ShowKeyAsync(WifiRow row)
    {
        string ssid = row.Ssid;
        if (KnownKeys.TryGetValue(ssid, out var known))
        {
            row.SetKey(known);
            return;
        }
        if (_readingKey)
            return;
        _readingKey = true;
        row.KeyStatus = null;
        row.KeyLoading = true;
        WifiKey.Result result;
        try
        {
            result = await WifiKey.GetAsync();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer la contraseña del Wi-Fi", ex);
            result = new WifiKey.Result(null);
        }
        finally
        {
            _readingKey = false;
            row.KeyLoading = false;
        }
        if (result.Key is { } key)
            KnownKeys[ssid] = key;
        if (!IsVisible || !row.IsExpanded)
            return; // se cerró o se plegó mientras tanto: no dejar la clave en una fila oculta
        Activate();
        if (result.Key is { } shown)
        {
            row.KeyStatus = null;
            row.SetKey(shown);
        }
        else
        {
            row.KeyStatus = result switch
            {
                { Cancelled: true } => Loc.T("Se necesita permiso de administrador para ver la contraseña."),
                { NotConnected: true } => Loc.T("No hay una red Wi-Fi conectada."),
                _ => Loc.T("Windows no entregó la contraseña de esta red."),
            };
        }
    }

    private void CopyKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WifiRow { Key: { Length: > 0 } key } row })
            return;
        try
        {
            Clipboard.SetText(key);
            row.Message = Loc.T("Contraseña copiada.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            row.Message = Loc.T("No se pudo copiar.");
        }
    }

    private async Task ConnectAsync(WifiRow row)
    {
        if (row.IsConnecting)
            return; // Enter dos veces o Enter y clic: un solo intento
        row.IsConnecting = true;
        row.Message = Loc.T("Conectando…");
        WifiConnectResult result;
        try
        {
            result = await _network.ConnectAsync(row.Network, row.PasswordBox?.Password);
        }
        finally
        {
            row.IsConnecting = false;
        }
        switch (result)
        {
            case WifiConnectResult.Connected:
                row.Message = null;
                UpdateConnection();
                await ScanAsync();
                break;
            case WifiConnectResult.NeedsPassword:
                row.NeedsPassword = true;
                row.Message = Loc.T("Escribe la contraseña de la red.");
                row.PasswordBox?.Focus();
                break;
            case WifiConnectResult.WrongPassword:
                row.Message = Loc.T("Contraseña incorrecta.");
                break;
            default:
                row.Message = Loc.T("No se pudo conectar.");
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
        public bool IsConnecting { get; set; }
        public PasswordBox? PasswordBox { get; set; }

        public string Ssid => Network.Ssid;
        public string Detail => Network.Connected ? Loc.T("Conectada") : Network.Secured ? Loc.T("Segura") : Loc.T("Abierta");
        public Visibility LockVisibility => Network.Secured ? Visibility.Visible : Visibility.Collapsed;
        public string ActionText => Network.Connected ? Loc.T("Desconectar") : Loc.T("Conectar");

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
            set
            {
                _expanded = value;
                if (!value)
                    Key = null; // al plegar la fila la clave deja de verse
                Notify(nameof(ExpandedVisibility));
            }
        }

        private string? _key;
        private System.Windows.Media.Geometry? _qr;

        /// <summary>La contraseña guardada, una vez leída (solo en la red conectada); "" = abierta.</summary>
        public string? Key
        {
            get => _key;
            set
            {
                _key = value;
                if (value is null)
                    _qr = null;
                Notify();
                Notify(nameof(KeyText));
                Notify(nameof(Qr));
                Notify(nameof(KeyVisibility));
                Notify(nameof(CopyVisibility));
                Notify(nameof(StatusVisibility));
            }
        }

        /// <summary>Pone la clave y su QR.</summary>
        public void SetKey(string key)
        {
            try
            {
                _qr = WifiQr.Create(Ssid, key);
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo generar el QR del Wi-Fi", ex);
                _qr = null;
            }
            Key = key;
        }

        public string KeyText => _key is { Length: 0 } ? Loc.T("Sin contraseña") : _key ?? "";
        public System.Windows.Media.Geometry? Qr => _qr;
        public Visibility KeyVisibility => _key is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CopyVisibility => _key is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        public bool NeedsPassword
        {
            get => _needsPassword;
            set { _needsPassword = value; Notify(nameof(PasswordVisibility)); }
        }

        private bool _keyLoading;
        private string? _keyStatus;

        /// <summary>Leyendo la contraseña: un círculo de carga en el lugar del QR.</summary>
        public bool KeyLoading
        {
            get => _keyLoading;
            set { _keyLoading = value; Notify(nameof(LoadingVisibility)); Notify(nameof(StatusVisibility)); }
        }

        /// <summary>Por qué no hay contraseña (permiso negado, Windows no la dio…), en el lugar del QR.</summary>
        public string? KeyStatus
        {
            get => _keyStatus;
            set { _keyStatus = value; Notify(); Notify(nameof(StatusVisibility)); }
        }

        public Visibility LoadingVisibility => _keyLoading ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StatusVisibility => !_keyLoading && _key is null && !string.IsNullOrEmpty(_keyStatus) ? Visibility.Visible : Visibility.Collapsed;

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
