using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Flyouts;
using Strata.Shell.Services.Localization;
using Media = Strata.Shell.Services.Media.MediaService;

namespace Strata.Shell.Lyrics;

/// <summary>
/// Panel con la letra de lo que suena. Si está sincronizada, la línea que se canta se resalta y
/// queda en el centro; clic en una línea salta a ese momento (si la app lo permite).
/// </summary>
public sealed class LyricsFlyout : FlyoutWindow
{
    private const double LinesHeight = 400;

    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _artist = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _status = new() { Margin = new Thickness(4, 12, 4, 12), TextWrapping = TextWrapping.Wrap };
    private readonly Button _retry = new() { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _lines = new();
    private readonly ScrollViewer _scroll;
    private readonly List<TextBlock> _lineTexts = [];
    private int _shownLine = -1;

    protected override bool ScrollContent => false;

    public LyricsFlyout()
    {
        Title = Loc.T("Letra");
        Width = 380;
        var content = new StackPanel { Margin = new Thickness(16, 16, 16, 12) };

        _title.SetResourceReference(StyleProperty, "FlyoutTitle");
        _artist.SetResourceReference(StyleProperty, "FlyoutHint");
        _status.SetResourceReference(StyleProperty, "FlyoutHint");
        _status.TextTrimming = TextTrimming.None;
        content.Children.Add(_title);
        content.Children.Add(_artist);
        content.Children.Add(_status);

        _retry.SetResourceReference(StyleProperty, "FlyoutRow");
        _retry.Content = Loc.T("Buscar de nuevo");
        _retry.Click += (_, _) => LyricsEngine.Retry();
        content.Children.Add(_retry);

        _scroll = new ScrollViewer
        {
            Content = _lines,
            Height = LinesHeight,
            Margin = new Thickness(-6, 10, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        content.Children.Add(_scroll);
        Content = content;

        LyricsEngine.Changed += Rebuild;
        LyricsEngine.LineChanged += Highlight;
        Closed += (_, _) =>
        {
            LyricsEngine.Changed -= Rebuild;
            LyricsEngine.LineChanged -= Highlight;
        };
        Rebuild();
    }

    private void Rebuild()
    {
        _title.Text = LyricsEngine.Title.Length > 0 ? LyricsEngine.Title : Loc.T("Letra");
        _artist.Text = LyricsEngine.Artist;
        _artist.Visibility = LyricsEngine.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lines.Children.Clear();
        _lineTexts.Clear();
        _shownLine = -1;

        var lyrics = LyricsEngine.Current;
        string status = LyricsEngine.State switch
        {
            LyricsState.None => Loc.T("No suena nada."),
            LyricsState.Searching => Loc.T("Buscando la letra…"),
            LyricsState.NotFound => Loc.T("No se encontró la letra de esta canción."),
            LyricsState.Failed => Loc.T("No se pudo buscar la letra. Revisa la conexión a internet."),
            _ when lyrics is { Instrumental: true, Synced.Count: 0, Plain.Length: 0 } => Loc.T("Es instrumental: no tiene letra."),
            _ when lyrics is { Synced.Count: 0 } => Loc.T("Esta letra no está sincronizada."),
            _ => "",
        };
        _status.Text = status;
        _status.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = LyricsEngine.State is LyricsState.NotFound or LyricsState.Failed ? Visibility.Visible : Visibility.Collapsed;

        if (lyrics is { Synced.Count: > 0 })
        {
            for (int i = 0; i < lyrics.Synced.Count; i++)
            {
                var line = lyrics.Synced[i];
                _lines.Children.Add(LineButton(line.Text.Length > 0 ? line.Text : "♪", line.Time));
            }
            // Espacio al final para que la última línea también pueda quedar en el centro.
            _lines.Children.Add(new Border { Height = LinesHeight / 2 });
        }
        else if (lyrics is { Plain.Length: > 0 })
        {
            var text = new TextBlock { Text = lyrics.Plain, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8), LineHeight = 22 };
            text.SetResourceReference(StyleProperty, "FlyoutText");
            text.TextTrimming = TextTrimming.None;
            _lines.Children.Add(text);
        }
        _scroll.Visibility = _lines.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.ScrollToTop();
        Highlight();
    }

    private Button LineButton(string text, TimeSpan time)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        block.SetResourceReference(StyleProperty, "FlyoutText");
        block.TextTrimming = TextTrimming.None;
        block.Opacity = 0.55;
        _lineTexts.Add(block);
        var button = new Button
        {
            Content = block,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 5, 6, 5),
            ToolTip = Media.CanSeek ? Loc.T("Ir a esta parte") : null,
        };
        button.SetResourceReference(StyleProperty, "FlyoutRow");
        button.Click += (_, _) =>
        {
            if (Media.CanSeek)
                Media.Seek(time);
        };
        return button;
    }

    private void Highlight()
    {
        int index = LyricsEngine.LineIndex;
        if (index == _shownLine || _lineTexts.Count == 0)
            return;
        if (_shownLine >= 0 && _shownLine < _lineTexts.Count)
        {
            _lineTexts[_shownLine].Opacity = 0.55;
            _lineTexts[_shownLine].FontWeight = FontWeights.Normal;
        }
        _shownLine = index;
        if (index < 0 || index >= _lineTexts.Count)
            return;
        var current = _lineTexts[index];
        current.Opacity = 1;
        current.FontWeight = FontWeights.SemiBold;
        // La línea actual en el centro de la lista (si todavía no se dibujó, cuando se dibuje).
        if (_lines.Children[index] is FrameworkElement { IsLoaded: true } row)
            CenterOn(row);
        else
            Dispatcher.BeginInvoke(() =>
            {
                if (_shownLine == index && index < _lines.Children.Count && _lines.Children[index] is FrameworkElement { IsLoaded: true } later)
                    CenterOn(later);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CenterOn(FrameworkElement row)
    {
        var top = row.TransformToAncestor(_lines).Transform(new Point(0, 0)).Y;
        _scroll.ScrollToVerticalOffset(Math.Max(0, top - (LinesHeight - row.ActualHeight) / 2));
    }
}
