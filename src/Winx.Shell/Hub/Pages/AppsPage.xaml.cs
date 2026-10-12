using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;
using Winx.Shell.Tweaks;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Hub.Pages;

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
        Loaded += async (_, _) => await LoadAsync();
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
