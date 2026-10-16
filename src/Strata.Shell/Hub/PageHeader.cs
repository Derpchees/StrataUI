using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub;

/// <summary>
/// Cabecera de las páginas del Hub: ícono sobre el color de acento, título y una línea que dice
/// para qué sirve la página.
/// </summary>
public sealed class PageHeader : Grid
{
    private readonly SymbolIcon _icon = new() { FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();

    public PageHeader()
    {
        Margin = new Thickness(0, 4, 0, 24);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());

        var tile = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _icon,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "StrataAccentBrush");
        _icon.SetResourceReference(IconElement.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title.FontSize = 28;
        _title.FontWeight = FontWeights.SemiBold;
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _subtitle.FontSize = 13;
        _subtitle.TextWrapping = TextWrapping.Wrap;
        _subtitle.Margin = new Thickness(0, 2, 0, 0);
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        text.Children.Add(_title);
        text.Children.Add(_subtitle);
        SetColumn(text, 1);
        Children.Add(text);
    }

    public SymbolRegular Icon
    {
        get => _icon.Symbol;
        set => _icon.Symbol = value;
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public string Subtitle
    {
        get => _subtitle.Text;
        set
        {
            _subtitle.Text = value;
            _subtitle.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
