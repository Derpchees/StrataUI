using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Strata.Shell.Services;
using Strata.Shell.Services.Bluetooth;
using Wpf.Ui.Controls;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Bluetooth: los emparejados (con su estado de conexión al día) y, con la radio encendida, los
/// cercanos sin emparejar para emparejarlos desde aquí. La búsqueda dura lo que el panel abierto.
/// </summary>
public partial class BluetoothFlyout : FlyoutWindow
{
    private Radio? _radio;
    private List<DeviceRow> _rows = new();
    private readonly ObservableCollection<AvailableRow> _available = new();
    // Lo que informa la búsqueda, por Id (los cambios llegan como parches de propiedades).
    private readonly Dictionary<string, DeviceInformation> _seen = new();
    private DeviceWatcher? _watcher;
    private bool _closed;

    public BluetoothFlyout()
    {
        InitializeComponent();
        AvailableList.ItemsSource = _available;
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            StopScan();
            foreach (var row in _available)
                row.Answer(false, null);
        };
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
            UpdateScan();
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

    private bool RadioOn => _radio?.State == RadioState.On;

    private async Task LoadDevicesAsync()
    {
        Hint.Visibility = Visibility.Collapsed;
        Loading.Visibility = Visibility.Visible;
        var devices = await BluetoothService.GetPairedDevicesAsync();
        bool radioOn = RadioOn;
        _rows = devices.Select(d => new DeviceRow(d, radioOn)).ToList();
        DeviceList.ItemsSource = _rows;
        Loading.Visibility = Visibility.Collapsed;
        if (_rows.Count == 0)
            ShowHint(Loc.T("No hay dispositivos emparejados."));
        else if (!radioOn)
            ShowHint(Loc.T("Bluetooth está desactivado."));
        // Un recién emparejado deja de estar en "disponibles".
        foreach (var row in _available.Where(a => _rows.Any(r => r.Device.Id == a.Id || r.Device.Address == a.Address)).ToList())
            _available.Remove(row);
        UpdateAvailableHint();
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
        UpdateScan();
    }

    // ── Búsqueda de dispositivos cercanos ───────────────────────────────────

    private void UpdateScan()
    {
        if (_closed)
            return;
        if (RadioOn)
            StartScan();
        else
            StopScan();
        AvailableSection.Visibility = RadioOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartScan()
    {
        if (_watcher != null)
            return;
        try
        {
            _watcher = BluetoothService.CreateWatcher();
            _watcher.Added += (_, info) => Dispatcher.BeginInvoke(() => OnAdded(info));
            _watcher.Updated += (_, update) => Dispatcher.BeginInvoke(() => OnUpdated(update));
            _watcher.Removed += (_, update) => Dispatcher.BeginInvoke(() => OnRemoved(update.Id));
            _watcher.EnumerationCompleted += (_, _) => Dispatcher.BeginInvoke(UpdateAvailableHint);
            _watcher.Start();
            Scanning.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth: no se pudo buscar dispositivos", ex);
            _watcher = null;
            Scanning.Visibility = Visibility.Collapsed;
        }
    }

    private void StopScan()
    {
        if (_watcher is null)
            return;
        try
        {
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                _watcher.Stop();
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth: no se pudo detener la búsqueda", ex);
        }
        _watcher = null;
        _seen.Clear();
        _available.Clear();
        Scanning.Visibility = Visibility.Collapsed;
    }

    private void OnAdded(DeviceInformation info)
    {
        if (_closed || _watcher is null)
            return;
        _seen[info.Id] = info;
        Refresh(info);
    }

    private void OnUpdated(DeviceInformationUpdate update)
    {
        if (_closed || !_seen.TryGetValue(update.Id, out var info))
            return;
        bool wasPaired = BluetoothService.Bool(info.Properties, BluetoothService.IsPairedKey) == true;
        info.Update(update);
        bool paired = BluetoothService.Bool(info.Properties, BluetoothService.IsPairedKey) == true;
        if (paired && !wasPaired)
            _ = LoadDevicesAsync(); // se emparejó (aquí o desde Windows)
        else
            Refresh(info);
    }

    private void OnRemoved(string id)
    {
        _seen.Remove(id);
        if (_available.FirstOrDefault(a => a.Id == id) is { IsBusy: false } row)
            _available.Remove(row);
        UpdateAvailableHint();
    }

    /// <summary>Un emparejado: su estado de conexión. Uno sin emparejar y con nombre: en disponibles.</summary>
    private void Refresh(DeviceInformation info)
    {
        ulong address = BluetoothService.AddressOf(info);
        if (BluetoothService.Bool(info.Properties, BluetoothService.IsPairedKey) == true)
        {
            if (BluetoothService.Bool(info.Properties, BluetoothService.IsConnectedKey) is { } connected)
                foreach (var row in _rows.Where(r => r.Device.Id == info.Id))
                    row.Connected = connected;
            // Dispositivos dobles (clásico y LE): conectado si lo está cualquiera de sus extremos.
            if (address != 0)
                foreach (var row in _rows.Where(r => r.Device.Address == address && r.Device.Id != info.Id))
                    row.Connected = _seen.Values.Any(i => BluetoothService.AddressOf(i) == address
                        && BluetoothService.Bool(i.Properties, BluetoothService.IsConnectedKey) == true);
            return;
        }

        var existing = _available.FirstOrDefault(a => a.Id == info.Id);
        // Sin nombre (balizas, teléfonos anunciándose de fondo) no hay forma de saber qué es.
        if (string.IsNullOrWhiteSpace(info.Name) || _rows.Any(r => address != 0 && r.Device.Address == address))
        {
            if (existing is { IsBusy: false })
                _available.Remove(existing);
            UpdateAvailableHint();
            return;
        }
        if (existing != null)
        {
            existing.Name = info.Name;
            return;
        }
        // El mismo dispositivo por clásico y por LE: una sola fila (la primera que llega).
        if (address != 0 && _available.Any(a => a.Address == address))
            return;
        var newRow = new AvailableRow(info.Id, address, info.Name, BluetoothService.KindOf(info));
        int index = 0;
        while (index < _available.Count && string.Compare(_available[index].Name, newRow.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
            index++;
        _available.Insert(index, newRow);
        UpdateAvailableHint();
    }

    private void UpdateAvailableHint()
    {
        AvailableHint.Visibility = _available.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Emparejados ─────────────────────────────────────────────────────────

    private void Device_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceRow row })
            return;
        bool expand = !row.IsExpanded;
        CollapseAll();
        row.IsExpanded = expand;
    }

    private void CollapseAll()
    {
        _rows.ForEach(r => r.IsExpanded = false);
        foreach (var row in _available.Where(a => !a.IsBusy))
            row.IsExpanded = false;
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceRow row })
            return;
        bool connect = !row.Connected;
        row.Status = connect ? Loc.T("Conectando…") : Loc.T("Desconectando…");
        bool ok = await BluetoothService.SetAudioConnectedAsync(row.Device, connect);
        if (!ok)
        {
            row.Status = Loc.T("No se pudo completar la acción");
            return;
        }
        await Task.Delay(connect ? 4000 : 1500); // la conexión de audio tarda unos segundos
        if (!_closed)
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

    // ── Emparejar ───────────────────────────────────────────────────────────

    private void Available_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AvailableRow row } || row.IsBusy)
            return;
        bool expand = !row.IsExpanded;
        CollapseAll();
        row.IsExpanded = expand;
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AvailableRow row })
            return;
        // Responde a lo que pidió el dispositivo (confirmar o escribir el PIN).
        if (row.Waiting)
        {
            row.Answer(true, row.Pin);
            return;
        }
        row.BeginPairing();
        try
        {
            var status = await BluetoothService.PairAsync(row.Id, (prompt, pin) =>
                Dispatcher.InvokeAsync(() => row.Ask(prompt, pin)).Task.Unwrap());
            if (_closed)
                return;
            if (status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
            {
                _available.Remove(row);
                await LoadDevicesAsync();
            }
            else
                row.Fail(status is DevicePairingResultStatus.PairingCanceled or DevicePairingResultStatus.RejectedByHandler
                    ? Loc.T("Emparejamiento cancelado")
                    : status == DevicePairingResultStatus.AuthenticationTimeout
                        ? Loc.T("El dispositivo no respondió a tiempo")
                        : Loc.T("No se pudo emparejar. Comprueba que esté en modo de emparejamiento."));
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth: no se pudo emparejar", ex);
            if (!_closed)
                row.Fail(Loc.T("No se pudo emparejar. Comprueba que esté en modo de emparejamiento."));
        }
    }

    private void PairCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AvailableRow row })
            row.Answer(false, null);
    }

    public abstract class Row : INotifyPropertyChanged
    {
        private bool _expanded;

        public bool IsExpanded
        {
            get => _expanded;
            set { _expanded = value; Notify(nameof(ExpandedVisibility)); }
        }

        public Visibility ExpandedVisibility => _expanded ? Visibility.Visible : Visibility.Collapsed;

        protected static SymbolRegular SymbolOf(BtKind kind) => kind switch
        {
            BtKind.Audio => SymbolRegular.HeadphonesSoundWave24,
            BtKind.Input => SymbolRegular.Keyboard24,
            BtKind.Phone => SymbolRegular.Phone24,
            BtKind.Computer => SymbolRegular.Laptop24,
            _ => SymbolRegular.Bluetooth24,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Notify([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class DeviceRow(BtDevice device, bool radioOn) : Row
    {
        private bool _connected = device.Connected;
        private string _status = StatusOf(device.Connected, device.Battery);

        public BtDevice Device { get; } = device;
        public string Name => Device.Name;
        public SymbolRegular Symbol => SymbolOf(Device.Kind);

        /// <summary>Conectado ahora (lo actualiza la búsqueda mientras el panel está abierto).</summary>
        public bool Connected
        {
            get => _connected;
            set
            {
                if (_connected == value)
                    return;
                _connected = value;
                Status = StatusOf(value, value ? Device.Battery : null);
                Notify();
                Notify(nameof(ActionText));
            }
        }

        private static string StatusOf(bool connected, int? battery) =>
            !connected ? Loc.T("Emparejado, sin conexión")
            : battery is { } b ? Loc.F("Conectado · {0}% de batería", b)
            : Loc.T("Conectado");

        public string ActionText => Connected ? Loc.T("Desconectar") : Loc.T("Conectar");
        // Conectar/desconectar solo está disponible para audio; el resto lo gestiona Windows al usarlos.
        public Visibility ActionVisibility => Device.CanConnect && radioOn ? Visibility.Visible : Visibility.Collapsed;

        public string Status
        {
            get => _status;
            set { _status = value; Notify(); }
        }
    }

    /// <summary>Un dispositivo cercano sin emparejar, y lo que pide mientras se empareja.</summary>
    public sealed class AvailableRow(string id, ulong address, string name, BtKind kind) : Row
    {
        private string _name = name;
        private string _status = "";
        private string _promptText = "";
        private bool _pinVisible;
        private bool _pairing;
        private string _pairText = Loc.T("Emparejar");
        private TaskCompletionSource<(bool, string?)>? _answer;

        public string Id { get; } = id;
        public ulong Address { get; } = address;
        public SymbolRegular Symbol => SymbolOf(kind);
        public string? Pin { get; set; }

        public string Name
        {
            get => _name;
            set { _name = value; Notify(); }
        }

        public string Status
        {
            get => _status;
            private set { _status = value; Notify(); Notify(nameof(StatusVisibility)); }
        }

        public Visibility StatusVisibility => _status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string PromptText
        {
            get => _promptText;
            private set { _promptText = value; Notify(); Notify(nameof(PromptVisibility)); }
        }

        public Visibility PromptVisibility => _promptText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PinVisibility => _pinVisible ? Visibility.Visible : Visibility.Collapsed;
        public string PairText => _pairText;

        /// <summary>Emparejándose: la fila no se cierra ni desaparece si deja de anunciarse.</summary>
        public bool IsBusy => _pairing;

        /// <summary>El dispositivo espera una respuesta (confirmar o el PIN).</summary>
        public bool Waiting => _answer != null;

        public Visibility PairVisibility => !_pairing || Waiting ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AnswerVisibility => Waiting ? Visibility.Visible : Visibility.Collapsed;

        public void BeginPairing()
        {
            _pairing = true;
            Status = Loc.T("Emparejando…");
            PromptText = "";
            SetPin(false);
            Refresh();
        }

        public Task<(bool, string?)> Ask(BtPrompt prompt, string pin)
        {
            IsExpanded = true;
            if (prompt == BtPrompt.DisplayPin)
            {
                // Se escribe en el dispositivo (un teclado): no hay nada que responder aquí.
                PromptText = Loc.F("Escribe {0} en el dispositivo y pulsa Enter.", pin);
                return Task.FromResult<(bool, string?)>((true, null));
            }
            Answer(false, null);
            _answer = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (prompt == BtPrompt.ProvidePin)
            {
                PromptText = Loc.T("Escribe el PIN del dispositivo (muchos usan 0000).");
                Pin = "";
                Notify(nameof(Pin));
                SetPin(true);
                _pairText = Loc.T("Aceptar");
            }
            else
            {
                PromptText = Loc.F("Comprueba que el dispositivo muestra {0}.", pin);
                _pairText = Loc.T("Confirmar");
            }
            Refresh();
            return _answer.Task;
        }

        public void Answer(bool accept, string? pin)
        {
            var answer = _answer;
            _answer = null;
            if (answer is null)
                return;
            PromptText = "";
            SetPin(false);
            Refresh();
            answer.TrySetResult((accept, pin));
        }

        public void Fail(string message)
        {
            _pairing = false;
            _answer = null;
            Status = message;
            PromptText = "";
            SetPin(false);
            Refresh();
        }

        private void SetPin(bool visible)
        {
            _pinVisible = visible;
            Notify(nameof(PinVisibility));
        }

        private void Refresh()
        {
            if (!Waiting)
                _pairText = Loc.T("Emparejar");
            Notify(nameof(PairText));
            Notify(nameof(PairVisibility));
            Notify(nameof(AnswerVisibility));
        }
    }
}
