using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Tweaks;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

public partial class BackupPage : Page
{
    public BackupPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var data = TweakEngine.Backup.Data;
        int count = data.Values.Count;
        BackupSummary.Text = count == 0
            ? Loc.T("No hay cambios que deshacer.")
            : Loc.F("{0} valores del registro guardados con su estado original.", count);
        RestoreButton.IsEnabled = count > 0;
        RemovedApps.Text = data.RemovedApps.Count == 0
            ? Loc.T("Ninguna.")
            : string.Join("\n", data.RemovedApps) + "\n\n" + Loc.T("Se reinstalan desde Microsoft Store.");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        RestoreButton.IsEnabled = false;
        StatusText.Text = Loc.T("Restaurando…");
        bool complete = await TweakEngine.RestoreAllAsync();
        StatusText.Text = complete
            ? Loc.T("Listo: todos los valores volvieron a su estado original.")
            : Loc.T("Se restauró lo posible. Algunos valores requieren permiso de administrador; inténtalo de nuevo.");
        Refresh();
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        RestorePointButton.IsEnabled = false;
        StatusText.Text = Loc.T("Creando punto de restauración…");
        int code = Elevation.IsAdmin
            ? TweakEngine.RunElevatedCommand(["--restore-point"])
            : await Elevation.RunSelfAsync("--restore-point");
        StatusText.Text = code switch
        {
            0 => Loc.T("Punto de restauración creado (Windows crea como máximo uno cada 24 horas)."),
            Elevation.Cancelled => Loc.T("Cancelado."),
            _ => Loc.T("No se pudo crear. Comprueba que la Protección del sistema esté activada en la unidad C:."),
        };
        RestorePointButton.IsEnabled = true;
    }
}
