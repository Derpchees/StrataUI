using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

public partial class GeneralPage : Page
{
    private static string DataFolder => Path.GetDirectoryName(Settings.FilePath)!;

    public GeneralPage()
    {
        InitializeComponent();
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
        HubAtStartupSwitch.IsChecked = Settings.Current.HubAtStartup;
        ReplaceExplorerSwitch.IsChecked = Settings.Current.FilesReplaceExplorer;
        DesktopSwitch.IsChecked = Settings.Current.DesktopEnabled;
        VersionText.Text = $"Strata UI v{App.Version}";
        DataPathText.Text = DataFolder;
        foreach (var code in Services.Localization.Loc.Translated)
            LanguageBox.Items.Add(new ComboBoxItem { Content = Services.Localization.Loc.NativeName(code), Tag = code });
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == Settings.Current.Language) ?? LanguageBox.Items[0];
        _loading = false;
        Loaded += (_, _) =>
        {
            OptimizationModule.Changed -= ShowOptimization;
            OptimizationModule.Changed += ShowOptimization;
            ShowOptimization();
        };
        Unloaded += (_, _) => OptimizationModule.Changed -= ShowOptimization;
    }

    private void ShowOptimization() => Dispatcher.BeginInvoke(() =>
        OptimizationCard.Visibility = OptimizationModule.CanRemove ? Visibility.Visible : Visibility.Collapsed);

    private void RemoveOptimization_Click(object sender, RoutedEventArgs e) => OptimizationModule.Remove();

    private bool _loading = true;

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language })
            return;
        Settings.Update(s => s.Language = language);
        // Los textos se resuelven al crear cada ventana: el cambio se ve al reiniciar.
        LanguageRestart.Visibility = Services.Localization.Loc.Resolve(language, System.Globalization.CultureInfo.CurrentUICulture)
                                     != Services.Localization.Loc.Language ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AutoStart.IsEnabled = AutoStartSwitch.IsChecked == true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el inicio automático", ex);
            AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
        }
    }

    private void HubAtStartup_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.HubAtStartup = HubAtStartupSwitch.IsChecked == true);

    private void ReplaceExplorer_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.FilesReplaceExplorer = ReplaceExplorerSwitch.IsChecked == true);

    private void Desktop_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.DesktopEnabled = DesktopSwitch.IsChecked == true);

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
    }

    // ── Tu configuración ────────────────────────────────────────────────────

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var folders = await FilePicker.PickFoldersAsync(Loc.T("Carpeta donde guardar la configuración"));
        if (folders.Length == 0)
            return;
        try
        {
            var path = SettingsTransfer.Export(folders[0]);
            ShowTransfer(Loc.F("Guardada en {0}", path));
            SettingsTransfer.Reveal(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudo exportar la configuración", ex);
            ShowTransfer(Loc.F("No se pudo exportar: {0}", ex.Message));
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var files = await FilePicker.PickFilesAsync(Loc.T("Importar configuración"),
            Loc.T("Configuración de Strata UI (*.zip, *.json)") + "|*.zip;*.json");
        if (files.Length == 0)
            return;
        try
        {
            SettingsTransfer.Import(files[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudo importar la configuración", ex);
            ShowTransfer(ex is InvalidDataException
                ? Loc.T("Ese archivo no es una configuración de Strata UI.")
                : Loc.F("No se pudo importar: {0}", ex.Message));
            return;
        }
        // Barras, menú, tema y servicios leen los ajustes al arrancar.
        App.Current.ExitShell(restart: true);
    }

    private void ShowTransfer(string text)
    {
        TransferStatus.Text = text;
        TransferStatus.Visibility = Visibility.Visible;
    }

    // ── Módulos ─────────────────────────────────────────────────────────────

    private void OpenModules_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Services.Modules.ModuleStore.Root);
        Process.Start(new ProcessStartInfo(Services.Modules.ModuleStore.Root) { UseShellExecute = true });
    }

    private void ModuleFormat_Click(object sender, RoutedEventArgs e) => ModulesSection.OpenFormat();

    private void Restart_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell(restart: true);

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell();
}
