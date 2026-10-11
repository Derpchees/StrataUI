using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Tweaks;

namespace Winx.Shell.Hub.Pages;

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
            ? "No hay cambios que deshacer."
            : $"{count} valores del registro guardados con su estado original.";
        RestoreButton.IsEnabled = count > 0;
        RemovedApps.Text = data.RemovedApps.Count == 0
            ? "Ninguna."
            : string.Join("\n", data.RemovedApps) + "\n\nSe reinstalan desde Microsoft Store.";
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        RestoreButton.IsEnabled = false;
        StatusText.Text = "Restaurando…";
        bool complete = await TweakEngine.RestoreAllAsync();
        StatusText.Text = complete
            ? "Listo: todos los valores volvieron a su estado original."
            : "Se restauró lo posible. Algunos valores requieren permiso de administrador; inténtalo de nuevo.";
        Refresh();
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        RestorePointButton.IsEnabled = false;
        StatusText.Text = "Creando punto de restauración…";
        int code = Elevation.IsAdmin
            ? TweakEngine.RunElevatedCommand(["--restore-point"])
            : await Elevation.RunSelfAsync("--restore-point");
        StatusText.Text = code switch
        {
            0 => "Punto de restauración creado (Windows crea como máximo uno cada 24 horas).",
            Elevation.Cancelled => "Cancelado.",
            _ => "No se pudo crear. Comprueba que la Protección del sistema esté activada en la unidad C:.",
        };
        RestorePointButton.IsEnabled = true;
    }
}
