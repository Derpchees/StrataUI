using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Cleanup;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using PageHeader = Strata.Shell.Hub.PageHeader;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Limpieza con un clic (<see cref="JunkCleaner"/>): cada categoría con lo que ocupa y un
/// interruptor (lo elegido se recuerda); "Limpiar ahora" borra lo marcado. La papelera no se toca.
/// </summary>
public sealed class CleanupPage : Page
{
    private readonly Dictionary<string, (ToggleSwitch Toggle, TextBlock Size)> _rows = new();
    private readonly Dictionary<string, CleanupSize> _sizes = new();
    private readonly TextBlock _total;
    private readonly TextBlock _result;
    private readonly Wpf.Ui.Controls.Button _clean;
    private readonly ProgressRing _busy;

    public CleanupPage()
    {
        Title = Loc.T("Limpieza");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Broom24,
            Title = Loc.T("Limpieza"),
            Subtitle = Loc.T("Libera espacio borrando lo que Windows y las apps ya no necesitan. La papelera de reciclaje no se toca."),
        });

        // Resumen: lo que se liberaría y el botón.
        _total = new TextBlock { Style = (Style)FindResource("CardTitle"), FontSize = 20, FontWeight = FontWeights.SemiBold };
        _result = new TextBlock { Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap };
        _clean = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("Limpiar ahora"),
            Appearance = ControlAppearance.Primary,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Broom24 },
            Padding = new Thickness(18, 8, 18, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _clean.Click += async (_, _) => await CleanAsync();
        _busy = new ProgressRing { IsIndeterminate = true, Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0), Visibility = Visibility.Collapsed };

        var summaryText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        summaryText.Children.Add(_total);
        summaryText.Children.Add(_result);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_busy);
        actions.Children.Add(_clean);
        var summary = new Grid();
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summary.Children.Add(summaryText);
        Grid.SetColumn(actions, 1);
        summary.Children.Add(actions);
        content.Children.Add(new Border
        {
            Child = summary,
            Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(0, 0, 0, 16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
        });

        content.Children.Add(new TextBlock { Style = (Style)FindResource("HubSectionTitle"), Text = Loc.T("Qué se limpia") });
        var selected = Selection();
        foreach (var category in JunkCleaner.Categories)
            content.Children.Add(Row(category, selected.Contains(category.Id)));

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
        Loaded += async (_, _) => await MeasureAsync();
    }

    /// <summary>Lo elegido (o, la primera vez, las categorías sin costo).</summary>
    private static HashSet<string> Selection() =>
        (Settings.Current.CleanupSelection ?? JunkCleaner.Categories.Where(c => c.DefaultOn).Select(c => c.Id).ToList()).ToHashSet();

    private CardControl Row(CleanupCategory category, bool on)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = category.Name });
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = category.Description, TextWrapping = TextWrapping.Wrap });
        if (category.NeedsAdmin)
        {
            var admin = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var tertiary = (Brush)FindResource("TextFillColorTertiaryBrush");
            admin.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Shield16, FontSize = 12, Margin = new Thickness(0, 0, 4, 0), Foreground = tertiary });
            admin.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = Loc.T("Requiere administrador"), Foreground = tertiary });
            header.Children.Add(admin);
        }
        var size = new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            Text = Loc.T("Midiendo…"),
            MinWidth = 80,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        var toggle = new ToggleSwitch { IsChecked = on };
        toggle.Click += (_, _) =>
        {
            Settings.Update(s => s.CleanupSelection = _rows.Where(r => r.Value.Toggle.IsChecked == true).Select(r => r.Key).ToList());
            UpdateTotal();
        };
        _rows[category.Id] = (toggle, size);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(size);
        right.Children.Add(toggle);
        return new CardControl { Style = (Style)FindResource("HubCard"), Header = header, Content = right };
    }

    private List<CleanupCategory> Checked() =>
        JunkCleaner.Categories.Where(c => _rows[c.Id].Toggle.IsChecked == true).ToList();

    private async Task MeasureAsync()
    {
        _total.Text = Loc.T("Midiendo…");
        foreach (var category in JunkCleaner.Categories)
        {
            var size = await Task.Run(() => JunkCleaner.Measure(category));
            _sizes[category.Id] = size;
            _rows[category.Id].Size.Text = Format(size.Bytes);
        }
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        if (_sizes.Count < JunkCleaner.Categories.Count)
            return;
        long bytes = Checked().Sum(c => _sizes[c.Id].Bytes);
        _total.Text = Loc.F("{0} para liberar", Format(bytes));
        _clean.IsEnabled = Checked().Count > 0;
    }

    private async Task CleanAsync()
    {
        var categories = Checked();
        if (categories.Count == 0)
            return;
        _clean.IsEnabled = false;
        _busy.Visibility = Visibility.Visible;
        _result.Text = Loc.T("Limpiando…");
        var (freed, cancelled) = await JunkCleaner.CleanAsync(categories);
        _busy.Visibility = Visibility.Collapsed;
        _result.Text = Loc.F("Se liberaron {0} ({1} archivos). Lo que estaba en uso se quedó.", Format(freed.Bytes), freed.Files)
                       + (cancelled ? " " + Loc.T("Lo de Windows no se limpió porque no se dio el permiso.") : "");
        await MeasureAsync();
        _clean.IsEnabled = true;
    }

    internal static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => Loc.F("{0:0.0} GB", bytes / (double)(1L << 30)),
        >= 1L << 20 => Loc.F("{0:0} MB", bytes / (double)(1L << 20)),
        > 0 => Loc.F("{0:0} KB", Math.Max(1, bytes / 1024.0)),
        _ => Loc.T("Nada"),
    };
}
