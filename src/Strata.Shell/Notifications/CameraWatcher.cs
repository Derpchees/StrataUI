using Windows.Devices.Enumeration;

namespace Strata.Shell.Notifications;

/// <summary>
/// Avisa cuando la cámara se enciende o se apaga con su tecla (la de los portátiles quita o
/// desactiva el dispositivo de video): así Strata UI muestra su aviso en lugar del del fabricante.
/// </summary>
internal static class CameraWatcher
{
    private static DeviceWatcher? _watcher;
    private static bool _ready;
    private static readonly Dictionary<string, bool> Enabled = new();
    private static Action<bool>? _changed;

    public static void Start(Action<bool> changed)
    {
        if (_watcher != null)
            return;
        _changed = changed;
        try
        {
            var watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
            watcher.Added += (_, info) => Post(() =>
            {
                Enabled[info.Id] = info.IsEnabled;
                if (_ready && info.IsEnabled)
                    _changed?.Invoke(true);
            });
            watcher.Removed += (_, update) => Post(() =>
            {
                if (Enabled.Remove(update.Id, out bool was) && was && _ready)
                    _changed?.Invoke(false);
            });
            watcher.Updated += (_, update) => Post(() =>
            {
                if (!update.Properties.TryGetValue("System.Devices.InterfaceEnabled", out var value) || value is not bool on)
                    return;
                bool known = Enabled.TryGetValue(update.Id, out bool was);
                Enabled[update.Id] = on;
                if (_ready && known && was != on)
                    _changed?.Invoke(on);
            });
            watcher.EnumerationCompleted += (_, _) => Post(() => _ready = true);
            watcher.Start();
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            Services.Log.Error("No se pudo vigilar la cámara", ex);
        }
    }

    public static void Stop()
    {
        if (_watcher is { } watcher && watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            watcher.Stop();
        _watcher = null;
        _changed = null;
        _ready = false;
        Enabled.Clear();
    }

    private static void Post(Action action) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
}
