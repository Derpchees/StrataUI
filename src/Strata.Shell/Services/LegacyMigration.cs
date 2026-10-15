using System.Diagnostics;
using System.IO;

namespace Strata.Shell.Services;

/// <summary>
/// Strata UI se llamaba WINX hasta la 1.3. La primera vez que abre, pasa los datos de
/// %LOCALAPPDATA%\WINX (configuración, registro, notificaciones, íconos de bandeja…) a
/// %LOCALAPPDATA%\StrataUI y corrige los valores guardados que cambiaron de nombre. El inicio
/// automático lo migra <see cref="AutoStart.Migrate"/> y las lecturas avanzadas,
/// <see cref="SensorsTask"/> al renovar su permiso.
/// </summary>
public static class LegacyMigration
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string OldFolder = Path.Combine(LocalAppData, "WINX");
    private static readonly string NewFolder = Path.Combine(LocalAppData, "StrataUI");

    /// <summary>Se llama antes de leer la configuración o escribir el registro.</summary>
    public static void Run()
    {
        if (!Directory.Exists(OldFolder) || Directory.Exists(NewFolder) || LegacyRunning())
            return;
        try
        {
            Directory.Move(OldFolder, NewFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Algún archivo en uso: se copia y la carpeta vieja queda como estaba.
            Copy(OldFolder, NewFolder);
        }
        FixSettings(Path.Combine(NewFolder, "settings.json"));
        DeleteOldLogs(Path.Combine(NewFolder, "logs"));
        Log.Info("Datos de WINX pasados a Strata UI");
    }

    /// <summary>
    /// WINX sigue abierto (el instalador lo cierra, pero alguien puede abrir los dos): mover sus
    /// archivos debajo de él lo dejaría a medias. Solo cuentan los procesos que se pueden leer: el de
    /// lecturas avanzadas corre elevado y no usa esta carpeta.
    /// </summary>
    private static bool LegacyRunning()
    {
        foreach (var process in Process.GetProcessesByName("WINX"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule != null)
                        return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
        }
        return false;
    }

    /// <summary>El registro ahora es strataui.log: los de WINX (winx.log, winx.old.log) sobran.</summary>
    private static void DeleteOldLogs(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder, "winx*.log"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Valores guardados con el nombre viejo: rutas a la carpeta de datos, el botón del Hub y el ícono de Inicio.</summary>
    internal static string FixSettingsText(string json)
    {
        // En el JSON las barras invertidas van dobles.
        string oldPath = OldFolder.Replace(@"\", @"\\") + @"\\";
        string newPath = NewFolder.Replace(@"\", @"\\") + @"\\";
        return json
            .Replace(oldPath, newPath, StringComparison.OrdinalIgnoreCase)
            .Replace("\"WinxSettings\"", "\"StrataSettings\"")
            .Replace("\"StartIcon\": \"Winx\"", "\"StartIcon\": \"Strata\"");
    }

    private static void FixSettings(string path)
    {
        try
        {
            if (File.Exists(path))
                File.WriteAllText(path, FixSettingsText(File.ReadAllText(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
