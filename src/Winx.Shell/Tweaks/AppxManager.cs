using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Winx.Shell.Tweaks;

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
        var json = await RunPowerShellAsync(script, elevated: false);
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
    public static Task RemoveAsync(IEnumerable<string> names, bool allUsers)
    {
        var list = string.Join(",", names.Select(n => $"'{n.Replace("'", "''")}'"));
        var script = allUsers
            ? $"foreach ($n in @({list})) {{ " +
              "Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
              "Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq $n | " +
              "Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null }"
            : $"foreach ($n in @({list})) {{ Get-AppxPackage -Name $n | Remove-AppxPackage -ErrorAction SilentlyContinue }}";
        return RunPowerShellAsync(script, elevated: allUsers);
    }

    private static async Task<string> RunPowerShellAsync(string script, bool elevated)
    {
        // -EncodedCommand evita cualquier problema de comillas al pasar el script.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script));
        string[] args = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded];

        if (elevated)
        {
            int code = await Elevation.RunAsync("powershell.exe", args);
            if (code == Elevation.Cancelled)
                throw new OperationCanceledException();
            return "";
        }

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
        ["Microsoft.BingNews"] = "Noticias",
        ["Microsoft.BingWeather"] = "El Tiempo",
        ["Microsoft.BingSearch"] = "Búsqueda de Bing",
        ["Microsoft.BingFinance"] = "Finanzas",
        ["Microsoft.BingSports"] = "Deportes",
        ["Microsoft.Copilot"] = "Copilot",
        ["Microsoft.GetHelp"] = "Obtener ayuda",
        ["Microsoft.Getstarted"] = "Consejos",
        ["Microsoft.Microsoft3DViewer"] = "Visor 3D",
        ["Microsoft.MicrosoftOfficeHub"] = "Microsoft 365 (Office)",
        ["Microsoft.MicrosoftSolitaireCollection"] = "Solitaire Collection",
        ["Microsoft.MixedReality.Portal"] = "Portal de realidad mixta",
        ["Microsoft.Office.OneNote"] = "OneNote para Windows 10",
        ["Microsoft.OneConnect"] = "Planes móviles",
        ["Microsoft.People"] = "Contactos",
        ["Microsoft.PowerAutomateDesktop"] = "Power Automate",
        ["Microsoft.SkypeApp"] = "Skype",
        ["Microsoft.Todos"] = "Microsoft To Do",
        ["Microsoft.WindowsFeedbackHub"] = "Centro de opiniones",
        ["Microsoft.WindowsMaps"] = "Mapas",
        ["Microsoft.ZuneVideo"] = "Películas y TV",
        ["Microsoft.549981C3F5F10"] = "Cortana",
        ["Microsoft.OutlookForWindows"] = "Outlook (nuevo)",
        ["Microsoft.Windows.DevHome"] = "Dev Home",
        ["MicrosoftCorporationII.MicrosoftFamily"] = "Microsoft Family Safety",
        ["MicrosoftTeams"] = "Teams (personal)",
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
        ["Microsoft.ZuneMusic"] = "Reproductor multimedia",
        ["Microsoft.WindowsAlarms"] = "Reloj",
        ["Microsoft.WindowsSoundRecorder"] = "Grabadora de sonido",
        ["Microsoft.MicrosoftStickyNotes"] = "Notas rápidas",
        ["Microsoft.YourPhone"] = "Vincular teléfono",
        ["Microsoft.GamingApp"] = "Xbox",
        ["Microsoft.XboxGamingOverlay"] = "Xbox Game Bar",
        ["Microsoft.Xbox.TCUI"] = "Xbox TCUI",
        ["Microsoft.XboxIdentityProvider"] = "Xbox Identity Provider",
        ["Microsoft.XboxSpeechToTextOverlay"] = "Xbox Speech to Text",
        ["Microsoft.Paint"] = "Paint",
        ["Microsoft.ScreenSketch"] = "Recortes",
        ["Microsoft.WindowsCamera"] = "Cámara",
        ["Microsoft.Windows.Photos"] = "Fotos",
        ["Microsoft.WindowsCalculator"] = "Calculadora",
        ["Microsoft.WindowsNotepad"] = "Bloc de notas",
        ["Microsoft.WindowsTerminal"] = "Terminal",
        ["MicrosoftCorporationII.QuickAssist"] = "Asistencia rápida",
        ["Microsoft.WindowsStore"] = "Microsoft Store",
        ["Microsoft.StorePurchaseApp"] = "Compras de Microsoft Store",
        ["Microsoft.DesktopAppInstaller"] = "Instalador de aplicaciones (winget)",
        ["SpotifyAB.SpotifyMusic"] = "Spotify (Store)",
    };

    public static bool IsRecommended(string name) => Recommended.ContainsKey(name);

    public static string FriendlyName(string name) =>
        Recommended.TryGetValue(name, out var r) ? r : Known.TryGetValue(name, out var k) ? k : name;
}
