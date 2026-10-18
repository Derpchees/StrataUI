using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Desinstala Microsoft Edge (el navegador; WebView2, que usan otras apps, se queda). Windows solo lo
/// permite en algunas regiones: aquí se usa el desinstalador del propio Edge con el permiso de
/// EdgeUpdate y, como pide el instalador fuera de esas regiones, el ejecutable del Edge antiguo en
/// SystemApps (si falta, uno vacío que se borra después). Luego se evita que Windows Update lo
/// reinstale. Se vuelve a instalar desde microsoft.com/edge.
/// </summary>
/// <remarks>Corre en un proceso elevado (<c>--remove-edge</c>, ver App.OnStartup) y solo hace esto.</remarks>
public static class EdgeRemover
{
    public const string Arg = "--remove-edge";

    private static string EdgeFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application");

    /// <summary>Edge está instalado (su navegador, no WebView2).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(EdgeFolder, "msedge.exe"));

    /// <summary>El desinstalador de la versión instalada más nueva (null si no hay).</summary>
    private static string? Setup()
    {
        if (!Directory.Exists(EdgeFolder))
            return null;
        return Directory.EnumerateDirectories(EdgeFolder)
            .Select(dir => (Dir: dir, Version: Version.TryParse(Path.GetFileName(dir), out var v) ? v : null))
            .Where(d => d.Version != null)
            .OrderByDescending(d => d.Version)
            .Select(d => Path.Combine(d.Dir, "Installer", "setup.exe"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Punto de entrada del proceso elevado. 0 = quitado; 2 = no había desinstalador; 3 = sigue instalado.</summary>
    public static int RunElevated()
    {
        try
        {
            if (Setup() is not { } setup)
                return IsInstalled ? 2 : 0;
            Services.Log.Info("Desinstalando Microsoft Edge: " + setup);

            // El permiso que el desinstalador consulta fuera de las regiones donde Windows lo deja quitar.
            using (var dev = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                       .CreateSubKey(@"SOFTWARE\Microsoft\EdgeUpdateDev", writable: true))
                dev.SetValue("AllowUninstall", 1, RegistryValueKind.DWord);

            string legacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                               "SystemApps", "Microsoft.MicrosoftEdge_8wekyb3d8bbwe");
            string legacyExe = Path.Combine(legacyFolder, "MicrosoftEdge.exe");
            bool createdLegacy = false;
            if (Directory.Exists(legacyFolder) && !File.Exists(legacyExe))
            {
                File.WriteAllBytes(legacyExe, []);
                createdLegacy = true;
            }
            try
            {
                using var process = Process.Start(new ProcessStartInfo(setup)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { "--uninstall", "--system-level", "--verbose-logging", "--force-uninstall" },
                });
                process?.WaitForExit(5 * 60 * 1000);
            }
            finally
            {
                if (createdLegacy)
                    File.Delete(legacyExe);
            }

            // Que Windows Update no lo vuelva a instalar.
            using (var update = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\EdgeUpdate", writable: true))
                update.SetValue("DoNotUpdateToEdgeWithChromium", 1, RegistryValueKind.DWord);

            bool removed = !IsInstalled;
            Services.Log.Info(removed ? "Microsoft Edge desinstalado" : "Microsoft Edge sigue instalado después del desinstalador");
            return removed ? 0 : 3;
        }
        catch (Exception ex)
        {
            Services.Log.Error("No se pudo desinstalar Microsoft Edge", ex);
            return 1;
        }
    }
}
