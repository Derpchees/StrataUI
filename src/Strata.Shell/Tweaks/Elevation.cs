using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Strata UI corre sin privilegios. Lo que requiere administrador se ejecuta en un proceso
/// elevado de corta vida (UAC) que hace solo esa tarea y termina.
/// </summary>
public static class Elevation
{
    public const int Cancelled = 1223; // ERROR_CANCELLED: el usuario rechazó UAC

    public static bool IsAdmin { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Ejecuta StrataUI.exe elevado con los argumentos dados y devuelve su código de salida.</summary>
    /// <remarks>
    /// Instalado para todos los usuarios, lo de la lista fija lo ejecuta el proceso elevado de las
    /// lecturas avanzadas sin UAC (<see cref="ElevatedBroker"/>); lo demás, o si no se puede, con UAC.
    /// </remarks>
    public static async Task<int> RunSelfAsync(params string[] args)
    {
        if (!IsAdmin && await Services.Sensors.RunElevatedAsync(args) is { } code)
            return code;
        return await RunAsync(Environment.ProcessPath!, args);
    }

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
