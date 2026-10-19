using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Tweaks;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

public partial class AppsPage : Page
{
    public sealed class AppRow(AppPackage package) : INotifyPropertyChanged
    {
        private bool _isSelected;

        public AppPackage Package { get; } = package;
        public Visibility RecommendedVisibility => Package.Recommended ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private List<AppRow> _rows = new();

    public AppsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ShowEdge();
            ShowGameBar();
            await LoadAsync();
        };
    }

    /// <summary>Instalada: se ofrece quitarla; ya quitada pero con restos: limpiarlos; si no, no se muestra.</summary>
    private void ShowGameBar()
    {
        bool installed = GameBarRemover.IsInstalled;
        bool leftovers = !installed && GameBarRemover.NeedsCleanup;
        GameBarCard.Visibility = installed || leftovers ? Visibility.Visible : Visibility.Collapsed;
        GameBarText.Text = installed
            ? Loc.T("Quita la Game Bar de Xbox (Win+G) y la grabación de juegos en segundo plano, sin que Windows pida otra app al abrir un juego. Se puede volver a instalar desde Microsoft Store.")
            : Loc.T("La Game Bar ya no está, pero Windows puede pedir una app para abrir sus vínculos al iniciar un juego y seguir grabando en segundo plano.");
        GameBarButton.Content = installed ? Loc.T("Desinstalar Game Bar") : Loc.T("Quitar sus restos");
        GameBarButton.IsEnabled = true;
    }

    private async void RemoveGameBar_Click(object sender, RoutedEventArgs e)
    {
        if (!GameBarRemover.IsInstalled)
        {
            GameBarRemover.CleanUp();
            GameBarText.Text = Loc.T("Listo: la Game Bar ya no deja restos.");
            GameBarButton.IsEnabled = false;
            return;
        }
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Desinstalar Game Bar"),
            Content = Loc.T("Se quitará la Game Bar de Xbox de tu usuario y se apagará la grabación de juegos en segundo plano. Para volver a tenerla, se instala desde Microsoft Store."),
            PrimaryButtonText = Loc.T("Desinstalar"),
            CloseButtonText = Loc.T("Cancelar"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;
        GameBarButton.IsEnabled = false;
        GameBarText.Text = Loc.T("Desinstalando Game Bar…");
        bool removed = await GameBarRemover.RemoveAsync();
        if (removed)
            TweakEngine.Backup.AddRemovedApps(["Xbox Game Bar"]);
        GameBarText.Text = removed ? Loc.T("Listo: la Game Bar ya no está instalada.")
            : Loc.T("Windows no dejó quitar la Game Bar. Ciérrala (y los juegos abiertos) e inténtalo de nuevo.");
        GameBarButton.IsEnabled = !removed;
        await LoadAsync(); // ya no está en la lista
    }

    /// <summary>Instalado: se ofrece quitarlo; desactivado (Windows no dejó quitarlo): restaurarlo.</summary>
    private void ShowEdge()
    {
        bool disabled = EdgeRemover.IsDisabled;
        EdgeCard.Visibility = EdgeRemover.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        EdgeText.Text = disabled
            ? Loc.T("Edge está desactivado: Windows no deja desinstalarlo en tu región. No tiene accesos directos ni corre en segundo plano, y sigue ahí para lo que lo necesite.")
            : Loc.T("Quita el navegador Edge (WebView2, que usan otras apps, se queda) y evita que Windows Update lo reinstale. Se puede volver a instalar desde microsoft.com/edge.");
        EdgeButton.Content = disabled ? Loc.T("Restaurar Edge") : Loc.T("Desinstalar Edge");
        EdgeButton.Appearance = disabled ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Danger;
        EdgeButton.IsEnabled = true;
    }

    private async void RemoveEdge_Click(object sender, RoutedEventArgs e)
    {
        if (EdgeRemover.IsDisabled)
        {
            EdgeButton.IsEnabled = false;
            int restored = await Elevation.RunSelfAsync(EdgeRemover.RestoreArg);
            ShowEdge();
            if (restored == Elevation.Cancelled)
                EdgeText.Text = Loc.T("Cancelado.");
            else if (!EdgeRemover.IsDisabled)
                EdgeText.Text = Loc.T("Listo: Edge volvió a como estaba.");
            return;
        }
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Desinstalar Edge"),
            Content = Loc.T("Se quitará Microsoft Edge de este equipo para todos los usuarios. Si Windows no lo permite en tu región, se desactiva: sin accesos directos ni procesos en segundo plano. Los enlaces se abrirán con tu navegador predeterminado."),
            PrimaryButtonText = Loc.T("Desinstalar"),
            CloseButtonText = Loc.T("Cancelar"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;
        EdgeButton.IsEnabled = false;
        EdgeText.Text = Loc.T("Desinstalando Edge…");
        int code = await Elevation.RunSelfAsync(EdgeRemover.Arg);
        bool removed = !EdgeRemover.IsInstalled;
        if (removed)
        {
            TweakEngine.Backup.AddRemovedApps(["Microsoft Edge"]);
            EdgeText.Text = Loc.T("Listo: Edge ya no está instalado.");
            return;
        }
        ShowEdge();
        if (code == Elevation.Cancelled)
            EdgeText.Text = Loc.T("Cancelado.");
        else if (!EdgeRemover.IsDisabled)
            EdgeText.Text = Loc.T("Windows no dejó quitar Edge. Puede que una actualización lo esté usando: reinicia e inténtalo de nuevo.");
    }

    private async Task LoadAsync()
    {
        Loading.Visibility = Visibility.Visible;
        AppList.ItemsSource = null;
        try
        {
            var packages = await AppxManager.ListAsync();
            _rows = packages.Select(p => new AppRow(p)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo listar las apps", ex);
            StatusText.Text = Loc.T("No se pudo obtener la lista de apps.");
            _rows = new();
        }
        Loading.Visibility = Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        bool showAll = ShowAllBox.IsChecked == true;
        AppList.ItemsSource = _rows.Where(r => showAll || r.Package.Recommended || r.IsSelected).ToList();
        if (!showAll && !_rows.Any(r => r.Package.Recommended))
            StatusText.Text = Loc.T("No quedan apps recomendadas para quitar. Marca \"Mostrar todas las apps\" para ver el resto.");
        UpdateRemoveButton();
    }

    private void UpdateRemoveButton()
    {
        int count = _rows.Count(r => r.IsSelected);
        RemoveButton.IsEnabled = count > 0;
        RemoveButton.Content = count > 0 ? Loc.F("Quitar seleccionadas ({0})", count) : Loc.T("Quitar seleccionadas");
    }

    private void Selection_Changed(object sender, RoutedEventArgs e) => UpdateRemoveButton();

    private void ShowAll_Click(object sender, RoutedEventArgs e) => ApplyFilter();

    private void SelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(r => r.Package.Recommended))
            row.IsSelected = true;
        UpdateRemoveButton();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _rows.ForEach(r => r.IsSelected = false);
        ApplyFilter();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Package).ToList();
        bool allUsers = AllUsersBox.IsChecked == true;

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Quitar apps"),
            Content = (allUsers ? Loc.F("Se quitarán {0} apps para todos los usuarios:", selected.Count)
                                : Loc.F("Se quitarán {0} apps:", selected.Count)) + "\n\n" +
                      string.Join("\n", selected.Select(p => "• " + p.Label).Take(15)) +
                      (selected.Count > 15 ? "\n" + Loc.F("… y {0} más", selected.Count - 15) : "") +
                      "\n\n" + Loc.T("Se pueden reinstalar desde Microsoft Store."),
            PrimaryButtonText = Loc.T("Quitar"),
            CloseButtonText = Loc.T("Cancelar"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        RemoveButton.IsEnabled = false;
        Loading.Visibility = Visibility.Visible;
        StatusText.Text = Loc.F("Quitando {0} apps…", selected.Count);
        try
        {
            await AppxManager.RemoveAsync(selected.Select(p => p.Name), allUsers);
            TweakEngine.Backup.AddRemovedApps(selected.Select(p => $"{p.Label} ({p.Name})"));
            Log.Info($"Apps quitadas: {string.Join(", ", selected.Select(p => p.Name))}");
            await LoadAsync();
            var remaining = selected.Where(p => _rows.Any(r => r.Package.Name == p.Name)).ToList();
            StatusText.Text = remaining.Count == 0
                ? Loc.F("Listo: se quitaron {0} apps.", selected.Count)
                : Loc.F("Se quitaron {0} de {1}. No se pudieron quitar: {2}.", selected.Count - remaining.Count, selected.Count,
                       string.Join(", ", remaining.Select(p => p.Label)));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.T("Cancelado.");
            Loading.Visibility = Visibility.Collapsed;
            UpdateRemoveButton();
        }
        catch (Exception ex)
        {
            Log.Error("Error al quitar apps", ex);
            StatusText.Text = Loc.T("Ocurrió un error al quitar las apps.");
            Loading.Visibility = Visibility.Collapsed;
            UpdateRemoveButton();
        }
    }
}
