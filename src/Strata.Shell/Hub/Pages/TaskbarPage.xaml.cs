using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Barra de tareas: el modo edición (donde se arma el diseño de cada monitor y las opciones de cada
/// barra y elemento), un resumen de lo que lleva cada monitor, las apps abiertas del monitor elegido
/// (<see cref="ScreenPicker"/>) y, para todos, en qué barras aparecen y las lecturas avanzadas.
/// </summary>
public partial class TaskbarPage : Page
{
    private bool _loading = true;

    public TaskbarPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        Select(AppsModeBox, s.TaskbarAppsMode.ToString());
        AppsModeCard.Visibility = ScreenPicker.Screens().Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SensorsStartupSwitch.IsChecked = s.SensorsAtStartup;
        UpdateSensors();
        LoadScreen();
        BuildSummary();
        _loading = false;
        Loaded += (_, _) =>
        {
            Sensors.Changed += UpdateSensors;
            OptionalModules.Changed += OnModuleChanged;
            ScreenPicker.Changed += OnScreenChanged;
            Settings.Changed += BuildSummary;
            EditMode.EditSession.ActiveChanged += UpdateEditLibrary;
            UpdateEditLibrary();
        };
        Unloaded += (_, _) =>
        {
            Sensors.Changed -= UpdateSensors;
            OptionalModules.Changed -= OnModuleChanged;
            ScreenPicker.Changed -= OnScreenChanged;
            Settings.Changed -= BuildSummary;
            EditMode.EditSession.ActiveChanged -= UpdateEditLibrary;
        };
    }

    /// <summary>Qué barras tiene cada monitor y qué lleva cada una (se cambia en el modo edición).</summary>
    private void BuildSummary()
    {
        LayoutSummary.Children.Clear();
        var screens = ScreenPicker.Screens();
        for (int i = 0; i < screens.Count; i++)
        {
            var layout = Settings.Current.Layouts.GetValueOrDefault(screens[i].DeviceName);
            LayoutSummary.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardTitle"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 2),
                Text = ScreenPicker.NameOf(screens[i], i, screens.Count),
            });
            IEnumerable<string> lines = layout is null || layout.Bars.Count == 0
                ? [Loc.T("Sin barra")]
                : BarLayout.InCreationOrder(layout).Select(bar => BarLayout.BarName(bar.Edge) + ": "
                    + (bar.Items.Count == 0 ? Loc.T("vacía") : string.Join(", ", bar.Items.Select(BarLayout.Name))));
            foreach (var line in lines)
                LayoutSummary.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap, Text = line });
        }
    }

    /// <summary>Mientras el modo edición está abierto, su biblioteca también se arrastra desde aquí.</summary>
    private void UpdateEditLibrary()
    {
        bool active = EditMode.EditSession.IsActive;
        EditLibraryHost.Child = active ? new EditMode.EditLibrary(null) : null;
        EditLibraryHost.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Device => ScreenPicker.Device;

    private void OnScreenChanged()
    {
        _loading = true;
        LoadScreen();
        _loading = false;
    }

    /// <summary>Las opciones de las apps abiertas del monitor elegido.</summary>
    private void LoadScreen()
    {
        var s = Settings.Current.For(Device);
        PreviewsSwitch.IsChecked = s.ShowTaskPreviews;
        CombineSwitch.IsChecked = s.CombineTaskWindows;
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.UpdateScreen(Device, s =>
        {
            s.ShowTaskPreviews = PreviewsSwitch.IsChecked == true;
            s.CombineTaskWindows = CombineSwitch.IsChecked == true;
        });
    }

    private void EditLayout_Click(object sender, RoutedEventArgs e) => EditMode.EditSession.Start();

    // ── Para todos los monitores ────────────────────────────────────────────

    private void AppsMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AppsModeBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.Update(s => s.TaskbarAppsMode = Enum.Parse<TaskbarAppsMode>(tag));
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true });
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
