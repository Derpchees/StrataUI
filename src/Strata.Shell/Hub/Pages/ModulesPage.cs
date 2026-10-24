using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Hub › Módulos opcionales: las partes de Strata UI que no vienen en el instalador
/// (<see cref="OptionalModules"/>), cada una con su botón para instalarla o quitarla.
/// </summary>
public sealed class ModulesPage : Page
{
    private sealed class Row
    {
        public required OptionalModule Module { get; init; }
        public required TextBlock Status { get; init; }
        public required Wpf.Ui.Controls.Button Install { get; init; }
        public required Wpf.Ui.Controls.Button Remove { get; init; }
        public required ProgressBar Progress { get; init; }
        public required TextBlock Error { get; init; }
    }

    private readonly List<Row> _rows = [];

    public ModulesPage()
    {
        Title = Loc.T("Módulos opcionales");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.PuzzlePiece24,
            Title = Loc.T("Módulos opcionales"),
            Subtitle = Loc.F("Partes de Strata UI que no vienen en el instalador: se descargan de la publicación {0} en GitHub y se instalan solo si las quieres.", App.Version),
        });
        foreach (var module in OptionalModules.All)
            content.Children.Add(Card(module));

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
        Loaded += (_, _) =>
        {
            OptionalModules.Changed -= OnChanged;
            OptionalModules.Changed += OnChanged;
            foreach (var row in _rows)
                Refresh(row);
        };
        Unloaded += (_, _) => OptionalModules.Changed -= OnChanged;
    }

    private FrameworkElement Card(OptionalModule module)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = module.Title });
        header.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            TextWrapping = TextWrapping.Wrap,
            Text = module.Description,
        });
        var status = new TextBlock { Style = (Style)FindResource("CardDescription"), Margin = new Thickness(0, 4, 0, 0) };
        header.Children.Add(status);

        var install = new Wpf.Ui.Controls.Button { Appearance = ControlAppearance.Primary, MinWidth = 110 };
        install.Click += async (_, _) => await module.InstallAsync();
        var remove = new Wpf.Ui.Controls.Button { Content = Loc.T("Quitar"), MinWidth = 90 };
        remove.Click += (_, _) => module.Remove();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(install);
        buttons.Children.Add(remove);

        var progress = new ProgressBar { Maximum = 1, Height = 4, Margin = new Thickness(0, 8, 0, 0) };
        var error = new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SystemFillColorCriticalBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        header.Children.Add(progress);
        header.Children.Add(error);

        _rows.Add(new Row { Module = module, Status = status, Install = install, Remove = remove, Progress = progress, Error = error });
        return new CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new SymbolIcon { Symbol = module.Icon },
            Header = header,
            Content = buttons,
        };
    }

    private void OnChanged(OptionalModule module) => Dispatcher.BeginInvoke(() =>
    {
        foreach (var row in _rows.Where(r => r.Module == module))
            Refresh(row);
    });

    private static void Refresh(Row row)
    {
        var module = row.Module;
        bool busy = module.IsDownloading;
        bool installed = module.IsInstalled;
        row.Status.Text = busy ? Loc.T("Descargando…")
            : module.RemovePending ? Loc.T("Se quitará al reiniciar Strata UI.")
            : installed ? Loc.T("Instalado.")
            : Loc.T("No instalado.");
        row.Install.Visibility = installed && !busy ? Visibility.Collapsed : Visibility.Visible;
        row.Install.IsEnabled = !busy;
        row.Install.Content = busy ? Loc.T("Descargando…") : module.ErrorText is null ? Loc.T("Instalar") : Loc.T("Reintentar");
        row.Remove.Visibility = module.CanRemove && !busy ? Visibility.Visible : Visibility.Collapsed;
        row.Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        row.Progress.Value = module.Progress;
        row.Error.Text = module.ErrorText ?? "";
        row.Error.Visibility = module.ErrorText is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
