using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Strata.Shell.Services;

/// <summary>Acciones sobre las apps de la barra que van más allá de la ventana.</summary>
public static class TaskActions
{
    /// <summary>
    /// Termina el proceso dueño de la ventana (como "Finalizar tarea" de Windows). Devuelve false si
    /// no se debe terminar (el Explorador, Strata UI mismo), null si Windows no lo permitió (una app
    /// elevada) y true si se terminó.
    /// </summary>
    public static bool? ForceClose(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId)
            return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                return false;
            Log.Info($"Forzar cierre: {process.ProcessName} ({pid})");
            process.Kill();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            // Ya terminó, o es de administrador y Strata UI no.
            Log.Info($"No se pudo forzar el cierre del proceso {pid}: {ex.Message}");
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
