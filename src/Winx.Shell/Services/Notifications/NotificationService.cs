using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Services.Notifications;

/// <summary>Una notificación tal como la muestra WINX.</summary>
public sealed class WinxNotification
{
    public uint Id { get; init; }
    public string AppName { get; init; } = "";
    /// <summary>AppUserModelID de la app (para abrirla); null si Windows no lo da.</summary>
    public string? AppId { get; init; }
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTimeOffset Time { get; init; }
    public ImageSource? Icon { get; set; }
    /// <summary>Aviso propio de WINX (prueba, actualización): no existe en Windows.</summary>
    public bool IsLocal { get; init; }
    /// <summary>Qué hacer al hacer clic en un aviso propio.</summary>
    public Action? OnClick { get; init; }

    public string TimeText => (DateTimeOffset.Now - Time).TotalHours < 24
        ? Time.LocalDateTime.ToString("t")
        : Time.LocalDateTime.ToString("d");

    public bool HasBody => Body.Length > 0;
}

/// <summary>
/// Lee las notificaciones de Windows (las mismas de su centro de notificaciones) y avisa de las
/// nuevas. Sin identidad de paquete Windows no envía el evento de cambios, así que se consultan
/// cada pocos segundos; la consulta es barata y se detiene si se desactiva la función.
/// </summary>
public static class NotificationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private static UserNotificationListener? _listener;
    private static DispatcherTimer? _timer;
    private static bool _polling;
    private static bool _loaded;
    private static int _waitedForCatalog;
    private static readonly Dictionary<string, ImageSource?> IconCache = new();

    /// <summary>Notificaciones actuales, la más reciente primero.</summary>
    public static ObservableCollection<WinxNotification> Items { get; } = new();

    /// <summary>Llegó una notificación nueva (para el aviso emergente).</summary>
    public static event Action<WinxNotification>? Arrived;

    /// <summary>Cambió la lista o el permiso.</summary>
    public static event Action? Changed;

    /// <summary>Windows no deja leer las notificaciones (Privacidad › Notificaciones).</summary>
    public static bool AccessDenied { get; private set; }

    public static bool Running => _timer?.IsEnabled == true;

    /// <summary>Arranca o detiene la lectura según la configuración.</summary>
    public static async void Update()
    {
        if (!Settings.Current.NotificationsEnabled)
        {
            _timer?.Stop();
            Items.Clear();
            _loaded = false;
            Changed?.Invoke();
            return;
        }
        if (Running)
            return;
        try
        {
            _listener ??= UserNotificationListener.Current;
            var access = _listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified)
                access = await _listener.RequestAccessAsync();
            AccessDenied = access != UserNotificationListenerAccessStatus.Allowed;
            if (AccessDenied)
            {
                Log.Info($"Windows no permite leer las notificaciones ({access})");
                Changed?.Invoke();
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo acceder a las notificaciones de Windows", ex);
            AccessDenied = true;
            Changed?.Invoke();
            return;
        }

        _timer ??= new DispatcherTimer(PollInterval, DispatcherPriority.Background, async (_, _) => await PollAsync(),
                                       System.Windows.Application.Current.Dispatcher);
        _timer.Start();
        await PollAsync();
    }

    public static void Shutdown() => _timer?.Stop();

    private static async Task PollAsync()
    {
        if (_polling || _listener is null)
            return;
        // La primera lectura espera al catálogo de Inicio (de ahí salen los íconos), como mucho ~10 s.
        if (!_loaded && StartMenu.StartMenuService.Apps.Count == 0 && _waitedForCatalog++ < 5)
            return;
        _polling = true;
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = current.Select(n => n.Id).ToHashSet();
            bool changed = false;

            foreach (var gone in Items.Where(i => !ids.Contains(i.Id)).ToList())
            {
                Items.Remove(gone);
                changed = true;
            }

            var known = Items.Select(i => i.Id).ToHashSet();
            foreach (var raw in current.Where(n => !known.Contains(n.Id)).OrderBy(n => n.CreationTime))
            {
                if (Convert(raw) is not { } item)
                    continue;
                item.Icon = await IconFor(raw, item.AppId);
                int index = 0;
                while (index < Items.Count && Items[index].Time > item.Time)
                    index++;
                Items.Insert(index, item);
                changed = true;
                // La primera lectura son las que ya había: no se avisan. Tampoco las viejas
                // que reaparecen (p. ej. tras reiniciar el Explorador).
                if (_loaded && DateTimeOffset.Now - item.Time < TimeSpan.FromMinutes(1))
                    Arrived?.Invoke(item);
            }
            _loaded = true;
            if (changed)
                Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron leer las notificaciones", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private static WinxNotification? Convert(UserNotification raw)
    {
        try
        {
            var binding = raw.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
            string? appName = null, appId = null;
            try
            {
                // Algunas apps sin registrar hacen fallar AppInfo (NotImplemented).
                appName = raw.AppInfo?.DisplayInfo?.DisplayName;
                appId = raw.AppInfo?.AppUserModelId;
            }
            catch (Exception)
            {
            }
            return new WinxNotification
            {
                Id = raw.Id,
                AppName = string.IsNullOrWhiteSpace(appName) ? Loc.T("Notificación") : appName,
                AppId = appId,
                Title = texts.FirstOrDefault() ?? "",
                Body = string.Join("\n", texts.Skip(1)),
                Time = raw.CreationTime,
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Notificación ilegible ({raw.Id})", ex);
            return null;
        }
    }

    /// <summary>Ícono de la app: el del menú Inicio si está; si no, el logotipo que da Windows.</summary>
    private static async Task<ImageSource?> IconFor(UserNotification raw, string? appId)
    {
        string key = appId ?? "";
        if (IconCache.TryGetValue(key, out var cached))
            return cached;
        // Las apps sin ícono no se guardan: puede aparecer cuando termine de cargar el catálogo.
        ImageSource? icon = StartMenu.StartMenuService.Apps.FirstOrDefault(a => a.Id == appId)?.Icon;
        if (icon is null)
        {
            try
            {
                var logo = raw.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
                using var stream = (await logo.OpenReadAsync()).AsStreamForRead();
                var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                memory.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = memory;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 32;
                bitmap.EndInit();
                bitmap.Freeze();
                icon = bitmap;
            }
            catch (Exception)
            {
                // Sin logotipo: se muestra el ícono genérico.
            }
        }
        if (icon != null)
            IconCache[key] = icon;
        return icon;
    }

    // ── Acciones ────────────────────────────────────────────────────────────

    /// <summary>Quita la notificación (también del centro de Windows).</summary>
    public static void Dismiss(WinxNotification item)
    {
        Items.Remove(item);
        if (!item.IsLocal)
        {
            try
            {
                _listener?.RemoveNotification(item.Id);
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo quitar la notificación", ex);
            }
        }
        Changed?.Invoke();
    }

    public static void ClearAll()
    {
        Items.Clear();
        try
        {
            _listener?.ClearNotifications();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron borrar las notificaciones", ex);
        }
        Changed?.Invoke();
    }

    /// <summary>Abre la app que envió la notificación y la quita, como al hacer clic en Windows.</summary>
    public static void Activate(WinxNotification item)
    {
        if (item.AppId is { Length: > 0 } appId)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo abrir {item.AppName}", ex);
            }
        }
        Dismiss(item);
    }

    /// <summary>Muestra un aviso de ejemplo (botón "Probar" del Hub).</summary>
    public static void ShowTest() => Arrived?.Invoke(new WinxNotification
    {
        IsLocal = true,
        AppName = "WINX",
        Title = Loc.T("Aviso de prueba"),
        Body = Loc.T("Así se verán las notificaciones en este monitor y esquina."),
        Time = DateTimeOffset.Now,
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/winx.ico")),
    });
}
