using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Winx.Shell.Services.Audio;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>Una aplicación con audio; agrupa todas las sesiones del mismo ejecutable.</summary>
public sealed class AudioApp
{
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

/// <summary>
/// Acceso a Core Audio: dispositivos de salida, volumen general y volumen por aplicación.
/// Los objetos COM se usan solo desde el hilo de la UI; las notificaciones se re-despachan a él.
/// </summary>
public sealed class AudioService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;

    /// <summary>Cambió el volumen, el silencio o el dispositivo predeterminado.</summary>
    public event Action? Changed;

    /// <summary>Se agregó, quitó o cambió algún dispositivo.</summary>
    public event Action? DevicesChanged;

    public AudioService()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
        AttachDefaultDevice();
    }

    public bool HasDevice => _device != null;

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

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        string? defaultId = _device?.ID;
        return _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDevice(d.ID, d.FriendlyName, d.ID == defaultId))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SetDefaultDevice(string id) => PolicyConfig.SetDefaultEndpoint(id);

    public IReadOnlyList<AudioApp> GetApps()
    {
        if (_device is null)
            return [];

        var manager = _device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        var groups = new Dictionary<string, (string Name, ImageSource? Icon, List<AudioSessionControl> Sessions)>();

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateExpired)
                continue;

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
            return ("system", "Sonidos del sistema", null);

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
            var display = string.IsNullOrWhiteSpace(session.DisplayName) ? "Aplicación" : session.DisplayName;
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

    private void AttachDefaultDevice()
    {
        if (_device != null)
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
            _device = null;
        }

        if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
        }
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data) =>
        Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());

    private void OnDevicesChanged(bool defaultChanged) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (defaultChanged)
                AttachDefaultDevice();
            DevicesChanged?.Invoke();
            Changed?.Invoke();
        });

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => OnDevicesChanged(false);
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => OnDevicesChanged(false);
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => OnDevicesChanged(false);
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
            OnDevicesChanged(true);
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        if (_device != null)
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
        }
        _enumerator.Dispose();
    }
}
