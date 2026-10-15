using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;

namespace Strata.Shell.Hub.Pages;

public partial class GeneralPage : Page
{
    private static string DataFolder => Path.GetDirectoryName(Settings.FilePath)!;

    public GeneralPage()
    {
        InitializeComponent();
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
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

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell(restart: true);

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell();
}
