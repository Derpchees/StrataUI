using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;

namespace Winx.Shell.Hub.Pages;

public partial class GeneralPage : Page
{
    private static string DataFolder => Path.GetDirectoryName(Settings.FilePath)!;

    public GeneralPage()
    {
        InitializeComponent();
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
        VersionText.Text = $"WINX v{App.Version}";
        DataPathText.Text = DataFolder;
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
