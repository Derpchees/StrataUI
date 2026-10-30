using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Updates;

/// <summary>Versión publicada más nueva que la instalada.</summary>
/// <param name="InstallerSha256">El hash que GitHub publica del instalador (hexadecimal; null si no lo trae).</param>
public sealed record UpdateInfo(Version Version, string Notes, string? InstallerUrl, long InstallerSize, string PageUrl,
                                string? InstallerSha256 = null);

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Error }

/// <summary>
/// Busca versiones nuevas en las publicaciones (releases) de GitHub del proyecto y las instala con
/// su instalador. La búsqueda automática se hace un rato después de arrancar y luego cada pocas
/// horas, si está activada en el Hub.
/// </summary>
public static class UpdateService
{
    /// <summary>Repositorio de GitHub donde se publican las versiones (con el instalador adjunto).</summary>
    public const string Repository = "Derpchees/StrataUI";

    private static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(6);
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"StrataUI/{App.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    });

    /// <summary>Cliente de GitHub compartido (también baja el módulo de optimización).</summary>
    internal static HttpClient Client => Http.Value;

    private static DispatcherTimer? _timer;
    private static Version? _notified;

    public static UpdateState State { get; private set; }
    public static UpdateInfo? Available { get; private set; }
    public static string? ErrorText { get; private set; }
    /// <summary>Avance de la descarga, de 0 a 1.</summary>
    public static double Progress { get; private set; }

    public static event Action? Changed;

    public static Version Current => Version.Parse(App.Version);

    /// <summary>Activa o desactiva la búsqueda automática según la configuración.</summary>
    public static void UpdateSchedule()
    {
        if (!Settings.Current.AutoCheckUpdates)
        {
            _timer?.Stop();
            return;
        }
        if (_timer is { IsEnabled: true })
            return;
        _timer ??= new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick -= OnTimer;
        _timer.Tick += OnTimer;
        // La primera, un minuto después de arrancar (o antes si hace mucho de la última).
        var since = DateTime.UtcNow - (Settings.Current.LastUpdateCheck ?? DateTime.MinValue);
        _timer.Interval = since > AutomaticInterval ? TimeSpan.FromMinutes(1) : AutomaticInterval - since;
        _timer.Start();
    }

    public static void Shutdown() => _timer?.Stop();

    private static async void OnTimer(object? sender, EventArgs e)
    {
        _timer!.Interval = AutomaticInterval;
        await CheckAsync();
        // Aviso una sola vez por versión.
        if (Available is { } update && update.Version != _notified)
        {
            _notified = update.Version;
            Strata.Shell.Notifications.NotificationBannerWindow.ShowBanner(new Strata.Shell.Services.Notifications.StrataNotification
            {
                IsLocal = true,
                AppName = "Strata UI",
                Title = Loc.F("Strata UI {0} está disponible", update.Version.ToString(3)),
                Body = Loc.T("Haz clic para ver las novedades e instalarla."),
                Time = DateTimeOffset.Now,
                Icon = Controls.Logo.Image,
                OnClick = () => App.Current.OpenHub("Updates"),
            });
        }
    }

    /// <summary>Consulta la última versión publicada.</summary>
    public static async Task CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading)
            return;
        SetState(UpdateState.Checking);
        try
        {
            using var response = await Http.Value.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Available = null;
                ErrorText = Loc.T("Todavía no hay versiones publicadas.");
                SetState(UpdateState.Error);
                return;
            }
            response.EnsureSuccessStatusCode();
            var info = Parse(await response.Content.ReadAsStringAsync());
            Settings.Current.LastUpdateCheck = DateTime.UtcNow;
            Settings.Save();
            Available = info != null && info.Version > Current ? info : null;
            SetState(Available != null ? UpdateState.Available : UpdateState.UpToDate);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            // Sin internet (o GitHub caído) es lo normal en una laptop: una línea, sin la pila.
            if (ex is HttpRequestException { InnerException: System.Net.Sockets.SocketException } or TaskCanceledException)
                Log.Info($"No se pudo buscar actualizaciones: sin conexión ({ex.Message})");
            else
                Log.Error("No se pudo buscar actualizaciones", ex);
            ErrorText = Loc.T("No se pudo conectar con el servidor de actualizaciones.");
            SetState(UpdateState.Error);
        }
    }

    /// <summary>Lee la respuesta de GitHub: versión (de la etiqueta vX.Y.Z), notas e instalador .exe adjunto.</summary>
    internal static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            return null;
        string? url = null, sha256 = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.GetProperty("browser_download_url").GetString();
                    size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    // "sha256:…" (GitHub lo calcula al subir el archivo).
                    if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
                        && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha256 = d["sha256:".Length..];
                    break;
                }
            }
        }
        string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
        return new UpdateInfo(version, notes.Trim(), url, size, page, sha256);
    }

    /// <summary>
    /// Descarga el instalador y lo ejecuta en modo silencioso; Strata UI se cierra (devolviendo la barra
    /// de Windows) y el instalador lo vuelve a abrir al terminar.
    /// </summary>
    public static async Task DownloadAndInstallAsync()
    {
        if (Available?.InstallerUrl is not { } url || State == UpdateState.Downloading)
            return;
        Progress = 0;
        SetState(UpdateState.Downloading);
        string file = Path.Combine(Path.GetTempPath(), $"StrataUI-Setup-{Available.Version.ToString(3)}.exe");
        try
        {
            using (var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? Available.InstallerSize;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                    {
                        Progress = (double)read / total;
                        Changed?.Invoke();
                    }
                }
                // Una descarga cortada sin error (Wi-Fi que se cae) dejaba un instalador a medias, y
                // Strata UI ya se había cerrado: se revisa antes de seguir.
                if (total > 0 && read != total)
                    throw new IOException($"descarga incompleta ({read} de {total} bytes)");
            }
            if (Available.InstallerSha256 is { } expected)
            {
                await using var downloaded = File.OpenRead(file);
                string actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(downloaded));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"el instalador no coincide con el publicado (SHA-256 {actual})");
            }
            Log.Info($"Instalando Strata UI {Available.Version} desde {file}");
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            App.Current.ExitShell();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo descargar la actualización", ex);
            ErrorText = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                ? Loc.T("Se canceló la instalación.")
                : Loc.T("No se pudo descargar la actualización.");
            SetState(UpdateState.Error);
        }
    }

    public static void OpenReleasePage()
    {
        string url = Available?.PageUrl is { Length: > 0 } page ? page : $"https://github.com/{Repository}/releases";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
