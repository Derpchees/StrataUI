using System.IO;
using Microsoft.Win32;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Desinstala la Game Bar de Xbox (Win+G) del usuario actual y apaga la grabación de juegos en
/// segundo plano. Sin la Game Bar, Windows abre sus vínculos (ms-gamingoverlay, ms-gamebar) al
/// iniciar un juego y pide "una aplicación para abrirlo": esos vínculos se mandan a systray.exe,
/// que no hace nada. Si la Game Bar se vuelve a instalar, <see cref="RepairIfReinstalled"/> los devuelve.
/// </summary>
/// <remarks>No pide administrador: el paquete se quita para este usuario y todo lo demás es de HKCU.</remarks>
public static class GameBarRemover
{
    private const string Family = "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe";

    /// <summary>La Game Bar actual y la de Windows 10 (las de sistema, como XboxGameCallableUI, no se pueden quitar).</summary>
    private static readonly string[] Packages = ["Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay"];

    private static readonly string[] Protocols = ["ms-gamebar", "ms-gamebarservices", "ms-gamingoverlay"];

    /// <summary>Marca en los vínculos que escribió Strata UI (para quitarlos solo a ellos).</summary>
    private const string Marker = "StrataUI";

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return new Windows.Management.Deployment.PackageManager().FindPackagesForUser("", Family).Any();
            }
            catch (Exception ex)
            {
                Services.Log.Error("No se pudo saber si la Game Bar está instalada", ex);
                return false;
            }
        }
    }

    /// <summary>Quita la Game Bar; true si ya no está.</summary>
    public static async Task<bool> RemoveAsync()
    {
        Services.Log.Info("Desinstalando la Game Bar de Xbox");
        await AppxManager.RemoveAsync(Packages, allUsers: false);
        bool removed = !IsInstalled;
        if (removed)
        {
            SilenceProtocols();
            DisableCapture();
        }
        Services.Log.Info(removed ? "Game Bar desinstalada" : "La Game Bar sigue instalada después de quitarla");
        return removed;
    }

    /// <summary>
    /// La Game Bar ya no está pero quedan sus restos: los vínculos sin app (Windows pide una al abrir
    /// juegos) o la grabación en segundo plano encendida.
    /// </summary>
    public static bool NeedsCleanup
    {
        get
        {
            if (IsInstalled)
                return false;
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
            bool silenced = classes != null && Protocols.All(p =>
            {
                using var key = classes.OpenSubKey(p);
                return key != null;
            });
            using var store = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            return !silenced || store?.GetValue("GameDVR_Enabled") is not 0;
        }
    }

    /// <summary>Quita los restos de una Game Bar ya desinstalada (ver <see cref="NeedsCleanup"/>).</summary>
    public static void CleanUp()
    {
        SilenceProtocols();
        DisableCapture();
        Services.Log.Info("Restos de la Game Bar quitados");
    }

    /// <summary>Si la Game Bar volvió (desde la Tienda), sus vínculos vuelven a abrirla.</summary>
    public static void RepairIfReinstalled()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classes is null || !Protocols.Any(p => IsOurs(classes, p)) || !IsInstalled)
                return;
            foreach (var protocol in Protocols.Where(p => IsOurs(classes, p)))
                classes.DeleteSubKeyTree(protocol, throwOnMissingSubKey: false);
            Services.Log.Info("La Game Bar se reinstaló: sus vínculos vuelven a abrirla");
        }
        catch (Exception ex)
        {
            Services.Log.Error("No se pudieron devolver los vínculos de la Game Bar", ex);
        }
    }

    private static bool IsOurs(RegistryKey classes, string protocol)
    {
        using var key = classes.OpenSubKey(protocol);
        return key?.GetValue(Marker) is not null;
    }

    private static void SilenceProtocols()
    {
        string systray = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "systray.exe");
        foreach (var protocol in Protocols)
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);
            // Una asociación propia del usuario (que no sea de Strata UI) se respeta.
            using (var existing = classes.OpenSubKey(protocol))
            {
                if (existing != null && existing.GetValue(Marker) is null)
                    continue;
            }
            using var key = classes.CreateSubKey(protocol, writable: true);
            key.SetValue("", "URL:" + protocol);
            key.SetValue("URL Protocol", "");
            key.SetValue("NoOpenWith", "");
            key.SetValue(Marker, 1, RegistryValueKind.DWord);
            using var command = key.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue("", $"\"{systray}\"");
        }
    }

    private static void DisableCapture()
    {
        using (var store = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", writable: true))
            store.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
        using (var dvr = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", writable: true))
            dvr.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
    }
}
