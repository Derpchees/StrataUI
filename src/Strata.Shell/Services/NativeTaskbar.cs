using ManagedShell.AppBar;
using ManagedShell.Interop;

namespace Strata.Shell.Services;

/// <summary>Restaura la barra de tareas de Windows sin depender del estado de Strata UI.</summary>
public static class NativeTaskbar
{
    private const int SWP_SHOWWINDOW = 0x0040;

    public static void Restore()
    {
        try
        {
            var explorer = new ExplorerHelper();
            explorer.SetTaskbarState(NativeMethods.ABState.Default);
            explorer.SetTaskbarVisibility(SWP_SHOWWINDOW);
            explorer.SetSecondaryTaskbarVisibility(SWP_SHOWWINDOW);
            Log.Info("Barra de tareas de Windows restaurada");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo restaurar la barra de Windows", ex);
        }
    }
}
