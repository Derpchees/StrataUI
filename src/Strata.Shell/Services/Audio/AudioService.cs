using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Audio;

public enum AudioDeviceKind { Speakers, Headphones, Headset, Display, Digital, Bluetooth, Microphone, Other }

/// <param name="Name">Nombre del dispositivo ("Altavoces", "MONITOR R").</param>
/// <param name="Adapter">Hardware al que pertenece ("NVIDIA High Definition Audio").</param>
public sealed record AudioDevice(string Id, string Name, string Adapter, AudioDeviceKind Kind, bool IsInput,
                                 bool IsDefault, bool IsDefaultCommunications);

/// <summary>
/// Una aplicación con audio; agrupa todas las sesiones del mismo ejecutable.
/// Envuelve objetos COM: hay que liberarla cuando deja de usarse.
/// </summary>
public sealed class AudioApp : IDisposable
{
    public void Dispose() => Sessions.ForEach(s => s.Dispose());

    internal AudioApp(string key, string name, ImageSource? icon, List<AudioSessionControl> sessions)
    {
        Key = key;
        Name = name;
        Icon = icon;
        Sessions = sessions;
    }

    public string Key { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    internal List<AudioSessionControl> Sessions { get; }

    public float Volume
    {
        get => Sessions.Max(s => s.SimpleAudioVolume.Volume);
        set => Sessions.ForEach(s => s.SimpleAudioVolume.Volume = value);
    }

    public bool Mute
    {
        get => Sessions.All(s => s.SimpleAudioVolume.Mute);
        set => Sessions.ForEach(s => s.SimpleAudioVolume.Mute = value);
    }
}

/// <summary>Volumen y silencio del dispositivo predeterminado de salida o de entrada.</summary>
public sealed class AudioEndpoint : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly DataFlow _flow;
    private MMDevice? _device;

    /// <summary>Cambió el volumen, el silencio o el dispositivo predeterminado.</summary>
    public event Action? Changed;

    internal AudioEndpoint(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        _enumerator = enumerator;
        _flow = flow;
        Attach();
    }

    public bool HasDevice => _device != null;
    public string? DeviceId => _device?.ID;
    internal MMDevice? Device => _device;

    public float Volume
    {
        get => _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0;
        set
        {
            if (_device != null)
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
        }
    }

    public bool Mute
    {
        get => _device?.AudioEndpointVolume.Mute ?? true;
        set
        {
            if (_device != null)
                _device.AudioEndpointVolume.Mute = value;
        }
    }

    /// <summary>Lo que se reconecta cuando cambia el predeterminado.</summary>
    internal void Attach()
    {
        Detach();
        if (_enumerator.HasDefaultAudioEndpoint(_flow, Role.Multimedia))
        {
            _device = _enumerator.GetDefaultAudioEndpoint(_flow, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
        }
    }

    internal void RaiseChanged() => Changed?.Invoke();

    private void Detach()
    {
        if (_device is null)
            return;
        _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        _device.Dispose();
        _device = null;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data) =>
        Application.Current?.Dispatcher.BeginInvoke(RaiseChanged);

    public void Dispose() => Detach();
}

/// <summary>
/// Acceso a Core Audio: dispositivos de salida y entrada, sus opciones, y volumen por aplicación.
/// Los objetos COM se usan solo desde el hilo de la UI; las notificaciones se re-despachan a él.
/// </summary>
public sealed class AudioService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public AudioService()
    {
        Output = new AudioEndpoint(_enumerator, DataFlow.Render);
        Input = new AudioEndpoint(_enumerator, DataFlow.Capture);
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public AudioEndpoint Output { get; }
    public AudioEndpoint Input { get; }

    /// <summary>Cambió el volumen, el silencio o el dispositivo de salida predeterminado.</summary>
    public event Action? Changed
    {
        add => Output.Changed += value;
        remove => Output.Changed -= value;
    }

    /// <summary>Se agregó, quitó o cambió algún dispositivo.</summary>
    public event Action? DevicesChanged;

    public bool HasDevice => Output.HasDevice;
    public float Volume { get => Output.Volume; set => Output.Volume = value; }
    public bool Mute { get => Output.Mute; set => Output.Mute = value; }

    // ── Dispositivos ────────────────────────────────────────────────────────

    private static readonly PropertyKey FormFactorKey = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
    private static readonly PropertyKey DeviceDescKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly PropertyKey InstancePathKey = new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

    public IReadOnlyList<AudioDevice> GetDevices(bool input)
    {
        var flow = input ? DataFlow.Capture : DataFlow.Render;
        string? defaultId = DefaultId(flow, Role.Multimedia);
        string? communicationsId = DefaultId(flow, Role.Communications);
        var devices = new List<AudioDevice>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                var props = device.Properties;
                string name = Read(props, DeviceDescKey) as string ?? device.FriendlyName;
                string path = Read(props, InstancePathKey) as string ?? "";
                var kind = KindOf(Read(props, FormFactorKey) is uint f ? (int)f : -1, path, input);
                devices.Add(new AudioDevice(device.ID, name, device.DeviceFriendlyName, kind, input,
                                            device.ID == defaultId, device.ID == communicationsId));
            }
        }
        return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Tipo de dispositivo según su forma (EndpointFormFactor) y si es Bluetooth.</summary>
    internal static AudioDeviceKind KindOf(int formFactor, string instancePath, bool input)
    {
        if (instancePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || instancePath.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase)
            || instancePath.Contains("BTHLEDEVICE", StringComparison.OrdinalIgnoreCase))
            return AudioDeviceKind.Bluetooth;
        return formFactor switch
        {
            1 => AudioDeviceKind.Speakers,
            3 => AudioDeviceKind.Headphones,
            5 or 6 => AudioDeviceKind.Headset,
            9 => AudioDeviceKind.Display,
            7 or 8 => AudioDeviceKind.Digital,
            4 => AudioDeviceKind.Microphone,
            _ => input ? AudioDeviceKind.Microphone : AudioDeviceKind.Other,
        };
    }

    private static object? Read(PropertyStore props, PropertyKey key)
    {
        try
        {
            return props.Contains(key) ? props[key].Value : null;
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidCastException or ArgumentException)
        {
            return null; // tipo de PROPVARIANT que NAudio no sabe leer
        }
    }

    private string? DefaultId(DataFlow flow, Role role)
    {
        if (!_enumerator.HasDefaultAudioEndpoint(flow, role))
            return null;
        using var device = _enumerator.GetDefaultAudioEndpoint(flow, role);
        return device.ID;
    }

    /// <summary>Predeterminado para todo, o solo para llamadas (rol de comunicaciones).</summary>
    public void SetDefaultDevice(string id, bool communicationsOnly = false) =>
        PolicyConfig.SetDefaultEndpoint(id, communicationsOnly ? PolicyConfig.Roles.Communications : PolicyConfig.Roles.All);

    // ── Opciones de un dispositivo ──────────────────────────────────────────

    /// <summary>
    /// Balance izquierda/derecha de -1 (solo izquierda) a 1 (solo derecha); null si el
    /// dispositivo no tiene al menos dos canales.
    /// </summary>
    public double? GetBalance(string id)
    {
        using var device = _enumerator.GetDevice(id);
        var channels = device.AudioEndpointVolume.Channels;
        if (channels.Count < 2)
            return null;
        float left = channels[0].VolumeLevelScalar, right = channels[1].VolumeLevelScalar;
        if (left <= 0 && right <= 0)
            return 0;
        return right >= left ? 1 - left / right : -(1 - right / left);
    }

    /// <summary>Atenúa un canal y deja el otro al nivel del volumen general.</summary>
    public void SetBalance(string id, double balance)
    {
        using var device = _enumerator.GetDevice(id);
        var volume = device.AudioEndpointVolume;
        if (volume.Channels.Count < 2)
            return;
        float level = Math.Max(volume.Channels[0].VolumeLevelScalar, volume.Channels[1].VolumeLevelScalar);
        balance = Math.Clamp(balance, -1, 1);
        volume.Channels[0].VolumeLevelScalar = (float)(level * (balance > 0 ? 1 - balance : 1));
        volume.Channels[1].VolumeLevelScalar = (float)(level * (balance < 0 ? 1 + balance : 1));
    }

    /// <summary>
    /// Mide el nivel de un dispositivo mientras está abierto. En micrófonos abre una captura
    /// (Windows solo mide la entrada si alguien la está usando); en salidas basta con leer el medidor.
    /// </summary>
    public LevelMonitor MonitorLevel(string id, bool input) => new(_enumerator.GetDevice(id), input);

    // ── Aplicaciones ────────────────────────────────────────────────────────

    public IReadOnlyList<AudioApp> GetApps()
    {
        // El dispositivo abierto del predeterminado: sus sesiones viven mientras él viva.
        if (Output.Device is not { } device)
            return [];

        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        var groups = new Dictionary<string, (string Name, ImageSource? Icon, List<AudioSessionControl> Sessions)>();

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateExpired)
            {
                session.Dispose();
                continue;
            }

            var (key, name, icon) = Describe(session);
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = (name, icon, new List<AudioSessionControl>());
            group.Sessions.Add(session);
        }

        return groups
            .Select(g => new AudioApp(g.Key, g.Value.Name, g.Value.Icon, g.Value.Sessions))
            .OrderBy(a => a.Key == "system" ? 1 : 0)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static (string Key, string Name, ImageSource? Icon) Describe(AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession)
            return ("system", Loc.T("Sonidos del sistema"), null);

        try
        {
            using var process = Process.GetProcessById((int)session.GetProcessID);
            string? path = null;
            string name = process.ProcessName;
            try
            {
                path = process.MainModule?.FileName;
                if (path != null)
                {
                    var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description))
                        name = description;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Procesos protegidos o de otra arquitectura: nos quedamos con el nombre del proceso.
            }
            return (path ?? process.ProcessName, name, path is null ? null : IconFromFile(path));
        }
        catch (ArgumentException)
        {
            // El proceso ya terminó.
            var display = string.IsNullOrWhiteSpace(session.DisplayName) ? Loc.T("Aplicación") : session.DisplayName;
            return (session.GetSessionInstanceIdentifier, display, null);
        }
    }

    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private static ImageSource? IconFromFile(string path)
    {
        if (IconCache.TryGetValue(path, out var cached))
            return cached;
        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
        }
        IconCache[path] = image;
        return image;
    }

    // ── Notificaciones ──────────────────────────────────────────────────────

    private void OnDevicesChanged(DataFlow? defaultChanged) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (defaultChanged == DataFlow.Render)
                Output.Attach();
            else if (defaultChanged == DataFlow.Capture)
                Input.Attach();
            DevicesChanged?.Invoke();
            Output.RaiseChanged();
            Input.RaiseChanged();
        });

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => OnDevicesChanged(null);
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => OnDevicesChanged(null);
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => OnDevicesChanged(null);
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // El de comunicaciones no cambia el volumen que se muestra, pero sí la marca en la lista.
        OnDevicesChanged(role == Role.Multimedia ? flow : null);
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        Output.Dispose();
        Input.Dispose();
        _enumerator.Dispose();
    }
}

/// <summary>Nivel de señal de un dispositivo (0–1) mientras el objeto está vivo.</summary>
public sealed class LevelMonitor : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiCapture? _capture;

    internal LevelMonitor(MMDevice device, bool input)
    {
        _device = device;
        if (!input)
            return;
        try
        {
            _capture = new WasapiCapture(device);
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir el micrófono {device.FriendlyName} para medir el nivel", ex);
            _capture?.Dispose();
            _capture = null;
        }
    }

    public float Peak
    {
        get
        {
            try
            {
                return _device.AudioMeterInformation.MasterPeakValue;
            }
            catch (Exception)
            {
                return 0; // el dispositivo desapareció
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _capture?.StopRecording();
        }
        catch (Exception)
        {
        }
        _capture?.Dispose();
        _device.Dispose();
    }
}
