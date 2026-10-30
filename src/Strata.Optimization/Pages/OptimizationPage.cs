using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;
using Strata.Shell.Tweaks;
using Wpf.Ui.Controls;
using PageHeader = Strata.Shell.Hub.PageHeader;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Optimización (--hub Optimization): la entrada del módulo en el Hub. Una tarjeta por categoría con
/// sus pestañas y cuántas optimizaciones de ella están activadas; clic para abrirla.
/// </summary>
public sealed class OptimizationPage : Page
{
    private sealed record Category(Type Host, SymbolRegular Icon, string Title, string Description,
                                   IReadOnlyList<TabbedPage.Tab> Tabs, string[] TweakCategories);

    private static readonly Category[] Categories =
    [
        new(typeof(SpeedHubPage), SymbolRegular.Flash24, Loc.T("Rendimiento"),
            Loc.T("Que Windows responda más rápido y deje más recursos para lo que estás usando."), SpeedHubPage.Tabs,
            [TweakCatalog.Performance, TweakCatalog.Memory, TweakCatalog.Gaming, TweakCatalog.Hardware]),
        new(typeof(CleanupHubPage), SymbolRegular.Broom24, Loc.T("Limpieza y apps"),
            Loc.T("Archivos que sobran, apps preinstaladas y extras de los navegadores."), CleanupHubPage.Tabs,
            [TweakCatalog.Browsers]),
        new(typeof(PrivacyHubPage), SymbolRegular.ShieldLock24, Loc.T("Privacidad"),
            Loc.T("Telemetría, anuncios, funciones de IA y actualizaciones automáticas."), PrivacyHubPage.Tabs,
            [TweakCatalog.Privacy, TweakCatalog.AI, TweakCatalog.Update]),
        new(typeof(WindowsHubPage), SymbolRegular.Desktop24, Loc.T("Personalizar Windows"),
            Loc.T("Comportamiento del sistema, su aspecto, el Explorador y la multitarea."), WindowsHubPage.Tabs,
            [TweakCatalog.System, TweakCatalog.Appearance, TweakCatalog.Explorer, TweakCatalog.Multitasking]),
        new(typeof(BackupPage), SymbolRegular.History24, Loc.T("Respaldo"),
            Loc.T("Los valores de antes de cada cambio: deshaz uno o todos."), [], []),
    ];

    private readonly List<(Category Category, TextBlock Count)> _counts = new();

    public OptimizationPage()
    {
        Title = Loc.T("Optimización");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Flash24,
            Title = Loc.T("Optimización"),
            Subtitle = Loc.T("Ajustes de Windows agrupados por tema. Cada cambio se puede deshacer en Respaldo."),
        });
        foreach (var category in Categories)
            content.Children.Add(Card(category));

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
        // Se cuentan al volver a la página (pudieron cambiar en una pestaña).
        Loaded += (_, _) => UpdateCounts();
    }

    private CardAction Card(Category category)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = category.Title });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = category.Description, TextWrapping = TextWrapping.Wrap });
        if (category.Tabs.Count > 0)
        {
            var tabs = new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Text = string.Join(" · ", category.Tabs.Select(t => t.Title)),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            tabs.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            text.Children.Add(tabs);
        }
        var count = new TextBlock { Style = (Style)FindResource("CardDescription"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        _counts.Add((category, count));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);

        var card = new CardAction
        {
            Icon = new SymbolIcon { Symbol = category.Icon },
            Content = grid,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        card.Click += (_, _) => (Window.GetWindow(this) as HubWindow)?.NavigateTo(category.Host.Name);
        return card;
    }

    /// <summary>"3 de 12 activadas": los interruptores de esa categoría que están aplicados.</summary>
    private void UpdateCounts()
    {
        foreach (var (category, label) in _counts)
        {
            var toggles = TweakCatalog.All.Where(t => category.TweakCategories.Contains(t.Category) && t.IsAvailable && t.IsToggle).ToList();
            label.Text = toggles.Count == 0 ? ""
                : Loc.F("{0} de {1} activadas", toggles.Count(t => TweakEngine.CurrentState(t) == 1), toggles.Count);
        }
    }
}
