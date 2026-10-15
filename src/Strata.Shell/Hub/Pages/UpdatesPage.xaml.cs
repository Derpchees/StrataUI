using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Updates;
using Wpf.Ui.Controls;

namespace Strata.Shell.Hub.Pages;

public partial class UpdatesPage : Page
{
    public UpdatesPage()
    {
        InitializeComponent();
        AutoSwitch.IsChecked = Settings.Current.AutoCheckUpdates;
        VersionText.Text = Loc.F("Versión instalada: {0}", App.Version);
        SourceText.Text = Loc.F("Las versiones se publican en github.com/{0}", UpdateService.Repository);
        UpdateService.Changed += OnChanged;
        Unloaded += (_, _) => UpdateService.Changed -= OnChanged;
        Refresh();
        // Al entrar se busca si nunca se buscó en esta sesión.
        Loaded += async (_, _) =>
        {
            if (UpdateService.State == UpdateState.Idle)
                await UpdateService.CheckAsync();
        };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var state = UpdateService.State;
        var update = UpdateService.Available;
        Checking.Visibility = state == UpdateState.Checking ? Visibility.Visible : Visibility.Collapsed;
        StateIcon.Visibility = state == UpdateState.Checking ? Visibility.Collapsed : Visibility.Visible;
        CheckButton.IsEnabled = state is not (UpdateState.Checking or UpdateState.Downloading);
        InstallButton.Visibility = update?.InstallerUrl != null && state is UpdateState.Available or UpdateState.Downloading
            ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.IsEnabled = state != UpdateState.Downloading;
        DownloadProgress.Visibility = state == UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Value = UpdateService.Progress;

        string lastCheck = Settings.Current.LastUpdateCheck is { } when
            ? Loc.F("Última búsqueda: {0}", when.ToLocalTime().ToString("g"))
            : Loc.T("Todavía no se ha buscado.");

        (StateIcon.Symbol, StateTitle.Text, StateDetail.Text) = state switch
        {
            UpdateState.Checking => (SymbolRegular.ArrowSync24, Loc.T("Buscando actualizaciones…"), lastCheck),
            UpdateState.Available or UpdateState.Downloading when update != null =>
                (SymbolRegular.ArrowCircleDown24, Loc.F("Strata UI {0} está disponible", update.Version.ToString(3)),
                 state == UpdateState.Downloading ? Loc.F("Descargando… {0:0}%", UpdateService.Progress * 100)
                 : update.InstallerUrl is null ? Loc.T("Esta versión no trae instalador: descárgala desde GitHub.")
                 : Loc.T("Strata UI se cerrará un momento para instalarla y volverá a abrirse.")),
            UpdateState.UpToDate => (SymbolRegular.CheckmarkCircle24, Loc.T("Strata UI está al día"), lastCheck),
            UpdateState.Error => (SymbolRegular.ErrorCircle24, UpdateService.ErrorText ?? Loc.T("No se pudo buscar actualizaciones."), lastCheck),
            _ => (SymbolRegular.ArrowSync24, Loc.T("Busca si hay una versión nueva"), lastCheck),
        };
        StateIcon.SetResourceReference(IconElement.ForegroundProperty, state switch
        {
            UpdateState.UpToDate => "SystemFillColorSuccessBrush",
            UpdateState.Available or UpdateState.Downloading => "StrataAccentBrush",
            UpdateState.Error => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        });

        NotesPanel.Visibility = update != null && update.Notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesText.Text = update?.Notes.Replace("### ", "").Replace("## ", "").Replace("**", "") ?? "";
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await UpdateService.CheckAsync();

    private async void Install_Click(object sender, RoutedEventArgs e) => await UpdateService.DownloadAndInstallAsync();

    private void Page_Click(object sender, RoutedEventArgs e) => UpdateService.OpenReleasePage();

    private void Auto_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.AutoCheckUpdates = AutoSwitch.IsChecked == true);
}
