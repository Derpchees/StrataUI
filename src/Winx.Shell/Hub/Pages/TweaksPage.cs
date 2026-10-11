using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Winx.Shell.Tweaks;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Winx.Shell.Hub.Pages;

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

    protected TweaksPage(string category, string title, string description)
    {
        _category = category;
        Title = title;

        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new TextBlock { Style = (Style)FindResource("HubPageTitle"), Text = title });
        content.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            Margin = new Thickness(0, -12, 0, 16),
            Text = description + " Cada cambio se puede deshacer en Optimización › Respaldo.",
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

        foreach (var tweak in TweakCatalog.All.Where(t => t.Category == category))
            content.Children.Add(BuildCard(tweak));

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };

        TweakEngine.ExplorerRestartNeeded += ShowExplorerBanner;
        Unloaded += (_, _) => TweakEngine.ExplorerRestartNeeded -= ShowExplorerBanner;
        Loaded += (_, _) => _refreshers.ForEach(r => r());
    }

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
            admin.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = "Requiere administrador",
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
            _status.Text = $"No se pudo aplicar \"{tweak.Title}\". Revisa el registro de WINX para más detalles.";
            _status.Visibility = Visibility.Visible;
        }
    }

    private Border BuildExplorerBanner()
    {
        var button = new Wpf.Ui.Controls.Button { Content = "Reiniciar ahora", Appearance = ControlAppearance.Primary };
        button.Click += (_, _) => App.Current.RestartExplorer();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardTitle"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Text = "Algunos cambios se verán después de reiniciar el Explorador de Windows.",
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

public sealed class PrivacyPage() : TweaksPage(TweakCatalog.Privacy, "Privacidad",
    "Telemetría, anuncios, sugerencias y rastreo.");

public sealed class AIPage() : TweaksPage(TweakCatalog.AI, "Inteligencia artificial",
    "Copilot, Recall y las funciones de IA integradas en las apps de Windows.");

public sealed class SystemTweaksPage() : TweaksPage(TweakCatalog.System, "Sistema",
    "Comportamiento general de Windows.");

public sealed class UpdatePage() : TweaksPage(TweakCatalog.Update, "Windows Update",
    "Cómo y cuándo se instalan las actualizaciones.");

public sealed class WindowsAppearancePage() : TweaksPage(TweakCatalog.Appearance, "Apariencia de Windows",
    "Tema y efectos visuales del sistema (no de WINX).");

public sealed class ExplorerPage() : TweaksPage(TweakCatalog.Explorer, "Explorador de archivos",
    "Qué muestra el Explorador y cómo.");

public sealed class MultitaskingPage() : TweaksPage(TweakCatalog.Multitasking, "Multitarea",
    "Ajuste de ventanas y Alt+Tab.");

public sealed class GamingPage() : TweaksPage(TweakCatalog.Gaming, "Juegos",
    "Xbox Game Bar y grabación en segundo plano.");

public sealed class BrowsersPage() : TweaksPage(TweakCatalog.Browsers, "Navegadores",
    "Funciones extra de los navegadores instalados.");
