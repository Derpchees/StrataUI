using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Optimización sin el módulo instalado: qué trae y el botón para instalarlo. Instalado, la entrada
/// abre la página del módulo.
/// </summary>
public sealed class OptimizationSetupPage : Page
{
    public OptimizationSetupPage()
    {
        Title = Loc.T("Optimización");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Flash24,
            Title = Loc.T("Optimización"),
            Subtitle = Loc.T("Ajustes de Windows agrupados por tema. Cada cambio se puede deshacer en Respaldo."),
        });
        content.Children.Add(InstallCard());
        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
    }

    private FrameworkElement InstallCard()
    {
        var module = OptionalModules.Optimization;
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = module.Title });
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap, Text = module.Description });
        var status = new TextBlock { Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        header.Children.Add(status);
        var install = new Wpf.Ui.Controls.Button { Appearance = ControlAppearance.Primary, MinWidth = 110 };
        install.Click += async (_, _) => await module.InstallAsync();

        void Refresh()
        {
            install.IsEnabled = !module.IsDownloading;
            install.Content = module.IsDownloading ? Loc.T("Descargando…") : Loc.T("Instalar el módulo");
            status.Text = module.ErrorText ?? "";
            status.Visibility = string.IsNullOrEmpty(status.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        void OnChanged(OptionalModule changed)
        {
            if (changed == module)
                Dispatcher.BeginInvoke(Refresh);
        }
        Loaded += (_, _) => { OptionalModules.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => OptionalModules.Changed -= OnChanged;
        Refresh();

        return new CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Flash24 },
            Header = header,
            Content = install,
        };
    }
}
