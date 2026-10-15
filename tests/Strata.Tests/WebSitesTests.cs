using Strata.Shell.Services.WebApps;

namespace Strata.Tests;

/// <summary>Reconocer YouTube, WhatsApp Web… por el título de la pestaña o el dominio.</summary>
public sealed class WebSitesTests
{
    [Theory]
    [InlineData("Lofi hip hop radio - YouTube - Brave", "YouTube")]
    [InlineData("(3) Lofi hip hop radio - YouTube - Google Chrome", "YouTube")]
    [InlineData("YouTube - Brave", "YouTube")]
    [InlineData("Estoy muy bien - YouTube Music - Brave", "YouTube Music")]
    [InlineData("WhatsApp - Brave", "WhatsApp")]
    [InlineData("(5) WhatsApp - Brave", "WhatsApp")]
    [InlineData("WhatsApp Business - Google Chrome", "WhatsApp")]
    [InlineData("Recibidos (12) - yo@gmail.com - Gmail - Brave", "Gmail")]
    [InlineData("Netflix - Personal - Microsoft\u200b Edge", "Netflix")]
    [InlineData("Spotify – Web Player - Mozilla Firefox", "Spotify")]
    [InlineData("Juan (@juan) / X - Brave", "X")]
    [InlineData("Nueva conversación - Claude - Brave", "Claude")]
    public void Tabs_are_recognized_by_title(string windowTitle, string expected) =>
        Assert.Equal(expected, WebSites.FromWindowTitle(windowTitle)?.Name);

    [Theory]
    [InlineData("NEXUS · Comercializadora y Distribuidora ASD - Brave")]
    [InlineData("Cómo usar YouTube para tu negocio - Blog - Brave")]
    [InlineData("WhatsApp para empresas: guía - Google Chrome")]
    [InlineData("Visual Studio Code")]
    [InlineData("")]
    public void Other_pages_are_not_web_apps(string windowTitle) =>
        Assert.Null(WebSites.FromWindowTitle(windowTitle));

    [Theory]
    [InlineData("Video - YouTube - Brave", "Video - YouTube")]
    [InlineData("Inbox - Personal - Microsoft\u200b Edge", "Inbox")]
    [InlineData("Página - Mozilla Firefox", "Página")]
    [InlineData("Sin navegador", "Sin navegador")]
    public void Browser_name_is_removed_from_the_title(string windowTitle, string expected) =>
        Assert.Equal(expected, WebSites.PageTitle(windowTitle));

    [Theory]
    [InlineData("web.whatsapp.com", "WhatsApp")]
    [InlineData("www.youtube.com", "YouTube")]
    [InlineData("music.youtube.com", "YouTube Music")]
    [InlineData("mail.google.com", "Gmail")]
    public void Notification_domains_map_to_sites(string domain, string expected) =>
        Assert.Equal(expected, WebSites.FromDomain(domain)?.Name);

    [Theory]
    [InlineData("web.whatsapp.com", "web.whatsapp.com")]
    [InlineData("https://www.ejemplo.com/", "www.ejemplo.com")]
    [InlineData("Hola, ¿cómo estás?", null)]
    [InlineData("Nuevo mensaje de Ana", null)]
    public void Attribution_text_is_read_as_domain(string text, string? expected) =>
        Assert.Equal(expected, WebSites.AsDomain(text));

    [Theory]
    [InlineData("Brave", true)]
    [InlineData("Chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData(null, false)]
    public void Browser_ids_are_recognized(string? id, bool expected) =>
        Assert.Equal(expected, WebSites.IsBrowserId(id));
}
