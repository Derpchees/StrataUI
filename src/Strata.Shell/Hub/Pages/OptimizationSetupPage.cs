using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// El grupo Optimización cuando su módulo no está instalado: qué trae y el botón para descargarlo
/// (<see cref="OptimizationModule"/>). Al terminar, el Hub cambia esta entrada por sus páginas.
/// </summary>
public sealed class OptimizationSetupPage : Page
{
    private readonly Wpf.Ui.Controls.Button _install;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;

    public OptimizationSetupPage()
    {
        Title = Loc.T("Optimización");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Flash24,
            Title = Loc.T("Optimización"),
            Subtitle = Loc.T("Rendimiento, privacidad, IA, limpieza, apps preinstaladas y más ajustes de Windows. Es un módulo aparte: no viene con Strata UI y se instala solo si lo quieres."),
        });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("Módulo de optimización") });
        header.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.F("Se descarga de la publicación de Strata UI {0} en GitHub y se instala para tu usuario, sin permiso de administrador.", App.Version),
        });
        _install = new Wpf.Ui.Controls.Button { Content = Loc.T("Instalar"), Appearance = ControlAppearance.Primary, MinWidth = 120 };
        _install.Click += async (_, _) => await OptimizationModule.InstallAsync();
        content.Children.Add(new CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
            Header = header,
            Content = _install,
        });

        _progress = new ProgressBar { Maximum = 1, Height = 4, Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed };
        content.Children.Add(_progress);
        _status = new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SystemFillColorCriticalBrush"),
        };
        content.Children.Add(_status);

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Content = content,
        };

        Loaded += (_, _) =>
        {
            OptimizationModule.Changed -= Refresh;
            OptimizationModule.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => OptimizationModule.Changed -= Refresh;
    }

    private void Refresh()
    {
        bool busy = OptimizationModule.IsDownloading;
        _install.IsEnabled = !busy;
        _install.Content = busy ? Loc.T("Descargando…") : OptimizationModule.ErrorText is null ? Loc.T("Instalar") : Loc.T("Reintentar");
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _progress.Value = OptimizationModule.Progress;
        _status.Text = OptimizationModule.ErrorText ?? "";
    }
}
