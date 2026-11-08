using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Hub;

/// <summary>
/// Lecturas avanzadas (temperaturas y FPS, con su permiso y PawnIO) y Liberar RAM, en Hub › Lecturas
/// avanzadas (<see cref="Pages.SensorsPage"/>).
/// </summary>
public partial class PerformanceTools : UserControl
{
    private bool _loading = true;

    public PerformanceTools()
    {
        InitializeComponent();
        SensorsStartupSwitch.IsChecked = Settings.Current.SensorsAtStartup;
        UpdateSensors();
        _loading = false;
        Loaded += (_, _) =>
        {
            Sensors.Changed += UpdateSensors;
            OptionalModules.Changed += OnModuleChanged;
            UpdateSensors();
        };
        Unloaded += (_, _) =>
        {
            Sensors.Changed -= UpdateSensors;
            OptionalModules.Changed -= OnModuleChanged;
        };
    }

    private async void FreeRam_Click(object sender, RoutedEventArgs e)
    {
        FreeRamButton.IsEnabled = false;
        FreeRamButton.Content = Loc.T("Liberando RAM…");
        try
        {
            ulong before = SystemMonitor.AvailableRam();
            if (!await Sensors.FreeRamAsync())
            {
                FreeRamButton.Content = Loc.T("No se liberó la RAM");
                return;
            }
            ulong after = SystemMonitor.AvailableRam();
            double freed = after > before ? (after - before) / 1073741824.0 : 0;
            FreeRamButton.Content = freed >= 0.05 ? Loc.F("RAM liberada: {0:0.0} GB", freed) : Loc.T("La memoria ya estaba libre");
        }
        finally
        {
            FreeRamButton.IsEnabled = true;
        }
    }

    private void Global_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            Settings.Update(s => s.SensorsAtStartup = SensorsStartupSwitch.IsChecked == true);
    }

    private void UpdateSensors() => Dispatcher.BeginInvoke(() =>
    {
        // Las temperaturas y los FPS vienen en el módulo Lecturas avanzadas.
        var module = OptionalModules.Sensors;
        if (!module.IsInstalled)
        {
            SensorsState.Text = module.IsDownloading ? Loc.T("Descargando…")
                : module.ErrorText ?? Loc.T("Necesitan el módulo opcional Lecturas avanzadas.");
            SensorsButton.Content = Loc.T("Instalar el módulo");
            SensorsButton.IsEnabled = !module.IsDownloading;
            SensorsRemoveButton.Visibility = Visibility.Collapsed;
            PawnIoBar.IsOpen = false;
            PawnIoButton.Visibility = Visibility.Collapsed;
            return;
        }
        SensorsRemoveButton.Visibility = Visibility.Visible;
        SensorsState.Text = Sensors.Starting ? Loc.T("Iniciando… si Windows pide permiso, acéptalo.")
            : Sensors.Running ? Loc.T("Activas: temperaturas y FPS hasta que se cierre Strata UI.")
                + (Sensors.SlowParts.Count > 0
                    ? "\n" + Loc.F("Se dejaron de leer por lentas (frenaban al equipo): {0}.", string.Join(", ", Sensors.SlowParts))
                    : "")
            : Loc.T("Detenidas. También se inician con un clic en el contador de FPS.");
        SensorsButton.Content = Sensors.Running ? Loc.T("Detener") : Loc.T("Iniciar");
        // Detenidas porque el permiso quedó viejo (se actualizó Strata UI o el módulo): los medidores no
        // las inician solos (nunca preguntan); se avisa para que se inicien a mano una vez.
        if (!Sensors.Running && !Sensors.Starting)
            _ = ShowPermissionNeededAsync();
        SensorsButton.IsEnabled = SensorsRemoveButton.IsEnabled = !Sensors.Starting;
        PawnIoBar.IsOpen = !Sensors.PawnIoInstalled;
        PawnIoButton.Visibility = PawnIoBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
    });

    private async Task ShowPermissionNeededAsync()
    {
        if (await Task.Run(SensorsTask.IsReady) || Sensors.Running || Sensors.Starting || !OptionalModules.Sensors.IsInstalled)
            return;
        SensorsState.Text = Loc.T("Necesitan permiso de administrador otra vez (Strata UI o el módulo se actualizó): pulsa Iniciar y acepta el aviso de Windows. Hasta entonces, los medidores no muestran temperaturas ni FPS.");
    }

    private void OnModuleChanged(OptionalModule module)
    {
        if (module == OptionalModules.Sensors)
            UpdateSensors();
    }

    /// <summary>
    /// Instala PawnIO con winget (su instalador pide permiso a Windows) y reinicia las lecturas
    /// para que la temperatura de la CPU aparezca. Sin winget, abre su página.
    /// </summary>
    private async void PawnIo_Click(object sender, RoutedEventArgs e)
    {
        PawnIoButton.IsEnabled = false;
        PawnIoButton.Content = Loc.T("Instalando…");
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("winget",
                "install --id namazso.PawnIO -e --silent --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = System.Diagnostics.Process.Start(info)!)
                await process.WaitForExitAsync();
            if (Sensors.PawnIoInstalled)
            {
                Log.Info("PawnIO instalado desde el Hub");
                Sensors.Stop();
                await Sensors.StartAsync();
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Sin winget: la página de PawnIO.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true })?.Dispose();
        }
        finally
        {
            PawnIoButton.IsEnabled = true;
            PawnIoButton.Content = Loc.T("Instalar PawnIO");
            UpdateSensors();
        }
    }

    private async void Sensors_Click(object sender, RoutedEventArgs e)
    {
        if (!OptionalModules.Sensors.IsInstalled)
        {
            await OptionalModules.Sensors.InstallAsync();
            return;
        }
        if (Sensors.Running)
            Sensors.Stop();
        else
            await Sensors.StartAsync();
    }

    private async void SensorsRemove_Click(object sender, RoutedEventArgs e)
    {
        Sensors.Stop();
        await SensorsTask.RemoveAsync();
        UpdateSensors();
    }
}
