using System.Windows.Media;
using ManagedShell.WindowsTasks;
using Strata.Shell.Services.WebApps;

namespace Strata.Shell.Services;

/// <summary>
/// A qué app pertenece cada ventana de la barra (para agruparlas) y con qué ícono se muestra. Una
/// pestaña del navegador con un sitio conocido (YouTube, WhatsApp Web…) cuenta como esa app.
/// </summary>
public static class TaskGroups
{
    /// <summary>
    /// El sitio que muestra una ventana del navegador. Las apps web instaladas (AppUserModelID con
    /// "_crx_") ya traen su propio ícono y no hace falta reconocerlas.
    /// </summary>
    public static WebApp? Site(ApplicationWindow window) =>
        WebSites.IsBrowserExe(window.WinFileName) && window.AppUserModelID?.Contains("_crx_") != true
            ? WebSites.FromWindowTitle(window.Title)
            : null;

    /// <summary>Clave del grupo: el sitio, la identidad de la app (AppUserModelID) o su ejecutable.</summary>
    public static string Key(ApplicationWindow window)
    {
        if (Site(window) is { } site)
            return "web:" + site.Domain;
        if (!string.IsNullOrEmpty(window.AppUserModelID))
            return window.AppUserModelID;
        return window.WinFileName?.ToLowerInvariant() ?? window.Handle.ToString();
    }

    /// <summary>Ícono del botón: el del sitio si es una app web (cuando ya se descargó), si no el de la ventana.</summary>
    public static ImageSource? Icon(ApplicationWindow window) =>
        Site(window) is { } site ? WebSites.Icon(site.Domain) ?? window.Icon : window.Icon;
}
