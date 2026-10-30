using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Strata.Shell.Hub;

/// <summary>
/// Buscador arriba de la navegación del Hub: mientras se escribe, una lista con lo que coincide
/// (<see cref="HubSearch"/>). Enter abre el primero; flechas para elegir otro; Esc borra. Ctrl+F lo enfoca.
/// </summary>
internal sealed class HubSearchBox : Grid
{
    private readonly TextBox _box = new()
    {
        PlaceholderText = Loc.T("Buscar en Strata UI"),
        Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 },
        ClearButtonEnabled = true,
    };
    private readonly ListBox _list = new() { MaxHeight = 420, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly Popup _popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private List<HubSearchEntry>? _index;

    /// <summary>Las entradas de la navegación (se leen al primer uso).</summary>
    public Func<IEnumerable<(string Title, Type Page)>>? Navigation { get; set; }

    /// <summary>Se eligió un resultado.</summary>
    public event Action<HubSearchEntry>? Chosen;

    public HubSearchBox()
    {
        Margin = new Thickness(0, 0, 0, 8);
        Children.Add(_box);

        var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(4), BorderThickness = new Thickness(1), Child = _list };
        card.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        _popup.Child = card;
        _popup.PlacementTarget = _box;
        Children.Add(_popup);

        _box.TextChanged += (_, _) => Refresh();
        _box.PreviewKeyDown += OnBoxKey;
        _list.PreviewKeyDown += OnListKey;
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemAt(e.OriginalSource as DependencyObject) is { } entry)
                Choose(entry);
        };
    }

    /// <summary>Ctrl+F: al buscador, con el texto seleccionado.</summary>
    public void FocusBox()
    {
        _box.Focus();
        _box.SelectAll();
    }

    private void Refresh()
    {
        _index ??= HubSearch.Build(Navigation?.Invoke() ?? []);
        var results = HubSearch.Find(_index, _box.Text ?? "");
        _list.Items.Clear();
        foreach (var entry in results)
            _list.Items.Add(Row(entry));
        if (results.Count == 0 && !string.IsNullOrWhiteSpace(_box.Text))
            _list.Items.Add(new ListBoxItem { Content = Loc.T("Sin resultados"), IsEnabled = false });
        _popup.Width = Math.Max(_box.ActualWidth, 280);
        _popup.IsOpen = _list.Items.Count > 0;
    }

    private static ListBoxItem Row(HubSearchEntry entry)
    {
        var title = new TextBlock { Text = entry.Title, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var where = new TextBlock { Text = entry.Where, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        where.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return new ListBoxItem { Content = new StackPanel { Children = { title, where } }, Tag = entry, Padding = new Thickness(8, 4, 8, 4) };
    }

    private void OnBoxKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _list.Items.Count > 0 && _list.Items[0] is ListBoxItem { IsEnabled: true } first:
                _popup.IsOpen = true;
                _list.SelectedIndex = 0;
                first.Focus();
                e.Handled = true;
                break;
            case Key.Enter when _list.Items.Count > 0 && _list.Items[0] is ListBoxItem { Tag: HubSearchEntry entry }:
                Choose(entry);
                e.Handled = true;
                break;
            case Key.Escape when !string.IsNullOrEmpty(_box.Text):
                _box.Text = "";
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _list.SelectedItem is ListBoxItem { Tag: HubSearchEntry entry })
        {
            Choose(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape || (e.Key == Key.Up && _list.SelectedIndex <= 0))
        {
            FocusBox();
            e.Handled = true;
        }
    }

    private static HubSearchEntry? ItemAt(DependencyObject? node)
    {
        for (; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ListBoxItem { Tag: HubSearchEntry entry })
                return entry;
        }
        return null;
    }

    private void Choose(HubSearchEntry entry)
    {
        _popup.IsOpen = false;
        _box.Text = "";
        Chosen?.Invoke(entry);
    }
}
