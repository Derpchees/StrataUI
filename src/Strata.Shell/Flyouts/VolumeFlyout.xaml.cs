using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Audio;
using Wpf.Ui.Controls;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Flyouts;

/// <summary>
/// Panel de sonido: volumen y dispositivos de salida y de entrada (mosaicos con las opciones de
/// Windows de cada uno) y volumen por aplicación.
/// </summary>
public partial class VolumeFlyout : FlyoutWindow
{
    private readonly AudioService _audio = App.Current.Audio;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _levelTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private List<AppRow> _rows = new();
    private DeviceOptionsModel? _options;
    private bool _updating;

    public VolumeFlyout()
    {
        InitializeComponent();
        _audio.Output.Changed += UpdateVolumes;
        _audio.Input.Changed += UpdateVolumes;
        _audio.DevicesChanged += UpdateDevices;
        AudioMirror.Changed += UpdateMirror;
        _refreshTimer.Tick += (_, _) => RefreshApps();
        _levelTimer.Tick += (_, _) => _options?.UpdateLevel();
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _audio.Output.Changed -= UpdateVolumes;
            _audio.Input.Changed -= UpdateVolumes;
            _audio.DevicesChanged -= UpdateDevices;
            AudioMirror.Changed -= UpdateMirror;
            CloseOptions();
            DisposeRows();
        };

        UpdateVolumes();
        UpdateDevices();
        RefreshApps();
        _refreshTimer.Start();
    }

    protected override bool StayOpenWhenDeactivated => _options?.IsBusy == true;

    // ── Volumen general ─────────────────────────────────────────────────────

    private void UpdateVolumes()
    {
        _updating = true;
        MasterSlider.Value = Math.Round(_audio.Output.Volume * 100);
        InputSlider.Value = Math.Round(_audio.Input.Volume * 100);
        _updating = false;
        MasterPercent.Text = $"{MasterSlider.Value:0}";
        InputPercent.Text = $"{InputSlider.Value:0}";
        MasterMuteIcon.Symbol = _audio.Output.Mute ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;
        InputMuteIcon.Symbol = _audio.Input.Mute ? SymbolRegular.MicOff24 : SymbolRegular.Mic24;
        MasterSlider.IsEnabled = MasterMuteButton.IsEnabled = _audio.Output.HasDevice;
        InputSlider.IsEnabled = InputMuteButton.IsEnabled = _audio.Input.HasDevice;
    }

    private void MasterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        OnSlider(_audio.Output, MasterPercent, e.NewValue);

    private void InputSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        OnSlider(_audio.Input, InputPercent, e.NewValue);

    private void OnSlider(AudioEndpoint endpoint, System.Windows.Controls.TextBlock percent, double value)
    {
        percent.Text = $"{value:0}";
        if (_updating)
            return;
        endpoint.Volume = (float)(value / 100);
        if (endpoint.Mute && value > 0)
            endpoint.Mute = false;
    }

    private void MasterMute_Click(object sender, RoutedEventArgs e) => _audio.Output.Mute = !_audio.Output.Mute;
    private void InputMute_Click(object sender, RoutedEventArgs e) => _audio.Input.Mute = !_audio.Input.Mute;

    // ── Dispositivos ────────────────────────────────────────────────────────

    private void UpdateDevices()
    {
        var outputs = Tiles(_audio.GetDevices(input: false));
        var inputs = Tiles(_audio.GetDevices(input: true));
        OutputList.ItemsSource = outputs;
        InputList.ItemsSource = inputs;
        NoInputText.Visibility = inputs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowCurrent(outputs, OutputDeviceText, OutputDeviceIcon);
        ShowCurrent(inputs, InputDeviceText, InputDeviceIcon);
        UpdateMirror();

        // Las opciones abiertas siguen al dispositivo (cambia la marca de "llamadas"); si se
        // desconectó, se cierran.
        if (_options != null)
        {
            var current = outputs.Concat(inputs).FirstOrDefault(t => t.Device.Id == _options.Device.Id);
            if (current is null)
                CloseOptions();
            else
                _options.Refresh(current.Device);
        }
    }

    /// <summary>La fila plegada muestra el dispositivo en uso con su ícono.</summary>
    private static void ShowCurrent(List<DeviceTile> tiles, System.Windows.Controls.TextBlock text, SymbolIcon icon)
    {
        var current = tiles.FirstOrDefault(t => t.IsDefault);
        text.Text = current?.Name ?? Loc.T("Sin dispositivo");
        if (current != null)
            icon.Symbol = current.Symbol;
    }

    // ── Sonar también en ────────────────────────────────────────────────────

    /// <summary>Una salida que puede repetir el sonido (todas menos la principal).</summary>
    public sealed record MirrorOption(AudioDevice Device, SymbolRegular Symbol, bool IsSelected)
    {
        public string Name => Device.Name;
        public string Adapter => Device.Adapter;
    }

    private bool _updatingMirror;

    private void UpdateMirror()
    {
        string? chosen = Settings.Current.AudioMirrorDevice;
        var outputs = _audio.GetDevices(input: false);
        var options = outputs.Where(d => !d.IsDefault)
            .Select(d => new MirrorOption(d, new DeviceTile(d, false).Symbol, d.Id == chosen)).ToList();
        MirrorList.ItemsSource = options;
        NoMirrorText.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MirrorSwitch.IsChecked = chosen != null;
        var device = outputs.FirstOrDefault(d => d.Id == chosen);
        MirrorText.Text = chosen is null ? Loc.T("Apagado")
            : device is null ? Loc.T("La salida elegida no está conectada")
            : device.IsDefault ? Loc.T("En pausa: es la salida principal")
            : device.Name;
        bool active = AudioMirror.ActiveTarget is { } target && target == chosen;
        MirrorVolumeRow.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active && AudioMirror.GetVolume(chosen!) is { } volume)
        {
            _updatingMirror = true;
            MirrorSlider.Value = Math.Round(volume * 100);
            _updatingMirror = false;
            MirrorPercent.Text = $"{MirrorSlider.Value:0}";
        }
    }

    private void MirrorHeader_Click(object sender, RoutedEventArgs e) =>
        MirrorPanel.Visibility = MirrorPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Encender elige la salida de antes o, la primera vez, la de un monitor (lo más común: que suenen
    /// los dos monitores) o cualquier otra; y despliega la lista para cambiarla.
    /// </summary>
    private void MirrorSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (MirrorSwitch.IsChecked != true)
        {
            Settings.Update(s => s.AudioMirrorDevice = null);
            UpdateMirror();
            return;
        }
        var others = _audio.GetDevices(input: false).Where(d => !d.IsDefault).ToList();
        var pick = others.FirstOrDefault(d => d.Kind == AudioDeviceKind.Display) ?? others.FirstOrDefault();
        MirrorPanel.Visibility = Visibility.Visible;
        if (pick is null)
        {
            MirrorSwitch.IsChecked = false;
            return;
        }
        Settings.Update(s => s.AudioMirrorDevice = pick.Id);
        UpdateMirror();
    }

    private void MirrorDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MirrorOption option })
        {
            Settings.Update(s => s.AudioMirrorDevice = option.Device.Id);
            UpdateMirror();
        }
    }

    private void MirrorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MirrorPercent.Text = $"{e.NewValue:0}";
        if (!_updatingMirror && AudioMirror.ActiveTarget is { } target)
            AudioMirror.SetVolume(target, (float)(e.NewValue / 100));
    }

    private void DevicesHeader_Click(object sender, RoutedEventArgs e)
    {
        var (panel, chevron) = ReferenceEquals(sender, OutputHeader)
            ? (OutputDevices, OutputChevron)
            : (InputDevices, InputChevron);
        bool expand = panel.Visibility != Visibility.Visible;
        panel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        if (chevron.RenderTransform is RotateTransform rotate)
            rotate.Angle = expand ? 180 : 0;
        // Al plegar, las opciones abiertas de ese grupo se cierran (y el micrófono deja de medirse).
        if (!expand && _options?.Device.IsInput == ReferenceEquals(panel, InputDevices))
            CloseOptions();
    }

    /// <summary>
    /// Nombres genéricos repetidos ("Speakers" de dos apps virtuales) no distinguen nada: en
    /// ese caso el título es el hardware y el nombre queda debajo.
    /// </summary>
    private static List<DeviceTile> Tiles(IReadOnlyList<AudioDevice> devices)
    {
        var repeated = devices.GroupBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                              .Where(g => g.Count() > 1).Select(g => g.Key)
                              .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return devices.Select(d => new DeviceTile(d, swapNames: repeated.Contains(d.Name))).ToList();
    }

    private void Device_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceTile tile } || tile.IsDefault)
            return;
        try
        {
            _audio.SetDefaultDevice(tile.Device.Id);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo cambiar al dispositivo {tile.Name}", ex);
        }
    }

    private void DeviceOptions_Click(object sender, RoutedEventArgs e)
    {
        // Click sube por el árbol: sin esto, el mosaico de debajo también cambiaría el predeterminado.
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: DeviceTile tile })
            return;
        bool sameDevice = _options?.Device.Id == tile.Device.Id;
        CloseOptions();
        if (sameDevice)
            return; // el mismo botón abre y cierra

        _options = new DeviceOptionsModel(_audio, tile.Device);
        _options.WriteFinished += () => Activate(); // el diálogo de UAC se llevó el foco
        var host = tile.Device.IsInput ? InputOptions : OutputOptions;
        host.Content = _options;
        host.Visibility = Visibility.Visible;
        _levelTimer.Start();
    }

    private void CloseOptions_Click(object sender, RoutedEventArgs e) => CloseOptions();

    private void CloseOptions()
    {
        _levelTimer.Stop();
        _options?.Dispose();
        _options = null;
        foreach (var host in new[] { OutputOptions, InputOptions })
        {
            host.Content = null;
            host.Visibility = Visibility.Collapsed;
        }
    }

    private void Communications_Click(object sender, RoutedEventArgs e)
    {
        if (_options is null)
            return;
        try
        {
            _audio.SetDefaultDevice(_options.Device.Id, communicationsOnly: true);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo usar {_options.Device.Name} para llamadas", ex);
        }
    }

    /// <summary>Mosaico de un dispositivo.</summary>
    public sealed class DeviceTile(AudioDevice device, bool swapNames)
    {
        public AudioDevice Device { get; } = device;
        public string Name => swapNames ? Device.Adapter : Device.Name;
        public string Adapter => swapNames ? Device.Name : Device.Adapter;
        public bool IsDefault => Device.IsDefault;
        public string ToolTip => $"{Device.Name}\n{Device.Adapter}";

        public SymbolRegular Symbol => Device.Kind switch
        {
            AudioDeviceKind.Headphones => SymbolRegular.Headphones24,
            AudioDeviceKind.Headset => SymbolRegular.HeadphonesSoundWave24,
            AudioDeviceKind.Display => SymbolRegular.Desktop24,
            AudioDeviceKind.Digital => SymbolRegular.PlugConnected24,
            AudioDeviceKind.Bluetooth => SymbolRegular.Bluetooth24,
            AudioDeviceKind.Microphone => SymbolRegular.Mic24,
            AudioDeviceKind.Speakers => SymbolRegular.Speaker224,
            _ => SymbolRegular.SpeakerSettings24,
        };
    }

    /// <summary>
    /// Opciones de un dispositivo: nivel en vivo, balance, mejoras de audio, modo exclusivo y uso
    /// para llamadas. Cada cambio se aplica al momento; si Windows lo rechaza, se registra.
    /// </summary>
    public sealed class DeviceOptionsModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AudioService _audio;
        private readonly LevelMonitor? _monitor;
        private double _balance;
        private double _level;

        public DeviceOptionsModel(AudioService audio, AudioDevice device)
        {
            _audio = audio;
            Device = device;
            try
            {
                _monitor = audio.MonitorLevel(device.Id, device.IsInput);
                _balance = device.IsInput ? 0 : (audio.GetBalance(device.Id) ?? double.NaN) * 100;
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudieron leer las opciones de {device.Name}", ex);
                _balance = double.NaN;
            }
        }

        public AudioDevice Device { get; private set; }
        public string Name => Device.Name;
        public string Adapter => Device.Adapter;

        public void Refresh(AudioDevice device)
        {
            Device = device;
            OnPropertyChanged(nameof(CommunicationsText));
            OnPropertyChanged(nameof(CanSetCommunications));
        }

        // Nivel
        public string LevelLabel => Device.IsInput ? Loc.T("Nivel del micrófono (habla para probarlo)") : Loc.T("Nivel de salida");
        public double Level => _level;

        public void UpdateLevel()
        {
            double level = (_monitor?.Peak ?? 0) * 100;
            // Sube al instante y baja suave, como los medidores de Windows.
            _level = level >= _level ? level : Math.Max(level, _level - 4);
            OnPropertyChanged(nameof(Level));
        }

        // Balance
        public Visibility BalanceVisibility => double.IsNaN(_balance) ? Visibility.Collapsed : Visibility.Visible;
        // Los pasos de volumen del hardware dejan diferencias pequeñas entre canales: menos de 3 % es centrado.
        public string BalanceText => Math.Abs(_balance) < 3 ? Loc.T("Centrado")
                                   : _balance < 0 ? Loc.F("Izquierda {0:0} %", -_balance) : Loc.F("Derecha {0:0} %", _balance);

        public double Balance
        {
            get => double.IsNaN(_balance) ? 0 : _balance;
            set
            {
                // Cerca del centro se ajusta a cero para poder volver a centrarlo fácil.
                _balance = Math.Abs(value) < 4 ? 0 : value;
                Try(() => _audio.SetBalance(Device.Id, _balance / 100), "el balance");
                OnPropertyChanged();
                OnPropertyChanged(nameof(BalanceText));
            }
        }

        // Mejoras de audio y modo exclusivo: se escriben elevando (UAC). Mientras tanto los
        // interruptores quedan bloqueados; al terminar se relee el valor real (si se canceló o
        // falló, el interruptor vuelve a donde estaba).
        private bool _busy;
        public bool NotBusy => !_busy;
        public bool IsBusy => _busy;

        /// <summary>Terminó una escritura elevada (para devolver el foco al panel).</summary>
        public event Action? WriteFinished;

        public Visibility EnhancementsVisibility =>
            AudioDeviceOptions.GetEnhancements(Device) is null ? Visibility.Collapsed : Visibility.Visible;

        public bool Enhancements
        {
            get => AudioDeviceOptions.GetEnhancements(Device) ?? false;
            set => Write(AudioDeviceOptions.SetEnhancementsAsync(Device, value), nameof(Enhancements));
        }

        public bool ExclusiveAllowed
        {
            get => AudioDeviceOptions.GetExclusiveAllowed(Device);
            set => Write(AudioDeviceOptions.SetExclusiveAllowedAsync(Device, value), nameof(ExclusiveAllowed), nameof(PriorityVisibility));
        }

        public Visibility PriorityVisibility => ExclusiveAllowed ? Visibility.Visible : Visibility.Collapsed;

        public bool ExclusivePriority
        {
            get => AudioDeviceOptions.GetExclusivePriority(Device);
            set => Write(AudioDeviceOptions.SetExclusivePriorityAsync(Device, value), nameof(ExclusivePriority));
        }

        private async void Write(Task<bool> write, params string[] properties)
        {
            _busy = true;
            OnPropertyChanged(nameof(NotBusy));
            try
            {
                await write;
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo cambiar una opción de {Device.Name}", ex);
            }
            _busy = false;
            OnPropertyChanged(nameof(NotBusy));
            foreach (var property in properties)
                OnPropertyChanged(property);
            WriteFinished?.Invoke();
        }

        // Llamadas
        public bool CanSetCommunications => !Device.IsDefaultCommunications;
        public string CommunicationsText => Device.IsDefaultCommunications
            ? Loc.T("Es el predeterminado para llamadas")
            : Loc.T("Usar para llamadas (Discord, Teams…)");

        private void Try(Action write, string what)
        {
            try
            {
                write();
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo cambiar {what} de {Device.Name}", ex);
            }
        }

        public void Dispose() => _monitor?.Dispose();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Aplicaciones ────────────────────────────────────────────────────────

    /// <summary>Reconstruye la lista solo si cambiaron las apps, para no interrumpir un arrastre.</summary>
    private void RefreshApps()
    {
        var apps = _audio.GetApps();
        AppsSummaryText.Text = apps.Count switch
        {
            0 => Loc.T("Ninguna usando el audio"),
            1 => Loc.T("1 aplicación"),
            _ => Loc.F("{0} aplicaciones", apps.Count),
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

    private void AppsHeader_Click(object sender, RoutedEventArgs e)
    {
        bool expand = AppsPanel.Visibility != Visibility.Visible;
        AppsPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        if (AppsChevron.RenderTransform is RotateTransform rotate)
            rotate.Angle = expand ? 180 : 0;
        DisposeRows(); // fuerza a reconstruir con el estado actual al expandir
        AppList.ItemsSource = null;
        RefreshApps();
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
