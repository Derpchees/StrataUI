using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Winx.Shell.Services.WebApps;

/// <summary>Un sitio que WINX reconoce como app (YouTube, WhatsApp Web…).</summary>
/// <param name="Name">Nombre que se muestra.</param>
/// <param name="Domain">Dominio del que sale el ícono.</param>
/// <param name="Title">Cómo termina o se ve el título de la pestaña (sin el nombre del navegador).</param>
public sealed record WebApp(string Name, string Domain, Regex Title);

/// <summary>
/// Apps web dentro del navegador: una pestaña con YouTube, WhatsApp Web, Gmail… se muestra en la
/// barra, en las notificaciones y en multimedia con el nombre y el ícono del sitio, no los del
/// navegador.
/// </summary>
/// <remarks>
/// El sitio se reconoce por el título de la pestaña (el navegador no dice la dirección) o, en las
/// notificaciones, por el dominio que el navegador agrega como atribución. Los íconos se descargan
/// una vez por dominio (servicio de favicons de Google) y se guardan en %LOCALAPPDATA%\WINX\icons.
/// </remarks>
public static class WebSites
{
    private static Regex T(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Separador del nombre del sitio en el título: "Video - YouTube", "Inbox | Gmail"…
    private const string Sep = @"(^|\s[-–—|·•]\s)";

    /// <summary>Sitios conocidos; el orden importa (YouTube Music antes que YouTube).</summary>
    public static IReadOnlyList<WebApp> Known { get; } =
    [
        new("YouTube Music", "music.youtube.com", T(Sep + @"YouTube Music$")),
        new("YouTube", "youtube.com", T(@"(^(\(\d+\)\s*)?YouTube$)|(\s[-–—]\s(YouTube)$)")),
        new("WhatsApp", "web.whatsapp.com", T(@"^(\(\d+\)\s*)?WhatsApp( Business)?$")),
        new("Gmail", "mail.google.com", T(Sep + @"Gmail$|^Gmail$")),
        new("Google Calendar", "calendar.google.com", T(Sep + @"(Google )?(Calendar|Calendario)$")),
        new("Google Drive", "drive.google.com", T(Sep + @"Google Drive$")),
        new("Google Meet", "meet.google.com", T(@"^Meet\s[-–—]\s|^Google Meet$")),
        new("Outlook", "outlook.live.com", T(Sep + @"Outlook$|^Outlook$")),
        new("Microsoft Teams", "teams.microsoft.com", T(Sep + @"Microsoft Teams$|^Microsoft Teams$")),
        new("Spotify", "open.spotify.com", T(@"(\s[-–—|·•]\s|^)Spotify$|^Spotify\s[-–—]\s")),
        new("Netflix", "netflix.com", T(@"^Netflix$|" + Sep + @"Netflix$")),
        new("Prime Video", "primevideo.com", T(Sep + @"Prime Video$|^Prime Video")),
        new("Disney+", "disneyplus.com", T(Sep + @"Disney\+$|^Disney\+")),
        new("Twitch", "twitch.tv", T(Sep + @"Twitch$|^Twitch$")),
        new("Discord", "discord.com", T(Sep + @"Discord$|^Discord$")),
        new("Telegram", "web.telegram.org", T(@"^(\(\d+\)\s*)?Telegram( Web)?$")),
        new("Messenger", "messenger.com", T(@"^(\(\d+\)\s*)?Messenger$|" + Sep + @"Messenger$")),
        new("Instagram", "instagram.com", T(@"^(\(\d+\)\s*)?Instagram$|" + Sep + @"Instagram( photos and videos)?$|•\sInstagram")),
        new("Facebook", "facebook.com", T(@"^(\(\d+\)\s*)?Facebook$|" + Sep + @"Facebook$")),
        new("X", "x.com", T(@"\s/\sX$|^X$")),
        new("LinkedIn", "linkedin.com", T(Sep + @"LinkedIn$|^(\(\d+\)\s*)?LinkedIn$")),
        new("Reddit", "reddit.com", T(Sep + @"Reddit$|^Reddit")),
        new("TikTok", "tiktok.com", T(Sep + @"TikTok$|^TikTok")),
        new("ChatGPT", "chatgpt.com", T(@"^ChatGPT$|" + Sep + @"ChatGPT$")),
        new("Claude", "claude.ai", T(@"^Claude$|" + Sep + @"Claude$")),
        new("GitHub", "github.com", T(Sep + @"GitHub$|^GitHub$")),
        new("Deezer", "deezer.com", T(Sep + @"Deezer$|^Deezer")),
        new("SoundCloud", "soundcloud.com", T(Sep + @"SoundCloud$|on SoundCloud$")),
    ];

    // ── Navegadores ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> BrowserExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "brave.exe", "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "vivaldi.exe", "arc.exe",
    };

    /// <summary>AppUserModelID con que los navegadores envían notificaciones y anuncian lo que suena.</summary>
    private static readonly HashSet<string> BrowserIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Brave", "Chrome", "MSEdge", "Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge", "308046B0AF4A39CB",
        "Opera", "Vivaldi", "brave.exe", "chrome.exe", "msedge.exe", "firefox.exe",
    };

    /// <summary>"Brave", "Google Chrome"… al final del título de la ventana.</summary>
    private static readonly Regex BrowserSuffix = T(
        @"\s[-–—]\s(?<browser>Brave|Google Chrome|Chromium|Microsoft\u200b?\s?Edge|Mozilla Firefox|Firefox|Opera|Vivaldi|Arc)$");

    /// <summary>Lo que Edge agrega antes de su nombre: " and 3 more pages" y " - Personal" (el perfil).</summary>
    private static readonly Regex EdgeMorePages = T(@"\s+and \d+ more pages?$");
    private static readonly Regex LastSegment = T(@"\s[-–—]\s[^-–—]{1,40}$");

    public static bool IsBrowserExe(string? path) =>
        !string.IsNullOrEmpty(path) && BrowserExes.Contains(Path.GetFileName(path));

    public static bool IsBrowserId(string? appId) =>
        !string.IsNullOrEmpty(appId) && (BrowserIds.Contains(appId) || BrowserIds.Contains(Path.GetFileName(appId)));

    /// <summary>Título de la pestaña sin el nombre del navegador ("Video - YouTube - Brave" → "Video - YouTube").</summary>
    public static string PageTitle(string windowTitle)
    {
        string title = windowTitle.Trim();
        var browser = BrowserSuffix.Match(title);
        if (!browser.Success)
            return title;
        title = title[..browser.Index];
        if (browser.Groups["browser"].Value.Contains("Edge", StringComparison.OrdinalIgnoreCase))
        {
            // Edge pone el perfil antes de su nombre ("Inbox - Personal - Microsoft Edge"). Si lo que
            // queda ya es un sitio ("Video - YouTube"), ese último tramo no es un perfil.
            if (!Known.Any(app => app.Title.IsMatch(title)) && LastSegment.Match(title) is { Success: true } profile)
                title = title[..profile.Index];
            title = EdgeMorePages.Replace(title, "");
        }
        return title;
    }

    /// <summary>El sitio de una ventana del navegador, por el título de su pestaña.</summary>
    public static WebApp? FromWindowTitle(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return null;
        string page = PageTitle(windowTitle);
        return Known.FirstOrDefault(app => app.Title.IsMatch(page));
    }

    /// <summary>Un sitio conocido por su dominio (de una notificación); también sus subdominios.</summary>
    public static WebApp? FromDomain(string domain)
    {
        domain = domain.ToLowerInvariant();
        if (domain.StartsWith("www."))
            domain = domain[4..];
        return Known.FirstOrDefault(app => domain == app.Domain || domain.EndsWith("." + app.Domain));
    }

    private static readonly Regex Host = T(@"^(?:https?://)?([a-z0-9-]+(\.[a-z0-9-]+)+)(:\d+)?/?$");

    /// <summary>Si un texto es solo un dominio ("web.whatsapp.com"), lo devuelve.</summary>
    public static string? AsDomain(string text)
    {
        var match = Host.Match(text.Trim());
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Nombre legible de un dominio desconocido: "www.ejemplo.com" → "ejemplo.com".</summary>
    public static string DomainName(string domain) =>
        Regex.Replace(domain, @"^(www|web|app|m)\.", "", RegexOptions.IgnoreCase);

    // ── Íconos ──────────────────────────────────────────────────────────────

    private static readonly string IconFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINX", "icons");

    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Loading = new(StringComparer.OrdinalIgnoreCase);
    private static HttpClient? _http;

    /// <summary>Llegó el ícono de algún sitio (en el hilo de la UI): quien muestre íconos los vuelve a pedir.</summary>
    public static event Action? IconsChanged;

    /// <summary>Ícono del sitio si ya está; si no, empieza a buscarlo y avisa con <see cref="IconsChanged"/>.</summary>
    public static ImageSource? Icon(string domain)
    {
        if (Icons.TryGetValue(domain, out var icon))
            return icon;
        if (Loading.Add(domain))
            _ = LoadIconAsync(domain);
        return null;
    }

    private static async Task LoadIconAsync(string domain)
    {
        ImageSource? icon = null;
        try
        {
            string file = Path.Combine(IconFolder, domain + ".png");
            if (!File.Exists(file))
            {
                _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var bytes = await _http.GetByteArrayAsync($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz=64");
                Directory.CreateDirectory(IconFolder);
                await File.WriteAllBytesAsync(file, bytes);
            }
            icon = LoadImage(file);
        }
        catch (Exception ex)
        {
            Log.Info($"No se pudo obtener el ícono de {domain}: {ex.Message}");
        }
        Icons[domain] = icon;
        Loading.Remove(domain);
        if (icon != null)
            IconsChanged?.Invoke();
    }

    private static ImageSource LoadImage(string file)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(file);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 64;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
