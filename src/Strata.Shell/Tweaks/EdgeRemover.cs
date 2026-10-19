using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Quita Microsoft Edge (el navegador; WebView2, que usan otras apps, se queda). Windows solo deja
/// desinstalarlo en el Espacio Económico Europeo: el desinstalador del propio Edge, con el permiso de
/// EdgeUpdate y el ejecutable del Edge antiguo en SystemApps que pide (si falta, uno vacío que se borra
/// después), dice "Browser/WebView is sticky, uninstall not allowed" en las demás regiones. Ahí se
/// desactiva en su lugar (<see cref="Disable"/>): sin accesos directos, sin arranque rápido ni procesos
/// en segundo plano, y sus actualizaciones no vuelven a crear accesos directos. Su servicio de
/// actualización se queda (también actualiza WebView2). <see cref="RestoreArg"/> lo deshace.
/// </summary>
/// <remarks>Corre en un proceso elevado (<c>--remove-edge</c> / <c>--restore-edge</c>, ver App.OnStartup) y solo hace esto.</remarks>
public static class EdgeRemover
{
    public const string Arg = "--remove-edge";
    public const string RestoreArg = "--restore-edge";

    /// <summary>Códigos de salida del proceso elevado.</summary>
    public const int Removed = 0, Failed = 1, NoUninstaller = 2, Disabled = 4;

    private static string EdgeFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application");

    /// <summary>Lo que se quitó al desactivarlo (accesos directos y los valores de antes de las directivas).</summary>
    private static string BackupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "EdgeDisabled");

    private static string ManifestPath => Path.Combine(BackupFolder, "manifest.json");

    /// <summary>Edge está instalado (su navegador, no WebView2).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(EdgeFolder, "msedge.exe"));

    /// <summary>Sigue instalado pero desactivado por Strata UI.</summary>
    public static bool IsDisabled => IsInstalled && File.Exists(ManifestPath);

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

    /// <summary>Punto de entrada del proceso elevado: desinstala o, si Windows no deja, desactiva.</summary>
    public static int RunElevated()
    {
        try
        {
            if (Setup() is { } setup)
                Uninstall(setup);
            if (!IsInstalled)
            {
                Services.Log.Info("Microsoft Edge desinstalado");
                return Removed;
            }
            Services.Log.Info("Windows no deja desinstalar Edge en esta región: se desactiva");
            Disable();
            return Disabled;
        }
        catch (Exception ex)
        {
            Services.Log.Error("No se pudo quitar Microsoft Edge", ex);
            return Failed;
        }
    }

    private static void Uninstall(string setup)
    {
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

        if (!IsInstalled)
        {
            // Que Windows Update no lo vuelva a instalar.
            using var update = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\EdgeUpdate", writable: true);
            update.SetValue("DoNotUpdateToEdgeWithChromium", 1, RegistryValueKind.DWord);
        }
    }

    // ── Desactivar ──────────────────────────────────────────────────────────

    /// <summary>Directivas de Edge que lo desactivan: (clave de HKLM, valor, dato).</summary>
    private static readonly (string Key, string Name, int Value)[] Policies =
    [
        (@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0),
        (@"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0),
        (@"SOFTWARE\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault", 0),
    ];

    private static IEnumerable<string> Shortcuts()
    {
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs,
                     Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.DesktopDirectory,
                 })
        {
            string path = Path.Combine(Environment.GetFolderPath(folder), "Microsoft Edge.lnk");
            if (File.Exists(path))
                yield return path;
        }
    }

    private sealed record Manifest(List<string> Shortcuts, Dictionary<string, int?> Policies);

    /// <summary>Guarda y quita sus accesos directos y pone sus directivas (lo de antes queda en el respaldo).</summary>
    private static void Disable()
    {
        Directory.CreateDirectory(BackupFolder);
        var manifest = File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath)) ?? new([], [])
            : new Manifest([], []);

        foreach (var shortcut in Shortcuts())
        {
            int index = manifest.Shortcuts.IndexOf(shortcut);
            if (index < 0)
            {
                manifest.Shortcuts.Add(shortcut);
                index = manifest.Shortcuts.Count - 1;
            }
            File.Copy(shortcut, Path.Combine(BackupFolder, $"{index}.lnk"), overwrite: true);
            File.Delete(shortcut);
        }

        foreach (var (keyName, name, value) in Policies)
        {
            using var key = Registry.LocalMachine.CreateSubKey(keyName, writable: true);
            string id = keyName + "|" + name;
            if (!manifest.Policies.ContainsKey(id))
                manifest.Policies[id] = key.GetValue(name) is int before ? before : null;
            key.SetValue(name, value, RegistryValueKind.DWord);
        }

        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
        Services.Log.Info($"Edge desactivado: {manifest.Shortcuts.Count} accesos directos guardados");
    }

    /// <summary>Punto de entrada del proceso elevado que deshace <see cref="Disable"/>.</summary>
    public static int RunElevatedRestore()
    {
        try
        {
            if (!File.Exists(ManifestPath))
                return Removed;
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath)) ?? new([], []);
            for (int i = 0; i < manifest.Shortcuts.Count; i++)
            {
                string saved = Path.Combine(BackupFolder, $"{i}.lnk");
                if (File.Exists(saved) && !File.Exists(manifest.Shortcuts[i]))
                    File.Copy(saved, manifest.Shortcuts[i]);
            }
            foreach (var (id, before) in manifest.Policies)
            {
                var (keyName, name) = (id[..id.IndexOf('|')], id[(id.IndexOf('|') + 1)..]);
                // Solo las directivas de la lista: el respaldo no decide qué se escribe.
                if (!Policies.Any(p => p.Key == keyName && p.Name == name))
                    continue;
                using var key = Registry.LocalMachine.OpenSubKey(keyName, writable: true);
                if (key is null)
                    continue;
                if (before is { } value)
                    key.SetValue(name, value, RegistryValueKind.DWord);
                else
                    key.DeleteValue(name, throwOnMissingValue: false);
            }
            Directory.Delete(BackupFolder, recursive: true);
            Services.Log.Info("Edge restaurado");
            return Removed;
        }
        catch (Exception ex)
        {
            Services.Log.Error("No se pudo restaurar Microsoft Edge", ex);
            return Failed;
        }
    }
}
