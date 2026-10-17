using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Strata.Shell.Services.StartMenu;
using Strata.Shell.Services.WebApps;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Clic en una notificación de un sitio (WhatsApp Web, Gmail…). Abrir el navegador por su
/// AppUserModelID solo abría una ventana nueva; Windows en cambio le pasa el "launch" del aviso al
/// activador del navegador, que relanza el navegador con <c>--notification-launch-id</c> y la
/// página recibe el clic (enfoca su pestaña y abre el chat). Aquí se hace lo mismo, salvo que el
/// sitio esté instalado como app web: entonces se abre la app web.
/// </summary>
internal static class BrowserActivation
{
    // Chromium: "tipo|botón|perfil|…|https://origen/|id" (ver NotificationDb).
    private static readonly Regex ChromiumLaunch = new(@"^\d+\|\d+\|", RegexOptions.CultureInvariant);

    public static bool TryActivate(StrataNotification item)
    {
        string? appId = item.AppId;
        bool webApp = appId != null && AppActions.CrxId(appId) != null;
        if (item.SiteDomain is null && !webApp && !WebSites.IsBrowserId(appId))
            return false;

        // Llegó de una pestaña y el sitio está instalado como app web: la app web.
        if (!webApp && item.SiteDomain is { } site && InstalledWebApp(site) is { } app)
        {
            Log.Info($"Notificación de {site}: se abre su app web {app.Name}");
            app.Launch();
            return true;
        }

        var browser = BrowserExe(appId);
        if (browser != null && item.Launch is { } launch && ChromiumLaunch.IsMatch(launch))
        {
            try
            {
                var start = new ProcessStartInfo(browser) { UseShellExecute = false };
                start.ArgumentList.Add("--notification-launch-id=" + launch);
                Process.Start(start)?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo pasar el clic de la notificación a {Path.GetFileName(browser)}", ex);
            }
        }

        // Sin "launch" de Chromium (Firefox, o sin base de notificaciones): el sitio en el navegador.
        if (item.SiteDomain is { } domain)
        {
            try
            {
                string url = "https://" + domain + "/";
                Process.Start(browser != null ? new ProcessStartInfo(browser, url) : new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo abrir {domain}", ex);
            }
        }
        return false;
    }

    /// <summary>
    /// La app web instalada de ese sitio (Brave._crx_…), por nombre: "WhatsApp" es la de "WhatsApp" o
    /// "WhatsApp Web". Primero la del mismo nombre exacto.
    /// </summary>
    internal static StartApp? InstalledWebApp(string domain)
    {
        string name = WebSites.FromDomain(domain)?.Name ?? WebSites.DomainName(domain);
        var webApps = StartMenuService.Apps.Where(a => AppActions.CrxId(a.Id) != null).ToList();
        return webApps.FirstOrDefault(a => Matches(a.Name, name, exact: true))
               ?? webApps.FirstOrDefault(a => Matches(a.Name, name, exact: false));
    }

    internal static bool Matches(string appName, string siteName, bool exact) =>
        appName.Equals(siteName, StringComparison.CurrentCultureIgnoreCase)
        || !exact && appName.StartsWith(siteName + " ", StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// El ejecutable del navegador que envió el aviso, por su AppUserModelID ("Brave", "Brave._crx_…",
    /// "Chrome", "MSEdge"): el registrado en App Paths o, si no, el del acceso directo de Inicio.
    /// </summary>
    internal static string? BrowserExe(string? appId)
    {
        if (string.IsNullOrEmpty(appId))
            return null;
        string family = appId.Split('.')[0];
        string? exe = family.ToLowerInvariant() switch
        {
            "brave" => "brave.exe",
            "chrome" => "chrome.exe",
            "msedge" => "msedge.exe",
            "vivaldi" => "vivaldi.exe",
            "opera" => "opera.exe",
            _ => null,
        };
        if (exe is null)
            return null;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
                return path.Trim('"');
        }
        return StartMenuService.Apps.FirstOrDefault(a => a.Id.Equals(family, StringComparison.OrdinalIgnoreCase)) is { } app
               && AppActions.TargetPath(app) is { } target && Path.GetFileName(target).Equals(exe, StringComparison.OrdinalIgnoreCase)
            ? target
            : null;
    }
}
