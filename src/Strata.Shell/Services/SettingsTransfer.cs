using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Strata.Shell.Services;

/// <summary>
/// Exportar e importar la configuración (Hub › General): un .zip con settings.json y lo importado
/// (temas, animaciones, widgets, aspectos del menú Inicio y sonidos).
/// </summary>
/// <remarks>
/// Lo que es de este equipo no viaja ni se reemplaza al importar: los valores de Windows que Strata UI
/// guardó para devolverlos al cerrarse (avisos, sonidos, acento, perfil de NVIDIA) y los monitores
/// conocidos. Copiarlos de otro equipo haría que al cerrar se devolvieran valores ajenos.
/// </remarks>
public static class SettingsTransfer
{
    private const string SettingsEntry = "settings.json";
    private const string ModulesEntry = "Modules/";

    /// <summary>Guarda la configuración en la carpeta elegida; devuelve la ruta del .zip.</summary>
    public static string Export(string folder)
    {
        var copy = Settings.Deserialize(Settings.Serialize(Settings.Current));
        KeepMachineData(from: new StrataSettings(), to: copy);

        var path = Path.Combine(folder, $"StrataUI-config-{DateTime.Now:yyyy-MM-dd}.zip");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"StrataUI-config-{DateTime.Now:yyyy-MM-dd} ({i}).zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry(SettingsEntry).Open()))
            writer.Write(Settings.Serialize(copy));
        if (Directory.Exists(Modules.ModuleStore.Root))
        {
            foreach (var file in Directory.EnumerateFiles(Modules.ModuleStore.Root, "*", SearchOption.AllDirectories).Where(IsModuleFile))
            {
                var relative = Path.GetRelativePath(Modules.ModuleStore.Root, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, ModulesEntry + relative);
            }
        }
        Log.Info("Configuración exportada: " + path);
        return path;
    }

    /// <summary>
    /// Pone la configuración del .zip (o de un settings.json suelto) y sus módulos. Lanza
    /// InvalidDataException si el archivo no es una configuración de Strata UI. Quien llama reinicia
    /// Strata UI para que todo la lea.
    /// </summary>
    public static void Import(string path)
    {
        StrataSettings imported;
        try
        {
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                imported = Settings.Deserialize(File.ReadAllText(path));
            }
            else
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry(SettingsEntry) ?? throw new InvalidDataException(SettingsEntry);
                using (var reader = new StreamReader(entry.Open()))
                    imported = Settings.Deserialize(reader.ReadToEnd());
                ExtractModules(zip);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        KeepMachineData(from: Settings.Current, to: imported);
        Settings.Replace(imported);
        Log.Info("Configuración importada: " + path);
    }

    /// <summary>Módulos (.json) y sonidos importados: lo único que viaja de la carpeta Modules.</summary>
    private static bool IsModuleFile(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || Notifications.SoundLibrary.IsSound(path);

    /// <summary>Los módulos y sonidos del .zip, en su carpeta (sin salirse de ella).</summary>
    private static void ExtractModules(ZipArchive zip)
    {
        var root = Path.GetFullPath(Modules.ModuleStore.Root) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(ModulesEntry, StringComparison.Ordinal)
                || !IsModuleFile(entry.Name))
                continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName[ModulesEntry.Length..]));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void KeepMachineData(StrataSettings from, StrataSettings to)
    {
        to.WindowsBannerBackup = from.WindowsBannerBackup;
        to.WindowsSoundBackup = from.WindowsSoundBackup;
        to.WindowsAccentBackup = from.WindowsAccentBackup;
        to.WindowsAnimationBackup = from.WindowsAnimationBackup;
        to.NvidiaOriginals = from.NvidiaOriginals;
        to.MonitorNames = from.MonitorNames;
        to.MonitorsPresent = from.MonitorsPresent;
        to.ParkedMonitors = from.ParkedMonitors;
        to.TaskbarPinsImported = from.TaskbarPinsImported;
        to.LastUpdateCheck = from.LastUpdateCheck;
        to.HubPlacement = from.HubPlacement;
    }

    /// <summary>Muestra el archivo exportado en el Explorador.</summary>
    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
