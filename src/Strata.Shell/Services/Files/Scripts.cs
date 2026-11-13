using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Ejecutar scripts (.bat, .cmd y .ps1) desde el menú de Archivos y del escritorio, normal o como
/// administrador. La consola se queda abierta al terminar (cmd /k, PowerShell -NoExit) para ver el
/// resultado. El .ps1 corre como "Ejecutar con PowerShell" del Explorador: con la directiva en
/// Bypass solo para ese proceso (salvo que sea AllSigned). Elevado, Windows empieza en System32:
/// por eso se cambia a la carpeta del script dentro del comando.
/// </summary>
public static class Scripts
{
    public static bool IsBatch(string path) =>
        Path.GetExtension(path) is { } ext
        && (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase));

    public static bool IsPowerShell(string path) =>
        Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase);

    public static bool IsScript(string path) => IsBatch(path) || IsPowerShell(path);

    /// <summary>
    /// Agrega "Ejecutar", "Ejecutar como administrador" y "Editar" si todo lo elegido son scripts
    /// (en disco: no dentro de un comprimido ni en un servidor).
    /// </summary>
    public static void AddMenuItems(ItemsControl menu, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || !paths.All(p => IsScript(p) && File.Exists(p)))
            return;
        bool onlyPowerShell = paths.All(IsPowerShell);
        menu.Items.Add(FileMenus.Item(onlyPowerShell ? Loc.T("Ejecutar con PowerShell") : Loc.T("Ejecutar"),
                                      SymbolRegular.Play24, () => RunAll(paths, admin: false)));
        menu.Items.Add(FileMenus.Item(Loc.T("Ejecutar como administrador"), SymbolRegular.ShieldKeyhole24,
                                      () => RunAll(paths, admin: true)));
        menu.Items.Add(FileMenus.Item(Loc.T("Editar"), SymbolRegular.Edit24, () => paths.ToList().ForEach(Edit)));
    }

    private static void RunAll(IReadOnlyList<string> paths, bool admin)
    {
        foreach (var path in paths)
        {
            if (!Run(path, admin))
                Notifications.NotificationService.ShowLocal(Loc.F("No se pudo ejecutar {0}", Path.GetFileName(path)), "");
        }
    }

    /// <summary>Ejecuta el script en su carpeta. False si falló (cancelar el UAC no cuenta como fallo).</summary>
    public static bool Run(string path, bool admin)
    {
        string folder = Path.GetDirectoryName(path) ?? "";
        var (file, arguments) = Command(path, folder);
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = folder,
                Verb = admin ? "runas" : "",
            })?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: se canceló el UAC
        {
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Archivos: no se pudo ejecutar {path}", ex);
            return false;
        }
    }

    /// <summary>El programa y los argumentos que ejecutan el script (y se quedan abiertos al terminar).</summary>
    public static (string File, string Arguments) Command(string path, string folder)
    {
        if (IsPowerShell(path))
        {
            static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
            string script = $"if ((Get-ExecutionPolicy) -ne 'AllSigned') {{ Set-ExecutionPolicy -Scope Process Bypass -Force }}; "
                          + $"Set-Location -LiteralPath {Quote(folder)}; & {Quote(path)}";
            return ("powershell.exe", $"-NoExit -Command \"{script.Replace("\"", "\\\"")}\"");
        }
        // Sin comilla al principio: así cmd no quita las de las rutas.
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/k cd /d \"{folder}\" && \"{path}\"");
    }

    /// <summary>Abre el script en el Bloc de notas (abrirlo con doble clic lo ejecuta o, el .ps1, lo abre).</summary>
    public static void Edit(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Archivos: no se pudo editar {path}", ex);
        }
    }
}
