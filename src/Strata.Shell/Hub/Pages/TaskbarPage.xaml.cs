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
        };
        Unloaded += (_, _) =>
        {
            Sensors.Changed -= UpdateSensors;
            ScreenPicker.Changed -= OnScreenChanged;
        };
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
        BuildMonitorPanel();
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

    // ── Posición (del monitor elegido) ──────────────────────────────────────

    private static readonly string[] PositionNames = [Loc.T("Abajo"), Loc.T("Arriba"), Loc.T("Izquierda"), Loc.T("Derecha")];

    // En el orden de SecondBarContent.
    private static readonly string[] SecondBarNames =
        [Loc.T("Ninguna"), Loc.T("Apps abiertas"), Loc.T("Área de estado y reloj"), Loc.T("Completa")];

    private static string SecondBarDescription(TaskbarPosition position) =>
        Loc.F("En el borde opuesto ({0}). Con \"Apps abiertas\" o \"Área de estado y reloj\" las dos barras se reparten el contenido; \"Completa\" repite todo.",
              PositionNames[(int)StrataSettings.Opposite(position)]);

    /// <summary>Borde de la barra, si tiene barra (el principal de Strata UI siempre) y la segunda barra.</summary>
    private void BuildMonitorPanel()
    {
        MonitorPanel.Children.Clear();
        string device = Device;
        bool isMain = device == ThemeManager.MainDevice;

        var position = new ComboBox { MinWidth = 140, ItemsSource = PositionNames, SelectedIndex = (int)Settings.Current.PositionOf(device) };
        var secondText = new TextBlock { Style = (Style)FindResource("CardDescription"), Text = SecondBarDescription(Settings.Current.PositionOf(device)) };
        var second = new ComboBox { MinWidth = 200, ItemsSource = SecondBarNames, SelectedIndex = (int)Settings.Current.SecondBarOf(device) };
        position.SelectionChanged += (_, _) =>
        {
            if (_loading || position.SelectedIndex < 0)
                return;
            Settings.Update(s => s.TaskbarPositions[device] = (TaskbarPosition)position.SelectedIndex);
            secondText.Text = SecondBarDescription((TaskbarPosition)position.SelectedIndex);
        };
        second.SelectionChanged += (_, _) =>
        {
            if (_loading || second.SelectedIndex < 0)
                return;
            var content = (SecondBarContent)second.SelectedIndex;
            Settings.Update(s =>
            {
                if (content == SecondBarContent.None)
                    s.SecondTaskbars.Remove(device);
                else
                    s.SecondTaskbars[device] = content;
            });
        };

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("Borde de la pantalla") });
        header.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            Text = isMain ? Loc.T("Es el principal de Strata UI: siempre tiene barra.") : Loc.T("El interruptor muestra u oculta la barra en este monitor."),
        });
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(position);
        if (!isMain)
        {
            var visible = new Wpf.Ui.Controls.ToggleSwitch
            {
                Margin = new Thickness(16, 0, 0, 0),
                IsChecked = !Settings.Current.TaskbarHiddenScreens.Contains(device),
                ToolTip = Loc.T("Mostrar la barra en este monitor"),
            };
            visible.Click += (_, _) =>
            {
                bool show = visible.IsChecked == true;
                position.IsEnabled = second.IsEnabled = show;
                Settings.Update(s =>
                {
                    s.TaskbarHiddenScreens.Remove(device);
                    if (!show)
                        s.TaskbarHiddenScreens.Add(device);
                });
            };
            position.IsEnabled = second.IsEnabled = visible.IsChecked == true;
            controls.Children.Add(visible);
        }
        MonitorPanel.Children.Add(new Wpf.Ui.Controls.CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Desktop24 },
            Header = header,
            Content = controls,
        });

        var secondHeader = new StackPanel();
        secondHeader.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("Segunda barra") });
        secondHeader.Children.Add(secondText);
        MonitorPanel.Children.Add(new Wpf.Ui.Controls.CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowBidirectionalUpDown24 },
            Header = secondHeader,
            Content = second,
        });
    }

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
        var device = (string?)item.Tag;
        // El principal de Strata UI siempre tiene barra: si estaba oculta, se vuelve a mostrar.
        Settings.Update(s =>
        {
            s.TaskbarMainScreen = device;
            if (device != null)
                s.TaskbarHiddenScreens.Remove(device);
        });
        _loading = true;
        BuildMonitorPanel();
        _loading = false;
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
