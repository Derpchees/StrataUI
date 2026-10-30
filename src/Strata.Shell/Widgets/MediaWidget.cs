using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Media;

namespace Strata.Shell.Widgets;

/// <summary>
/// Lo que suena, para el escritorio: carátula, título y artista, y anterior / reproducir / siguiente.
/// Es la misma sesión que el módulo de la barra (<see cref="MediaService"/>: cualquier app que se
/// anuncie a Windows). Clic en la carátula abre la app que suena.
/// </summary>
public sealed class MediaWidget : Grid, IWidgetContent
{
    private readonly Border _cover = new() { ClipToBounds = true };
    private readonly Image _coverImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _coverGlyph = Glyph(0xE8D6); // nota musical, sin carátula
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _previous, _playPause, _next;
    private readonly TextBlock _playGlyph = Glyph(0xE768);
    private double _scale = 1;

    public MediaWidget(WidgetConfig config)
    {
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _title.FontFamily = _artist.FontFamily = font;
        _title.Foreground = _artist.Foreground = Brushes.White;
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());

        _cover.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        _cover.Child = new Grid { Children = { _coverGlyph, _coverImage } };
        _cover.Cursor = Cursors.Hand;
        _cover.ToolTip = Loc.T("Abrir la app");
        Handle(_cover, MediaService.ShowSourceApp);
        Children.Add(_cover);

        _previous = Button(Glyph(0xE892), Loc.T("Anterior"), MediaService.Previous);
        _playPause = Button(_playGlyph, Loc.T("Reproducir o pausar"), MediaService.PlayPause);
        _next = Button(Glyph(0xE893), Loc.T("Siguiente"), MediaService.Next);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Children = { _previous, _playPause, _next } };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _artist, controls } };
        SetColumn(text, 1);
        Children.Add(text);

        Apply(config);
        Loaded += (_, _) =>
        {
            MediaService.Changed += Update;
            Update();
        };
        Unloaded += (_, _) => MediaService.Changed -= Update;
    }

    public void Apply(WidgetConfig config)
    {
        _scale = config.Scale;
        double cover = 96 * _scale;
        _cover.Width = _cover.Height = cover;
        _cover.CornerRadius = new CornerRadius(10 * _scale);
        _cover.Margin = new Thickness(0, 0, 14 * _scale, 0);
        _coverGlyph.FontSize = 36 * _scale;
        Width = 360 * _scale;
        _title.FontSize = 18 * _scale;
        _artist.FontSize = 13 * _scale;
        _artist.Margin = new Thickness(0, 0, 0, 6 * _scale);
        foreach (var button in new[] { _previous, _playPause, _next })
        {
            button.Width = button.Height = 36 * _scale;
            button.CornerRadius = new CornerRadius(18 * _scale);
            ((TextBlock)button.Child).FontSize = 16 * _scale;
        }
    }

    private void Update()
    {
        bool playing = MediaService.HasSession;
        _title.Text = playing && MediaService.Title.Length > 0 ? MediaService.Title : Loc.T("Nada sonando");
        _artist.Text = playing ? MediaService.Artist.Length > 0 ? MediaService.Artist : MediaService.AppName : "";
        _coverImage.Source = playing ? MediaService.Cover : null;
        _coverGlyph.Visibility = _coverImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        _playGlyph.Text = ((char)(MediaService.IsPlaying ? 0xE769 : 0xE768)).ToString(); // pausa : reproducir
        _previous.IsEnabled = playing && MediaService.CanPrevious;
        _next.IsEnabled = playing && MediaService.CanNext;
        _playPause.IsEnabled = playing;
        foreach (var button in new[] { _previous, _playPause, _next })
            button.Opacity = button.IsEnabled ? 1 : 0.4;
    }

    private static TextBlock Glyph(int code) => new()
    {
        Text = ((char)code).ToString(),
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Botón redondo que se ilumina al pasar el puntero.</summary>
    private static Border Button(TextBlock glyph, string tip, Action click)
    {
        var idle = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));
        var hover = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var button = new Border { Background = idle, Child = glyph, ToolTip = tip, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = idle;
        Handle(button, click);
        return button;
    }

    /// <summary>Clic propio: el botón se queda con el clic (la ventana no empieza a arrastrarse).</summary>
    private static void Handle(UIElement element, Action click)
    {
        element.MouseLeftButtonDown += (_, e) => e.Handled = true;
        element.MouseLeftButtonUp += (_, e) =>
        {
            if (element.IsEnabled)
                click();
            e.Handled = true;
        };
    }
}
