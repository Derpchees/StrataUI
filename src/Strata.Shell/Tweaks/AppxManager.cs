using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Tweaks;

public sealed record AppPackage(string Name, string FullName)
{
    public string Label => BloatCatalog.FriendlyName(Name);
    public bool Recommended => BloatCatalog.IsRecommended(Name);
}

/// <summary>Lista y quita apps de la Store (paquetes Appx) mediante PowerShell.</summary>
public static class AppxManager
{
    public static async Task<IReadOnlyList<AppPackage>> ListAsync()
    {
        const string script =
            "ConvertTo-Json -Compress -InputObject @(Get-AppxPackage | " +
            "Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | " +
            "Select-Object Name, PackageFullName)";
        var json = await RunPowerShellAsync(script);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new AppPackage(e.GetProperty("Name").GetString()!, e.GetProperty("PackageFullName").GetString()!))
            .DistinctBy(p => p.Name)
            .OrderByDescending(p => p.Recommended)
            .ThenBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Quita las apps del usuario actual; con <paramref name="allUsers"/> también de los demás
    /// usuarios y de la imagen (para cuentas nuevas), lo que requiere administrador.
    /// </summary>
    public static async Task RemoveAsync(IEnumerable<string> names, bool allUsers)
    {
        if (!allUsers)
        {
            await RunPowerShellAsync(UserScript(names));
            return;
        }
        // Con administrador: Strata UI mismo (--remove-appx), que solo acepta nombres de paquete; así
        // también lo puede hacer el proceso elevado sin UAC (ver ElevatedBroker).
        int code = await Elevation.RunSelfAsync([ElevatedBroker.RemoveAppxArg, .. names]);
        if (code == Elevation.Cancelled)
            throw new OperationCanceledException();
    }

    private static string Quoted(IEnumerable<string> names) => string.Join(",", names.Select(n => $"'{n.Replace("'", "''")}'"));

    private static string UserScript(IEnumerable<string> names) =>
        $"foreach ($n in @({Quoted(names)})) {{ Get-AppxPackage -Name $n | Remove-AppxPackage -ErrorAction SilentlyContinue }}";

    /// <summary>
    /// StrataUI.exe --remove-appx nombre… (elevado): quita esas apps de todos los usuarios y de la imagen
    /// (para cuentas nuevas). Solo nombres de paquete (letras, números, puntos, guiones).
    /// </summary>
    public static int RunElevatedRemove(string[] args)
    {
        var names = args.Skip(1).ToList();
        if (names.Count == 0 || !names.All(ElevatedBroker.IsPackageName))
            return 2;
        string script = $"foreach ($n in @({Quoted(names)})) {{ " +
                        "Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
                        "Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq $n | " +
                        "Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null }";
        try
        {
            // Fuera del hilo de la UI: esperarlo ahí con la tarea en curso lo dejaría trabado.
            Task.Run(() => RunPowerShellAsync(script)).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Services.Log.Error("No se pudieron quitar las apps para todos los usuarios", ex);
            return 1;
        }
    }

    /// <summary>PowerShell en este proceso (elevado solo si Strata UI ya lo está: ver RunElevatedRemove).</summary>
    private static async Task<string> RunPowerShellAsync(string script)
    {
        // -EncodedCommand evita cualquier problema de comillas al pasar el script.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script));
        string[] args = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded];

        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}

/// <summary>Nombres legibles y apps que Win11Debloat considera prescindibles por defecto.</summary>
public static class BloatCatalog
{
    // Recomendadas: publicidad, apps promocionadas y servicios de Microsoft que casi nadie usa.
    private static readonly Dictionary<string, string> Recommended = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.BingNews"] = Loc.T("Noticias"),
        ["Microsoft.BingWeather"] = Loc.T("El Tiempo"),
        ["Microsoft.BingSearch"] = Loc.T("Búsqueda de Bing"),
        ["Microsoft.BingFinance"] = Loc.T("Finanzas"),
        ["Microsoft.BingSports"] = Loc.T("Deportes"),
        ["Microsoft.Copilot"] = "Copilot",
        ["Microsoft.GetHelp"] = Loc.T("Obtener ayuda"),
        ["Microsoft.Getstarted"] = Loc.T("Consejos"),
        ["Microsoft.Microsoft3DViewer"] = Loc.T("Visor 3D"),
        ["Microsoft.MicrosoftOfficeHub"] = "Microsoft 365 (Office)",
        ["Microsoft.MicrosoftSolitaireCollection"] = "Solitaire Collection",
        ["Microsoft.MixedReality.Portal"] = Loc.T("Portal de realidad mixta"),
        ["Microsoft.Office.OneNote"] = Loc.T("OneNote para Windows 10"),
        ["Microsoft.OneConnect"] = Loc.T("Planes móviles"),
        ["Microsoft.People"] = Loc.T("Contactos"),
        ["Microsoft.PowerAutomateDesktop"] = "Power Automate",
        ["Microsoft.SkypeApp"] = "Skype",
        ["Microsoft.Todos"] = "Microsoft To Do",
        ["Microsoft.WindowsFeedbackHub"] = Loc.T("Centro de opiniones"),
        ["Microsoft.WindowsMaps"] = Loc.T("Mapas"),
        ["Microsoft.ZuneVideo"] = Loc.T("Películas y TV"),
        ["Microsoft.549981C3F5F10"] = "Cortana",
        ["Microsoft.OutlookForWindows"] = Loc.T("Outlook (nuevo)"),
        ["Microsoft.Windows.DevHome"] = "Dev Home",
        ["MicrosoftCorporationII.MicrosoftFamily"] = "Microsoft Family Safety",
        ["MicrosoftTeams"] = Loc.T("Teams (personal)"),
        ["MSTeams"] = "Microsoft Teams",
        ["Clipchamp.Clipchamp"] = "Clipchamp",
        ["Disney.37853FC22B2CE"] = "Disney+",
        ["4DF9E0F8.Netflix"] = "Netflix",
        ["AmazonVideo.PrimeVideo"] = "Prime Video",
        ["BytedancePte.Ltd.TikTok"] = "TikTok",
        ["Facebook.Facebook"] = "Facebook",
        ["Facebook.InstagramBeta"] = "Instagram",
        ["7EE7776C.LinkedInforWindows"] = "LinkedIn",
        ["king.com.CandyCrushSaga"] = "Candy Crush Saga",
        ["king.com.CandyCrushSodaSaga"] = "Candy Crush Soda Saga",
        ["king.com.BubbleWitch3Saga"] = "Bubble Witch 3 Saga",
        ["Duolingo-LearnLanguagesforFree"] = "Duolingo",
        ["AdobeSystemsIncorporated.AdobePhotoshopExpress"] = "Photoshop Express",
        ["Flipboard.Flipboard"] = "Flipboard",
        ["ShazamEntertainmentLtd.Shazam"] = "Shazam",
        ["PandoraMediaInc.29680B314EFC2"] = "Pandora",
        ["9E2F88E3.Twitter"] = "Twitter",
    };

    // Conocidas pero útiles para muchos: aparecen con nombre legible, sin marcar.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.ZuneMusic"] = Loc.T("Reproductor multimedia"),
        ["Microsoft.WindowsAlarms"] = Loc.T("Reloj"),
        ["Microsoft.WindowsSoundRecorder"] = Loc.T("Grabadora de sonido"),
        ["Microsoft.MicrosoftStickyNotes"] = Loc.T("Notas rápidas"),
        ["Microsoft.YourPhone"] = Loc.T("Vincular teléfono"),
        ["Microsoft.GamingApp"] = "Xbox",
        ["Microsoft.XboxGamingOverlay"] = "Xbox Game Bar",
        ["Microsoft.Xbox.TCUI"] = "Xbox TCUI",
        ["Microsoft.XboxIdentityProvider"] = "Xbox Identity Provider",
        ["Microsoft.XboxSpeechToTextOverlay"] = "Xbox Speech to Text",
        ["Microsoft.Paint"] = "Paint",
        ["Microsoft.ScreenSketch"] = Loc.T("Recortes"),
        ["Microsoft.WindowsCamera"] = Loc.T("Cámara"),
        ["Microsoft.Windows.Photos"] = Loc.T("Fotos"),
        ["Microsoft.WindowsCalculator"] = Loc.T("Calculadora"),
        ["Microsoft.WindowsNotepad"] = Loc.T("Bloc de notas"),
        ["Microsoft.WindowsTerminal"] = "Terminal",
        ["MicrosoftCorporationII.QuickAssist"] = Loc.T("Asistencia rápida"),
        ["Microsoft.WindowsStore"] = "Microsoft Store",
        ["Microsoft.StorePurchaseApp"] = Loc.T("Compras de Microsoft Store"),
        ["Microsoft.DesktopAppInstaller"] = Loc.T("Instalador de aplicaciones (winget)"),
        ["SpotifyAB.SpotifyMusic"] = "Spotify (Store)",
    };

    public static bool IsRecommended(string name) => Recommended.ContainsKey(name);

    public static string FriendlyName(string name) =>
        Recommended.TryGetValue(name, out var r) ? r : Known.TryGetValue(name, out var k) ? k : name;
}
