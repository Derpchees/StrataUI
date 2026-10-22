using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.WebApps;

namespace Strata.Shell.Services.Notifications;

/// <summary>Una notificación tal como la muestra Strata UI.</summary>
public sealed class StrataNotification : System.ComponentModel.INotifyPropertyChanged
{
    public uint Id { get; init; }
    public string AppName { get; init; } = "";
    /// <summary>AppUserModelID de la app (para abrirla); null si Windows no lo da.</summary>
    public string? AppId { get; init; }
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTimeOffset Time { get; init; }
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    private ImageSource? _icon;

    /// <summary>Sitio que la envió desde el navegador (WhatsApp Web, Gmail…): de ahí sale el ícono.</summary>
    public string? SiteDomain { get; init; }

    /// <summary>Imagen del aviso (foto del remitente, logotipo del sitio), como la muestra Windows a la izquierda.</summary>
    public ImageSource? Picture
    {
        get => _picture;
        set
        {
            _picture = value;
            Notify(nameof(Picture));
            Notify(nameof(HasPicture));
        }
    }

    private ImageSource? _picture, _hero;

    /// <summary>La imagen va recortada en círculo (fotos de contactos).</summary>
    public bool PictureIsCircle { get; init; }

    public bool HasPicture => Picture != null;

    /// <summary>Imagen grande del contenido (un sticker, una foto), debajo del texto.</summary>
    public ImageSource? Hero
    {
        get => _hero;
        set
        {
            _hero = value;
            Notify(nameof(Hero));
            Notify(nameof(HasHero));
        }
    }

    public bool HasHero => Hero != null;

    /// <summary>El "launch" del aviso: lo que recibe la app al hacer clic (ver <see cref="NotificationService.Activate"/>).</summary>
    public string? Launch { get; init; }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Aviso propio de Strata UI (prueba, actualización): no existe en Windows.</summary>
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

    /// <summary>
    /// Descartadas en Strata UI que Windows no quiso quitar de su centro: no se vuelven a mostrar mientras
    /// sigan allí (si no, "Borrar todo" las traería de vuelta en la siguiente consulta).
    /// </summary>
    private static readonly HashSet<uint> Dismissed = new();

    /// <summary>Notificaciones actuales, la más reciente primero.</summary>
    public static ObservableCollection<StrataNotification> Items { get; } = new();

    /// <summary>Llegó una notificación nueva (para el aviso emergente).</summary>
    public static event Action<StrataNotification>? Arrived;

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
        WebSites.IconsChanged -= OnSiteIconsChanged;
        WebSites.IconsChanged += OnSiteIconsChanged;
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

    /// <summary>Llegaron íconos de sitios: se ponen en sus notificaciones.</summary>
    private static void OnSiteIconsChanged()
    {
        foreach (var item in Items)
        {
            if (item.SiteDomain is { } domain && WebSites.Icon(domain) is { } icon && !ReferenceEquals(item.Icon, icon))
                item.Icon = icon;
        }
    }

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
            Dismissed.IntersectWith(ids); // las que Windows ya quitó no hace falta recordarlas
            current = current.Where(n => !Dismissed.Contains(n.Id)).ToList();
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
                // Del navegador: el ícono del sitio (si aún no se descargó, llega con WebSites.IconsChanged).
                item.Icon = item.SiteDomain is { } domain
                    ? WebSites.Icon(domain) ?? await IconFor(raw, item.AppId, item.AppName)
                    : await IconFor(raw, item.AppId, item.AppName);
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

    private static StrataNotification? Convert(UserNotification raw)
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
            // Apps web instaladas (Brave._crx_…) y otras sin nombre en Windows: el del menú Inicio o
            // el que la app registró para sus notificaciones.
            if (string.IsNullOrWhiteSpace(appName) && appId != null)
                appName = StartMenu.StartMenuService.Apps.FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase))?.Name
                          ?? AppIcons.RegisteredName(appId)
                          ?? Media.MediaService.CleanName(appId);

            // El XML original (base de datos de Windows) trae lo que la API esconde: la atribución
            // del navegador con el dominio del sitio y la imagen del aviso.
            var details = NotificationDb.Details(raw.Id);

            string? siteDomain = null;
            bool fromBrowser = WebSites.IsBrowserId(appId) || WebSites.IsBrowserId(appName);
            if (fromBrowser)
            {
                string? domain = details?.Domain;
                // Sin base de datos: a veces el dominio llega como un texto más ("web.whatsapp.com").
                for (int i = texts.Count - 1; i >= 1; i--)
                {
                    if (WebSites.AsDomain(texts[i]) is { } inText)
                    {
                        texts.RemoveAt(i);
                        domain ??= inText;
                        break;
                    }
                }
                if (domain != null)
                {
                    var site = WebSites.FromDomain(domain);
                    siteDomain = site?.Domain ?? domain;
                    appName = site?.Name ?? WebSites.DomainName(domain);
                }
            }
            var item = new StrataNotification
            {
                Id = raw.Id,
                AppName = string.IsNullOrWhiteSpace(appName) ? Loc.T("Notificación") : appName,
                AppId = appId,
                SiteDomain = siteDomain,
                Launch = details?.Launch,
                PictureIsCircle = details?.CircleCrop == true,
                Title = texts.FirstOrDefault() ?? "",
                Body = string.Join("\n", texts.Skip(1)),
                Time = raw.CreationTime,
            };
            // Las imágenes de internet (fotos de WhatsApp) llegan un momento después.
            if (details?.Picture is { } picture)
                _ = NotificationImages.SetAsync(picture, 96, image => item.Picture = image);
            if (details?.Hero is { } hero)
                _ = NotificationImages.SetAsync(hero, 360, image => item.Hero = image);
            return item;
        }
        catch (Exception ex)
        {
            Log.Error($"Notificación ilegible ({raw.Id})", ex);
            return null;
        }
    }

    /// <summary>
    /// Ícono de la app: el del menú Inicio, el que registró para sus notificaciones o el de su
    /// ventana (ver <see cref="AppIcons"/>); si no, el logotipo que da Windows.
    /// </summary>
    private static async Task<ImageSource?> IconFor(UserNotification raw, string? appId, string appName)
    {
        // Sin identidad se guarda por nombre (antes todas las apps sin identidad compartían ícono).
        string key = appId ?? "name:" + appName;
        if (IconCache.TryGetValue(key, out var cached))
            return cached;
        // Las apps sin ícono no se guardan: puede aparecer cuando termine de cargar el catálogo.
        ImageSource? icon = AppIcons.Find(appId, appName);
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
    public static void Dismiss(StrataNotification item)
    {
        Items.Remove(item);
        if (!item.IsLocal)
            Remove(item.Id);
        Changed?.Invoke();
    }

    /// <summary>
    /// Borra todas. Windows a veces rechaza el borrado en bloque (0x80070490, "no encontrado", p. ej.
    /// si alguna ya no existe): entonces se quitan una por una.
    /// </summary>
    public static void ClearAll()
    {
        var items = Items.Where(i => !i.IsLocal).ToList();
        Items.Clear();
        bool cleared = false;
        try
        {
            _listener?.ClearNotifications();
            cleared = true;
        }
        catch (Exception ex)
        {
            Log.Info($"Windows no borró las notificaciones de una vez ({ex.Message.Trim()}); se quitan una por una");
        }
        if (!cleared)
        {
            foreach (var item in items)
                Remove(item.Id);
        }
        Changed?.Invoke();
    }

    /// <summary>Quita una notificación del centro de Windows; si no se puede, al menos no vuelve a Strata UI.</summary>
    private static void Remove(uint id)
    {
        try
        {
            _listener?.RemoveNotification(id);
        }
        catch (Exception ex)
        {
            Dismissed.Add(id);
            Log.Info($"Windows no quitó la notificación {id} ({ex.Message.Trim()}); se oculta en Strata UI");
        }
    }

    /// <summary>
    /// Hace lo que haría Windows al hacer clic y la quita. En orden:
    /// <list type="number">
    /// <item>De un sitio del navegador (WhatsApp Web…): su app web si está instalada; si no, el
    /// navegador recibe el clic como de Windows y la página lo atiende (ver <see cref="BrowserActivation"/>).</item>
    /// <item>De una app con activador propio (WhatsApp, Spotify, Thunderbird…): el activador recibe el
    /// clic y la app abre el chat, el correo o la canción (ver <see cref="ToastActivator"/>).</item>
    /// <item>Si no, la ventana de la app al frente si está abierta, o se abre la app.</item>
    /// </list>
    /// </summary>
    public static async void Activate(StrataNotification item)
    {
        Dismiss(item);
        // El clic fue en Strata UI: la app que se abre puede pasar al frente (si no, Windows solo hace
        // parpadear su botón en la barra).
        AllowSetForegroundWindow(AsfwAny);
        try
        {
            if (BrowserActivation.TryActivate(item) || await ToastActivator.TryActivateAsync(item) || OpenApp(item))
                return;
            Log.Info($"Notificación de {item.AppName} ({item.AppId ?? "sin identidad"}): no se encontró cómo abrir la app");
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {item.AppName}", ex);
        }
    }

    /// <summary>
    /// Sin activador: su ventana al frente (por identidad o por ejecutable) o la app desde el catálogo
    /// de Inicio (por identidad o por nombre). Abrir por <c>shell:AppsFolder</c> una identidad que no
    /// está ahí abría una ventana del Explorador.
    /// </summary>
    private static bool OpenApp(StrataNotification item)
    {
        string? appId = item.AppId;
        try
        {
            var window = App.Current.Shell.Tasks.GroupedWindows.OfType<ManagedShell.WindowsTasks.ApplicationWindow>()
                .FirstOrDefault(w => appId != null && (string.Equals(w.AppUserModelID, appId, StringComparison.OrdinalIgnoreCase)
                                                       || string.Equals(Path.GetFileName(w.WinFileName), appId, StringComparison.OrdinalIgnoreCase)));
            if (window != null)
            {
                window.BringToFront();
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // El shell aún no arrancó.
        }
        var apps = StartMenu.StartMenuService.Apps;
        var app = (appId is null ? null : apps.FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase)))
                  ?? apps.FirstOrDefault(a => a.Name.Equals(item.AppName, StringComparison.CurrentCultureIgnoreCase));
        if (app != null)
        {
            app.Launch();
            return true;
        }
        // Empaquetada pero fuera del catálogo (aún cargando): Windows la sabe abrir igual.
        if (appId is { } id && id.Contains('!'))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = true })?.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>Quita varias de una vez (un grupo del centro de notificaciones).</summary>
    public static void Dismiss(IReadOnlyList<StrataNotification> items)
    {
        foreach (var item in items)
        {
            Items.Remove(item);
            if (!item.IsLocal)
                Remove(item.Id);
        }
        Changed?.Invoke();
    }

    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Aviso propio de Strata UI (p. ej. cuánta RAM se liberó); el texto llega ya traducido.</summary>
    public static void ShowLocal(string title, string body) => Arrived?.Invoke(new StrataNotification
    {
        IsLocal = true,
        AppName = "Strata UI",
        Title = title,
        Body = body,
        Time = DateTimeOffset.Now,
        Icon = Controls.Logo.Image,
    });

    /// <summary>Muestra un aviso de ejemplo (botón "Probar" del Hub).</summary>
    public static void ShowTest() => Arrived?.Invoke(new StrataNotification
    {
        IsLocal = true,
        AppName = "Strata UI",
        Title = Loc.T("Aviso de prueba"),
        Body = Loc.T("Así se verán las notificaciones en este monitor y esquina."),
        Time = DateTimeOffset.Now,
        Icon = Controls.Logo.Image,
    });
}
