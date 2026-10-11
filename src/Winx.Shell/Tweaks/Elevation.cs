using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Winx.Shell.Tweaks;

/// <summary>
/// WINX corre sin privilegios. Lo que requiere administrador se ejecuta en un proceso
/// elevado de corta vida (UAC) que hace solo esa tarea y termina.
/// </summary>
public static class Elevation
{
    public const int Cancelled = 1223; // ERROR_CANCELLED: el usuario rechazó UAC

    public static bool IsAdmin { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Ejecuta WINX.exe elevado con los argumentos dados y devuelve su código de salida.</summary>
    public static Task<int> RunSelfAsync(params string[] args) =>
        RunAsync(Environment.ProcessPath!, args);

    public static async Task<int> RunAsync(string fileName, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(info)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return Cancelled;
        }
    }
}
