using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Winx.Shell.Services;

/// <summary>
/// Permiso de administrador una sola vez para las lecturas avanzadas: la primera vez (con UAC) se
/// crea una tarea programada del usuario con privilegios elevados; después WINX la inicia sin
/// preguntar. La tarea no ejecuta el WINX instalado en la carpeta del usuario (cualquier programa
/// sin permisos podría reemplazarlo y obtener administrador): usa una copia en
/// %ProgramFiles%\WINX\Lecturas, donde solo escriben administradores. Al actualizar WINX la copia
/// queda vieja y se renueva pidiendo permiso otra vez.
/// </summary>
public static class SensorsTask
{
    public const string InstallArg = "--install-sensors-task";
    public const string RemoveArg = "--remove-sensors-task";
    public const string TaskArg = "--sensors-task";

    private static readonly Regex Sid = new(@"^S-1-5-21(-\d+){3,4}$");

    public static string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? "";

    /// <summary>Nombre de la tarea (una por usuario).</summary>
    public static string TaskName(string sid) => $"WINX lecturas avanzadas ({ShortHash(sid)})";

    /// <summary>Tubería fija por usuario: la tarea no puede recibir argumentos nuevos cada vez.</summary>
    public static string PipeName(string sid) => "WINX-sensors-" + ShortHash(sid, 32);

    private static string ShortHash(string text, int length = 8) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..length];

    /// <summary>Carpeta de la copia protegida (solo se usa si WINX está instalado en la carpeta del usuario).</summary>
    public static string CopyFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WINX", "Lecturas");

    /// <summary>
    /// WINX ya está en Archivos de programa (instalado para todos los usuarios): esa carpeta solo la
    /// cambian administradores, así que la tarea puede usarlo tal cual.
    /// </summary>
    private static bool InstalledProtected =>
        IsUnder(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
        && !IsUnder(AppContext.BaseDirectory, CopyFolder);

    private static bool IsUnder(string path, string folder) =>
        Path.GetFullPath(path).TrimEnd('\\').StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lo que ejecuta la tarea: WINX mismo si está protegido; si no, su copia.</summary>
    private static string ProtectedExe => InstalledProtected
        ? Path.Combine(AppContext.BaseDirectory, "WINX.exe")
        : Path.Combine(CopyFolder, "WINX.exe");

    /// <summary>La tarea existe y su copia es de esta versión de WINX (si no, hay que renovarla con UAC).</summary>
    public static bool IsReady()
    {
        try
        {
            // Misma versión completa (incluye el commit) y el mismo WINX.dll: una copia de otra
            // compilación se renueva (dos compilaciones del mismo commit tienen la misma versión).
            if (!File.Exists(ProtectedExe)
                || FileVersionInfo.GetVersionInfo(ProtectedExe).ProductVersion
                   != FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion
                || !SameFile(Path.Combine(Path.GetDirectoryName(ProtectedExe)!, "WINX.dll"),
                             Path.Combine(AppContext.BaseDirectory, "WINX.dll")))
                return false;
            return RunSchtasks($"/Query /TN \"{TaskName(UserSid)}\"") == 0;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Mismo tamaño y fecha (la copia conserva la fecha del original).</summary>
    private static bool SameFile(string a, string b)
    {
        var x = new FileInfo(a);
        var y = new FileInfo(b);
        return x.Exists && y.Exists && x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc;
    }

    /// <summary>Crea o renueva la tarea (pide UAC). Devuelve false si se canceló o falló.</summary>
    public static async Task<bool> InstallAsync()
    {
        int code = await Tweaks.Elevation.RunSelfAsync(InstallArg, UserSid);
        if (code != 0)
            Log.Info($"No se creó la tarea de lecturas avanzadas (código {code})");
        return code == 0;
    }

    /// <summary>Inicia el proceso de lecturas con la tarea (sin UAC).</summary>
    public static bool Run() => RunSchtasks($"/Run /TN \"{TaskName(UserSid)}\"") == 0;

    /// <summary>Quita la tarea y la copia (pide UAC).</summary>
    public static Task<int> RemoveAsync() => Tweaks.Elevation.RunSelfAsync(RemoveArg, UserSid);

    // ── Proceso elevado ─────────────────────────────────────────────────────

    /// <summary>
    /// WINX.exe --install-sensors-task &lt;SID&gt;: copia WINX a la carpeta protegida y crea la tarea
    /// para ese usuario (con su SID: si quien acepta UAC es otra cuenta de administrador, la tarea
    /// igual queda para el usuario de WINX).
    /// </summary>
    public static int RunInstall(string[] args)
    {
        if (args.Length != 2 || !Sid.IsMatch(args[1]))
            return 2;
        string sid = args[1];
        try
        {
            if (!InstalledProtected)
                CopyApp(AppContext.BaseDirectory, CopyFolder);
            string xml = TaskXml(sid, ProtectedExe);
            string file = Path.Combine(Path.GetTempPath(), $"winx-task-{Guid.NewGuid():N}.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            try
            {
                return RunSchtasks($"/Create /TN \"{TaskName(sid)}\" /XML \"{file}\" /F") == 0 ? 0 : 3;
            }
            finally
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 4;
        }
    }

    /// <summary>
    /// WINX.exe --remove-sensors-task [SID]: borra la tarea (la de ese usuario o, sin SID, todas las
    /// de WINX: así lo usa el desinstalador) y la copia protegida.
    /// </summary>
    public static int RunRemove(string[] args)
    {
        if (args.Length == 2 && Sid.IsMatch(args[1]))
            RunSchtasks($"/Delete /TN \"{TaskName(args[1])}\" /F");
        else if (args.Length == 1)
            foreach (var name in AllTasks())
                RunSchtasks($"/Delete /TN \"{name}\" /F");
        else
            return 2;
        try
        {
            // Solo la copia; nunca la carpeta donde está instalado WINX.
            if (Directory.Exists(CopyFolder) && !IsUnder(AppContext.BaseDirectory, CopyFolder))
                Directory.Delete(CopyFolder, recursive: true);
            var parent = Path.GetDirectoryName(CopyFolder)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 4;
        }
        return 0;
    }

    /// <summary>Copia la app (reemplaza la copia anterior). Si WINX ya está ahí, no hace nada.</summary>
    private static void CopyApp(string from, string to)
    {
        from = Path.GetFullPath(from).TrimEnd('\\');
        to = Path.GetFullPath(to).TrimEnd('\\');
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return;
        if (Directory.Exists(to))
            Directory.Delete(to, recursive: true);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    /// <summary>
    /// Tarea a pedido (sin desencadenador), del usuario y con sus privilegios más altos, solo con
    /// la sesión iniciada, sin límite de tiempo y aunque el equipo esté con batería.
    /// </summary>
    internal static string TaskXml(string sid, string exe) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Temperaturas, FPS y liberar RAM para WINX (las inicia WINX).</Description>
          </RegistrationInfo>
          <Principals>
            <Principal id="Author">
              <UserId>{sid}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
            <AllowStartOnDemand>true</AllowStartOnDemand>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"{System.Security.SecurityElement.Escape(exe)}"</Command>
              <Arguments>{TaskArg}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    /// <summary>Tareas de lecturas de WINX de cualquier usuario ("\WINX lecturas avanzadas (…)").</summary>
    private static IEnumerable<string> AllTasks()
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Query /FO CSV /NH")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(15000);
        return Regex.Matches(output, @"^""\\(WINX lecturas avanzadas \([0-9a-f]{8}\))""", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    private static int RunSchtasks(string arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        return process.HasExited ? process.ExitCode : -1;
    }
}
