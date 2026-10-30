using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.WebApps;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace Strata.Shell.Services.Media;

/// <summary>Una app que está reproduciendo (o pausada) y se anuncia a Windows.</summary>
public sealed record MediaSource(string Id, string Name);

/// <summary>
/// Lo que suena ahora en cualquier app que se anuncie a Windows (Spotify, Reproductor multimedia,
/// Deezer, YouTube y demás sitios en el navegador…): carátula, título, artista, avance y controles.
/// Es la misma fuente que usa el panel multimedia de Windows (System Media Transport Controls).
/// </summary>
/// <remarks>
/// Windows avisa de los cambios desde otros hilos: todo se pasa al hilo de la UI antes de tocar el
/// estado. Sin nada reproduciéndose no hay temporizadores: solo se escuchan los avisos.
/// </remarks>
public static class MediaService
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static Session? _session;
    private static string? _chosenId;
    private static string _coverKey = "";
    private static string? _coverPendingKey;

    public static bool HasSession => _session != null;
    public static string Title { get; private set; } = "";
    public static string Artist { get; private set; } = "";
    public static string Album { get; private set; } = "";
    public static string AppName { get; private set; } = "";

    /// <summary>Ícono de la app que suena, o del sitio si suena en el navegador (YouTube…).</summary>
    public static ImageSource? AppIcon => _site is { } site ? WebSites.Icon(site.Domain) : _appIcon;

    private static WebApp? _site;
    private static ManagedShell.WindowsTasks.ApplicationWindow? _siteWindow;
    private static ImageSource? _appIcon;
    public static ImageSource? Cover { get; private set; }
    public static bool IsPlaying { get; private set; }
    public static bool CanPrevious { get; private set; }
    public static bool CanNext { get; private set; }
    public static bool CanSeek { get; private set; }

    /// <summary>Todas las apps con sesión (para elegir cuál controlar si hay varias).</summary>
    public static IReadOnlyList<MediaSource> Sources { get; private set; } = [];

    public static string CurrentSourceId => _session?.SourceAppUserModelId ?? "";

    /// <summary>Cambió la canción, el estado, la app o la carátula (en el hilo de la UI).</summary>
    public static event Action? Changed;

    public static async void Initialize()
    {
        try
        {
            WebSites.IconsChanged += () =>
            {
                if (_site != null)
                    Changed?.Invoke();
            };
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => OnUi(Refresh);
            _manager.CurrentSessionChanged += (_, _) => OnUi(Refresh);
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo acceder a los controles multimedia de Windows", ex);
        }
    }

    private static void OnUi(Action action) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    /// <summary>
    /// Elige la sesión: la que eligió el usuario si sigue abierta; si no, la que Windows considera
    /// actual; si no, la primera que esté sonando.
    /// </summary>
    private static void Refresh()
    {
        if (_manager is null)
            return;
        var sessions = _manager.GetSessions().ToList();
        Sources = sessions.Select(s => new MediaSource(s.SourceAppUserModelId, FriendlyName(s.SourceAppUserModelId))).ToList();
        var next = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _chosenId)
                   ?? _manager.GetCurrentSession()
                   ?? sessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                   ?? sessions.FirstOrDefault();
        Attach(next);
    }

    private static void Attach(Session? session)
    {
        if (!ReferenceEquals(session, _session))
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
            }
            _session = session;
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnMediaChanged;
                _session.PlaybackInfoChanged += OnPlaybackChanged;
            }
        }
        if (_session is null)
        {
            Title = Artist = Album = AppName = "";
            _site = null;
            _siteWindow = null;
            _appIcon = null;
            Cover = null;
            _coverKey = "";
            _coverPendingKey = null;
            IsPlaying = false;
            Changed?.Invoke();
            return;
        }
        AppName = FriendlyName(_session.SourceAppUserModelId);
        _appIcon = AppIconFor(_session.SourceAppUserModelId);
        UpdatePlayback();
        _ = UpdateMediaAsync();
    }

    private static void OnMediaChanged(Session sender, MediaPropertiesChangedEventArgs args) =>
        OnUi(() => _ = UpdateMediaAsync());

    private static void OnPlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args) =>
        OnUi(() =>
        {
            UpdatePlayback();
            Changed?.Invoke();
        });

    private static void UpdatePlayback()
    {
        // Mientras una app cierra su sesión, Windows puede devolver la información vacía.
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = null;
        try
        {
            info = _session?.GetPlaybackInfo();
        }
        catch (Exception)
        {
        }
        IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        CanPrevious = info?.Controls?.IsPreviousEnabled == true;
        CanNext = info?.Controls?.IsNextEnabled == true;
        CanSeek = info?.Controls?.IsPlaybackPositionEnabled == true;
    }

    private static async Task UpdateMediaAsync()
    {
        var session = _session;
        if (session is null)
            return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session) || props is null)
                return;
            Title = props.Title ?? "";
            Artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            Album = props.AlbumTitle ?? "";
            FindSite(session.SourceAppUserModelId);
            // La carátula solo se vuelve a leer si cambió la canción (leerla cuesta). Spotify y otros
            // avisan primero del título y la imagen llega después: solo se da por leída cuando se
            // cargó, y si aún no está se reintenta en un momento.
            string key = session.SourceAppUserModelId + "|" + Title + "|" + Artist;
            if (key != _coverKey)
            {
                if (_coverPendingKey != key)
                    Cover = null; // otra canción: no dejar la carátula anterior
                var cover = props.Thumbnail is { } thumb ? await LoadCoverAsync(thumb) : null;
                if (!ReferenceEquals(session, _session))
                    return;
                if (cover != null)
                {
                    Cover = cover;
                    _coverKey = key;
                    _coverPendingKey = null;
                }
                else if (_coverPendingKey != key)
                {
                    _coverPendingKey = key;
                    _ = RetryCoverAsync(key);
                }
            }
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // Algunas apps cierran la sesión justo mientras se lee.
            Log.Info($"No se pudo leer lo que suena en {AppName}: {ex.Message}");
        }
    }

    /// <summary>Vuelve a pedir la carátula unas veces mientras siga la misma canción.</summary>
    private static async Task RetryCoverAsync(string key)
    {
        foreach (var delay in new[] { 700, 1500, 3000 })
        {
            await Task.Delay(delay);
            if (_coverPendingKey != key || _coverKey == key)
                return;
            await UpdateMediaAsync();
        }
    }

    private static async Task<ImageSource?> LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumb)
    {
        try
        {
            using var stream = (await thumb.OpenReadAsync()).AsStreamForRead();
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memory;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Info($"No se pudo leer la carátula: {ex.Message}");
            return null;
        }
    }

    /// <summary>Avance de la canción ahora mismo (Windows da la posición de la última actualización).</summary>
    public static (TimeSpan Position, TimeSpan Duration) Timeline()
    {
        GlobalSystemMediaTransportControlsSessionTimelineProperties? t = null;
        try
        {
            t = _session?.GetTimelineProperties();
        }
        catch (Exception)
        {
        }
        if (t is null)
            return (TimeSpan.Zero, TimeSpan.Zero);
        var position = t.Position;
        if (IsPlaying && t.LastUpdatedTime > DateTimeOffset.MinValue)
            position += DateTimeOffset.Now - t.LastUpdatedTime;
        var duration = t.EndTime - t.StartTime;
        if (duration > TimeSpan.Zero && position > duration)
            position = duration;
        return (position < TimeSpan.Zero ? TimeSpan.Zero : position, duration);
    }

    // ── Controles ───────────────────────────────────────────────────────────

    public static void PlayPause() => Send(session => session.TryTogglePlayPauseAsync());

    public static void Next() => Send(session => session.TrySkipNextAsync());

    public static void Previous() => Send(session => session.TrySkipPreviousAsync());

    public static void Seek(TimeSpan position)
    {
        if (CanSeek)
            Send(session => session.TryChangePlaybackPositionAsync(position.Ticks));
    }

    /// <summary>
    /// Una orden a la app que suena. Si la app se acaba de cerrar, Windows lanza (servidor RPC no
    /// disponible): no pasa nada, la sesión se va con el próximo aviso de cambios.
    /// </summary>
    private static async void Send(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        if (_session is not { } session)
            return;
        try
        {
            await command(session);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info($"Multimedia: la app no recibió la orden ({ex.Message})");
        }
    }

    /// <summary>
    /// Trae al frente la app que suena: su ventana si está en la barra (Spotify.exe, navegadores) o,
    /// si no, la abre por su AppUserModelID (apps de la Tienda).
    /// </summary>
    public static void ShowSourceApp()
    {
        string id = CurrentSourceId;
        if (id.Length == 0)
            return;
        try
        {
            if (_siteWindow is { } tab && App.Current.Shell.Tasks.GroupedWindows.OfType<ManagedShell.WindowsTasks.ApplicationWindow>().Contains(tab))
            {
                tab.BringToFront();
                return;
            }
            var window = App.Current.Shell.Tasks.GroupedWindows.OfType<ManagedShell.WindowsTasks.ApplicationWindow>()
                .FirstOrDefault(w => string.Equals(w.AppUserModelID, id, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetFileName(w.WinFileName), id, StringComparison.OrdinalIgnoreCase));
            if (window != null)
            {
                window.BringToFront();
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {AppName}", ex);
        }
    }

    /// <summary>Controlar otra app (cuando hay varias con sesión).</summary>
    public static void Choose(string sourceId)
    {
        _chosenId = sourceId;
        Refresh();
    }

    // ── Sitio dentro del navegador ──────────────────────────────────────────

    /// <summary>
    /// Si suena en el navegador, qué sitio es: el navegador solo dice "Brave", así que se busca la
    /// ventana cuya pestaña lleva en el título lo que suena ("Canción - YouTube - Brave").
    /// </summary>
    private static void FindSite(string sourceId)
    {
        _site = null;
        _siteWindow = null;
        if (!WebSites.IsBrowserId(sourceId) || Title.Length == 0)
        {
            AppName = FriendlyName(sourceId);
            return;
        }
        foreach (var window in App.Current.Shell.Tasks.GroupedWindows.OfType<ManagedShell.WindowsTasks.ApplicationWindow>())
        {
            if (!WebSites.IsBrowserExe(window.WinFileName) || string.IsNullOrEmpty(window.Title)
                || !WebSites.PageTitle(window.Title).Contains(Title, StringComparison.OrdinalIgnoreCase))
                continue;
            _siteWindow = window;
            _site = WebSites.FromWindowTitle(window.Title);
            break;
        }
        AppName = _site?.Name ?? FriendlyName(sourceId);
    }

    /// <summary>Ícono de una app por su AppUserModelID (menú Inicio, registro o su ventana).</summary>
    private static ImageSource? AppIconFor(string id) =>
        string.IsNullOrEmpty(id) ? null : AppIcons.Find(id, null);

    // ── Nombre de la app ────────────────────────────────────────────────────

    /// <summary>
    /// Windows da el AppUserModelID ("Spotify.exe", "Microsoft.ZuneMusic_…!Microsoft.ZuneMusic",
    /// "308046B0AF4A39CB" para Firefox…): se busca en el menú Inicio y, si no, se limpia.
    /// </summary>
    internal static string FriendlyName(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Loc.T("Multimedia");
        var app = StartMenu.StartMenuService.Apps.FirstOrDefault(a =>
            a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(a.Id).Equals(id, StringComparison.OrdinalIgnoreCase));
        if (app != null)
            return app.Name;
        return CleanName(id);
    }

    internal static string CleanName(string id)
    {
        string name = id.Contains('!') ? id[..id.IndexOf('!')] : id;   // familia de paquete
        name = name.Contains('_') ? name[..name.IndexOf('_')] : name;  // sin el editor
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);              // "Spotify.exe" → "Spotify"
        if (name.Contains('.'))
        {
            // "Microsoft.ZuneMusic" → "ZuneMusic"; "Deezer.62021768415AF" → "Deezer" (el código del editor no tiene minúsculas).
            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            name = parts.LastOrDefault(p => p.Any(char.IsLower)) ?? parts.LastOrDefault() ?? name;
        }
        return name switch
        {
            "ZuneMusic" => "Media Player",
            "msedge" => "Microsoft Edge",
            "chrome" => "Google Chrome",
            "brave" => "Brave",
            "firefox" or "308046B0AF4A39CB" => "Firefox",
            "opera" => "Opera",
            _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : Loc.T("Multimedia"),
        };
    }
}
