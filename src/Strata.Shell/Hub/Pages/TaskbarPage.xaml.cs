using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Barra de tareas del monitor elegido arriba (<see cref="ScreenPicker"/>): qué lleva, su aspecto,
/// posición, reloj y temperaturas. Abajo, lo que vale para todos los monitores.
/// </summary>
public partial class TaskbarPage : Page
{
    private bool _loading = true;

    public TaskbarPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        Select(AppsModeBox, s.TaskbarAppsMode.ToString());
        BuildMainScreenBox();
        SensorsStartupSwitch.IsChecked = s.SensorsAtStartup;
        BuildFpsScreens();
        UpdateSensors();
        LoadScreen();
        _loading = false;
        Loaded += (_, _) =>
        {
            Sensors.Changed += UpdateSensors;
            ScreenPicker.Changed += OnScreenChanged;
            EditMode.EditSession.ActiveChanged += UpdateEditLibrary;
            UpdateEditLibrary();
        };
        Unloaded += (_, _) =>
        {
            Sensors.Changed -= UpdateSensors;
            ScreenPicker.Changed -= OnScreenChanged;
            EditMode.EditSession.ActiveChanged -= UpdateEditLibrary;
        };
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

    /// <summary>Los ajustes del monitor elegido.</summary>
    private void LoadScreen()
    {
        var s = Settings.Current.For(Device);
        Select(AlignmentBox, s.TaskbarAlignment.ToString());
        CenterAppsSwitch.IsChecked = s.TaskbarCenterApps;
        PreviewsSwitch.IsChecked = s.ShowTaskPreviews;
        CombineSwitch.IsChecked = s.CombineTaskWindows;
        HeightBox.SelectedItem = HeightBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == s.TaskbarHeight.ToString()) ?? HeightBox.Items[1];
        SecondsSwitch.IsChecked = s.ShowSeconds;
        DateSwitch.IsChecked = s.ShowDate;
        TemperaturesSwitch.IsChecked = s.ShowTemperatures;
        BatteryPercentSwitch.IsChecked = s.ShowBatteryPercent;
        Select(BarStyleBox, s.BarStyle.ToString());
        BuildBarColor();
    }

    /// <summary>El editor del color propio de la barra de este monitor (solo con ese estilo).</summary>
    private void BuildBarColor()
    {
        string device = Device;
        BarColorHost.Content = Settings.Current.For(device).BarStyle != BarStyle.Color ? null
            : ColorsPage.CustomColorCard(this, Loc.T("Color de la barra"), Loc.T("Con su opacidad: a menos, más se ve el material del tema detrás."),
                Wpf.Ui.Controls.SymbolRegular.Color24,
                () => Settings.Current.For(device).BarColor,
                (st, value) =>
                {
                    if (!st.Screens.TryGetValue(device, out var screen))
                        st.Screens[device] = screen = st.DefaultScreen();
                    screen.BarColor = value;
                },
                toggle: false);
    }

    private void BarStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BarStyleBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        var style = Enum.Parse<BarStyle>(tag);
        Settings.UpdateScreen(Device, s =>
        {
            s.BarStyle = style;
            // Al elegir color propio por primera vez, uno oscuro y algo transparente para empezar.
            if (style == BarStyle.Color && s.BarColor is null)
                s.BarColor = "#E61C1C1C";
        });
        BuildBarColor();
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Settings.UpdateScreen(Device, s =>
        {
            s.TaskbarCenterApps = CenterAppsSwitch.IsChecked == true;
            s.ShowTaskPreviews = PreviewsSwitch.IsChecked == true;
            s.CombineTaskWindows = CombineSwitch.IsChecked == true;
            s.ShowSeconds = SecondsSwitch.IsChecked == true;
            s.ShowDate = DateSwitch.IsChecked == true;
            s.ShowTemperatures = TemperaturesSwitch.IsChecked == true;
            s.ShowBatteryPercent = BatteryPercentSwitch.IsChecked == true;
        });
    }

    private void Alignment_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AlignmentBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.UpdateScreen(Device, s => s.TaskbarAlignment = Enum.Parse<TaskbarAlignment>(tag));
    }

    private void Height_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && HeightBox.SelectedItem is ComboBoxItem { Tag: string tag })
            Settings.UpdateScreen(Device, s => s.TaskbarHeight = int.Parse(tag));
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e) => BarEditor.Reset();

    private void EditLayout_Click(object sender, RoutedEventArgs e) => EditMode.EditSession.Start();

    // ── Para todos los monitores ────────────────────────────────────────────

    private void BuildMainScreenBox()
    {
        var screens = ScreenPicker.Screens();
        MultiMonitorOptions.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        MainScreenBox.Items.Add(new ComboBoxItem { Content = Loc.T("Igual que Windows"), Tag = null });
        for (int i = 0; i < screens.Count; i++)
            MainScreenBox.Items.Add(new ComboBoxItem
            {
                Content = screens.Count == 1 ? Loc.T("Monitor") : Loc.F("Monitor {0}", i + 1),
                Tag = screens[i].DeviceName,
            });
        string? chosen = Settings.Current.TaskbarMainScreen;
        MainScreenBox.SelectedItem = MainScreenBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == chosen)
                                     ?? MainScreenBox.Items[0];
    }

    private void MainScreen_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MainScreenBox.SelectedItem is not ComboBoxItem item)
            return;
        Settings.Update(s => s.TaskbarMainScreen = (string?)item.Tag);
    }

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

    private void BuildFpsScreens()
    {
        var screens = ScreenPicker.Screens();
        FpsScreenBox.Items.Add(new ComboBoxItem { Content = Loc.T("Automático: la app activa"), Tag = null });
        for (int i = 0; i < screens.Count; i++)
            FpsScreenBox.Items.Add(new ComboBoxItem { Content = ScreenPicker.NameOf(screens[i], i, screens.Count), Tag = screens[i].DeviceName });
        string? chosen = Settings.Current.FpsScreen;
        FpsScreenBox.SelectedItem = FpsScreenBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == chosen)
                                    ?? FpsScreenBox.Items[0];
    }

    private void FpsScreen_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && FpsScreenBox.SelectedItem is ComboBoxItem item)
            Settings.Update(s => s.FpsScreen = (string?)item.Tag);
    }

    private void UpdateSensors()
    {
        SensorsState.Text = Sensors.Starting ? Loc.T("Iniciando… si Windows pide permiso, acéptalo.")
            : Sensors.Running ? Loc.T("Activas: temperaturas y FPS hasta que se cierre Strata UI.")
            : Loc.T("Detenidas. También se inician con un clic en el contador de FPS.");
        SensorsButton.Content = Sensors.Running ? Loc.T("Detener") : Loc.T("Iniciar");
        SensorsButton.IsEnabled = SensorsRemoveButton.IsEnabled = !Sensors.Starting;
        PawnIoBar.IsOpen = !Sensors.PawnIoInstalled;
        PawnIoButton.Visibility = PawnIoBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
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
