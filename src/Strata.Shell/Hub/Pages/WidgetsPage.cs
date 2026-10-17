using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Strata.Shell.Services.Widgets;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Hub › Widgets: se agregan, mueven, cambian de tamaño y quitan en el modo edición (sobre el
/// escritorio mismo); aquí, el acceso y cuántos hay. Los hechos por otros se importan en Hub ›
/// Importar (<see cref="WidgetModule"/>) y aparecen en la biblioteca del modo edición.
/// </summary>
public sealed class WidgetsPage : Page
{
    private readonly TextBlock _count = new();

    public WidgetsPage()
    {
        Title = Loc.T("Widgets");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Apps24,
            Title = Loc.T("Widgets"),
            Subtitle = Loc.T("Relojes, un visualizador de música y medidores de CPU, GPU y FPS sobre el fondo de pantalla."),
        });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("Editar en el escritorio") });
        header.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.T("En el modo edición se arrastran desde la biblioteca, se mueven a cualquier monitor y con clic derecho se cambia su tamaño, sus opciones o se quitan."),
        });
        _count.Style = (Style)FindResource("CardDescription");
        header.Children.Add(_count);
        var edit = new Wpf.Ui.Controls.Button
        {
            Appearance = ControlAppearance.Primary,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Edit24 },
            Content = Loc.T("Editar diseño"),
        };
        edit.Click += (_, _) => EditMode.EditSession.Start();
        content.Children.Add(new CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.DesignIdeas24 },
            Header = header,
            Content = edit,
        });

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
        Loaded += (_, _) =>
        {
            WidgetManager.Changed += UpdateCount;
            UpdateCount();
        };
        Unloaded += (_, _) => WidgetManager.Changed -= UpdateCount;
    }

    private void UpdateCount()
    {
        int count = Settings.Current.Widgets.Count;
        _count.Text = count == 0 ? Loc.T("Todavía no hay widgets.") : Loc.F("Widgets en el escritorio: {0}", count);
        _count.Margin = new Thickness(0, 4, 0, 0);
    }
}
