using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Strata.Shell.Services;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Bloqueo de anuncios en todo el equipo: manda a 0.0.0.0 los servidores de publicidad en el
/// archivo hosts, entre dos marcas propias (quitarlo solo borra ese bloque). Sirve para cualquier
/// programa (Blitz, CurseForge, Modrinth, navegadores…) porque Windows lo consulta antes que el DNS.
/// </summary>
/// <remarks>
/// Sin dominios de Microsoft: Defender marca como secuestro un hosts que los redirige. Los anuncios
/// de Windows se quitan con los ajustes del registro de la misma página.
/// </remarks>
public static class HostsAdBlock
{
    public const string Arg = "--adblock";

    private const string Begin = "# >>> Strata UI: bloqueo de anuncios";
    private const string End = "# <<< Strata UI: bloqueo de anuncios";

    private static string HostsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    // Redes de anuncios que usan las apps (Google Ad Manager y los socios de Prebid de los que cargan
    // Blitz, Overwolf/CurseForge y Modrinth) y los sitios web.
    internal static readonly string[] Domains =
    [
        "doubleclick.net", "ad.doubleclick.net", "securepubads.g.doubleclick.net", "pubads.g.doubleclick.net",
        "googleads.g.doubleclick.net", "stats.g.doubleclick.net", "static.doubleclick.net", "cm.g.doubleclick.net",
        "pagead2.googlesyndication.com", "tpc.googlesyndication.com", "googlesyndication.com",
        "adservice.google.com", "partner.googleadservices.com", "www.googleadservices.com", "googleadservices.com",
        "adnxs.com", "ib.adnxs.com", "secure.adnxs.com", "acdn.adnxs.com",
        "rubiconproject.com", "fastlane.rubiconproject.com", "pixel.rubiconproject.com", "prebid-server.rubiconproject.com",
        "pubmatic.com", "hbopenbid.pubmatic.com", "ads.pubmatic.com", "image6.pubmatic.com",
        "openx.net", "rtb.openx.net", "us-u.openx.net",
        "casalemedia.com", "htlb.casalemedia.com", "as-sec.casalemedia.com",
        "criteo.com", "bidder.criteo.com", "static.criteo.net", "gum.criteo.com",
        "amazon-adsystem.com", "aax.amazon-adsystem.com", "c.amazon-adsystem.com", "s.amazon-adsystem.com",
        "3lift.com", "tlx.3lift.com", "eb2.3lift.com",
        "sharethrough.com", "btlr.sharethrough.com",
        "adsrvr.org", "match.adsrvr.org", "js.adsrvr.org",
        "smartadserver.com", "prg.smartadserver.com",
        "teads.tv", "a.teads.tv", "t.teads.tv",
        "outbrain.com", "widgets.outbrain.com", "log.outbrainimg.com",
        "taboola.com", "cdn.taboola.com", "trc.taboola.com",
        "adform.net", "adx.adform.net", "track.adform.net",
        "yieldmo.com", "ads.yieldmo.com",
        "media.net", "contextual.media.net",
        "gumgum.com", "g2.gumgum.com",
        "sovrn.com", "ap.lijit.com", "lijit.com",
        "districtm.io", "indexww.com",
        "moatads.com", "z.moatads.com", "px.moatads.com",
        "adsafeprotected.com", "pixel.adsafeprotected.com", "static.adsafeprotected.com",
        "doubleverify.com", "cdn.doubleverify.com", "tps.doubleverify.com",
        "adroll.com", "d.adroll.com",
        "quantserve.com", "pixel.quantserve.com",
        "scorecardresearch.com", "sb.scorecardresearch.com",
        "advertising.com", "adtech.de",
        "zedo.com", "popads.net", "popcash.net", "propellerads.com", "adsterra.com", "exoclick.com",
        "ads.yahoo.com", "analytics.yahoo.com",
        "ads.twitter.com", "ads-api.twitter.com",
        "an.facebook.com",
        "unityads.unity3d.com", "auction.unityads.unity3d.com",
        "applovin.com", "a.applovin.com", "rt.applovin.com",
        "vungle.com", "api.vungle.com",
        "chartboost.com", "live.chartboost.com",
        "inmobi.com", "ads.inmobi.com",
        "adcolony.com", "ads30.adcolony.com",
        "venatusmedia.com", "hb.vntsm.com", "vntsm.com",
        "nitropay.com", "s.nitropay.com",
        "aditude.io",
        "playwire.com", "cdn.intergient.com", "cdn.intergi.com",
        "ad-delivery.net", "adition.com", "connatix.com", "cd.connatix.com",
        "primis.tech", "video.primis.tech", "springserve.com", "vid.springserve.com",
        "33across.com", "ssc.33across.com", "e-planning.net", "smaato.net",
        "mgid.com", "servicer.mgid.com", "revcontent.com", "trends.revcontent.com",
        "zemanta.com", "b1sync.zemanta.com", "bidswitch.net", "x.bidswitch.net",
        "rlcdn.com", "bluekai.com", "krxd.net",
    ];

    /// <summary>El bloque de Strata UI está en el archivo hosts (leerlo no necesita administrador).</summary>
    public static bool IsOn
    {
        get
        {
            try
            {
                return File.ReadAllText(HostsPath).Contains(Begin, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Lo pone o lo quita (con UAC si hace falta). Devuelve si quedó como se pidió.</summary>
    public static async Task<ApplyResult> SetAsync(bool on)
    {
        try
        {
            if (Elevation.IsAdmin)
                Write(on);
            else
            {
                int code = await Elevation.RunSelfAsync(Arg, on ? "on" : "off");
                if (code == Elevation.Cancelled)
                    return ApplyResult.Cancelled;
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el bloqueo de anuncios", ex);
            return ApplyResult.Failed;
        }
        Log.Info($"Bloqueo de anuncios (hosts) → {on}");
        return IsOn == on ? ApplyResult.Applied : ApplyResult.Failed;
    }

    /// <summary>Proceso elevado: <c>--adblock on|off</c>.</summary>
    internal static int RunElevated(string[] args)
    {
        if (args.Length != 2 || args[1] is not ("on" or "off"))
            return 1;
        try
        {
            Write(args[1] == "on");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo escribir el archivo hosts", ex);
            return 1;
        }
    }

    private static void Write(bool on)
    {
        var path = HostsPath;
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var updated = Apply(text, on);
        if (updated == text)
            return;
        if (File.Exists(path))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        File.WriteAllText(path, updated, new UTF8Encoding(false));
        DnsFlushResolverCache();
    }

    /// <summary>El texto del hosts sin el bloque de Strata UI y, si <paramref name="on"/>, con él al final.</summary>
    internal static string Apply(string text, bool on)
    {
        int start = text.IndexOf(Begin, StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = text.IndexOf(End, start, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end + End.Length;
            text = (text[..start].TrimEnd('\r', '\n') + text[end..].TrimStart('\r', '\n')).TrimEnd('\r', '\n');
            text = text.Length > 0 ? text + "\r\n" : text;
        }
        if (!on)
            return text;

        var block = new StringBuilder();
        if (text.Length > 0)
            block.Append("\r\n");
        block.Append(Begin).Append("\r\n");
        foreach (var domain in Domains.Distinct(StringComparer.OrdinalIgnoreCase))
            block.Append("0.0.0.0 ").Append(domain).Append("\r\n");
        block.Append(End).Append("\r\n");
        return text + block;
    }

    [DllImport("dnsapi.dll")]
    private static extern bool DnsFlushResolverCache();
}
