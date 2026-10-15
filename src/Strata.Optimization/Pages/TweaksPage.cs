using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Strata.Shell.Tweaks;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using Strata.Shell.Services.Localization;
using PageHeader = Strata.Shell.Hub.PageHeader;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Página genérica que muestra los ajustes de una categoría del catálogo como tarjetas
/// con interruptor o lista de opciones. Cada categoría es una subclase de una línea.
/// </summary>
public abstract class TweaksPage : Page
{
    private readonly string _category;
    private readonly Border _explorerBanner;
    private readonly TextBlock _status;
    private readonly List<Action> _refreshers = new();

    protected TweaksPage(string category, string title, string description, SymbolRegular icon)
    {
        _category = category;
        Title = title;

        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = icon,
            Title = title,
            Subtitle = description + " " + Loc.T("Cada cambio se puede deshacer en Optimización › Respaldo."),
        });

        _explorerBanner = BuildExplorerBanner();
        content.Children.Add(_explorerBanner);
        _status = new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            Foreground = (Brush)FindResource("SystemFillColorCriticalBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(_status);

        foreach (var element in ExtraContent())
            content.Children.Add(element);
        foreach (var tweak in TweakCatalog.All.Where(t => t.Category == category && t.IsAvailable))
            content.Children.Add(BuildCard(tweak));

        // CanContentScroll=false en la página evita que WPF-UI la envuelva en otro ScrollViewer
        // (con altura infinita la página nunca desplaza y su ScrollViewer se queda con la rueda).
        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Content = content,
        };

        // Suscripción en Loaded (no en el constructor): la página se descarga al navegar a otra
        // sección y se vuelve a cargar al regresar.
        Loaded += (_, _) =>
        {
            TweakEngine.ExplorerRestartNeeded -= ShowExplorerBanner;
            TweakEngine.ExplorerRestartNeeded += ShowExplorerBanner;
            _refreshers.ForEach(r => r());
        };
        Unloaded += (_, _) => TweakEngine.ExplorerRestartNeeded -= ShowExplorerBanner;
    }

    /// <summary>Lo que una página agrega antes de los ajustes del catálogo.</summary>
    protected virtual IEnumerable<UIElement> ExtraContent() => [];

    private CardControl BuildCard(Tweak tweak)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = tweak.Title });
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = tweak.Description });
        if (tweak.RequiresAdmin)
        {
            var admin = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            admin.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Shield16, FontSize = 12, Margin = new Thickness(0, 0, 4, 0),
                                                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush") });
            admin.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = Loc.T("Requiere administrador"),
                                               Foreground = (Brush)FindResource("TextFillColorTertiaryBrush") });
            header.Children.Add(admin);
        }

        var card = new CardControl { Style = (Style)FindResource("HubCard"), Header = header };

        if (tweak.IsToggle)
        {
            var toggle = new ToggleSwitch();
            void Refresh() => toggle.IsChecked = TweakEngine.CurrentState(tweak) == 1;
            toggle.Click += async (_, _) =>
            {
                toggle.IsEnabled = false;
                await ApplyAsync(tweak, toggle.IsChecked == true ? 1 : 0);
                Refresh();
                toggle.IsEnabled = true;
            };
            _refreshers.Add(Refresh);
            card.Content = toggle;
        }
        else
        {
            var combo = new ComboBox { MinWidth = 180, ItemsSource = tweak.Choices };
            bool syncing = false;
            void Refresh()
            {
                syncing = true;
                combo.SelectedIndex = TweakEngine.CurrentState(tweak) ?? -1;
                syncing = false;
            }
            combo.SelectionChanged += async (_, _) =>
            {
                if (syncing || combo.SelectedIndex < 0)
                    return;
                combo.IsEnabled = false;
                await ApplyAsync(tweak, combo.SelectedIndex);
                Refresh();
                combo.IsEnabled = true;
            };
            _refreshers.Add(Refresh);
            card.Content = combo;
        }
        return card;
    }

    private async Task ApplyAsync(Tweak tweak, int state)
    {
        _status.Visibility = Visibility.Collapsed;
        var result = await TweakEngine.ApplyAsync(tweak, state);
        if (result == ApplyResult.Failed)
        {
            _status.Text = Loc.F("No se pudo aplicar \"{0}\". Revisa el registro de Strata UI para más detalles.", tweak.Title);
            _status.Visibility = Visibility.Visible;
        }
    }

    private Border BuildExplorerBanner()
    {
        var button = new Wpf.Ui.Controls.Button { Content = Loc.T("Reiniciar ahora"), Appearance = ControlAppearance.Primary };
        button.Click += (_, _) => App.Current.RestartExplorer();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardTitle"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.T("Algunos cambios se verán después de reiniciar el Explorador de Windows."),
        });
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("SystemFillColorAttentionBackgroundBrush"),
            Visibility = Visibility.Collapsed,
        };
    }

    private void ShowExplorerBanner() => _explorerBanner.Visibility = Visibility.Visible;
}

public sealed class PrivacyPage() : TweaksPage(TweakCatalog.Privacy, Loc.T("Privacidad"),
    Loc.T("Telemetría, anuncios, sugerencias y rastreo."), SymbolRegular.ShieldLock24);

public sealed class AIPage() : TweaksPage(TweakCatalog.AI, Loc.T("Inteligencia artificial"),
    Loc.T("Copilot, Recall y las funciones de IA integradas en las apps de Windows."), SymbolRegular.BrainCircuit24);

public sealed class SystemTweaksPage() : TweaksPage(TweakCatalog.System, Loc.T("Sistema"),
    Loc.T("Comportamiento general de Windows."), SymbolRegular.Desktop24);

public sealed class UpdatePage() : TweaksPage(TweakCatalog.Update, "Windows Update",
    Loc.T("Cómo y cuándo se instalan las actualizaciones."), SymbolRegular.ArrowSync24);

public sealed class WindowsAppearancePage() : TweaksPage(TweakCatalog.Appearance, Loc.T("Apariencia de Windows"),
    Loc.T("Tema y efectos visuales del sistema (no de Strata UI)."), SymbolRegular.DarkTheme24);

public sealed class ExplorerPage() : TweaksPage(TweakCatalog.Explorer, Loc.T("Explorador de archivos"),
    Loc.T("Qué muestra el Explorador y cómo."), SymbolRegular.Folder24);

public sealed class MultitaskingPage() : TweaksPage(TweakCatalog.Multitasking, Loc.T("Multitarea"),
    Loc.T("Ajuste de ventanas y Alt+Tab."), SymbolRegular.SquareMultiple24);

public sealed class GamingPage() : TweaksPage(TweakCatalog.Gaming, Loc.T("Juegos"),
    Loc.T("Xbox Game Bar y grabación en segundo plano."), SymbolRegular.Games24);

public sealed class BrowsersPage() : TweaksPage(TweakCatalog.Browsers, Loc.T("Navegadores"),
    Loc.T("Funciones extra de los navegadores instalados."), SymbolRegular.Globe24);

public sealed class PerformancePage() : TweaksPage(TweakCatalog.Performance, Loc.T("Rendimiento"),
    Loc.T("Respuesta más rápida de menús y ventanas, y más prioridad para lo que estás usando."), SymbolRegular.Flash24);
