using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Winx.Shell.Services.Localization;
using Winx.Shell.Services.Media;
using Wpf.Ui.Controls;

namespace Winx.Shell.Flyouts;

/// <summary>Lo que suena ahora: carátula, título, artista, avance y controles.</summary>
public partial class MediaFlyout : FlyoutWindow
{
    // El avance solo se refresca mientras el panel está abierto.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _dragging;
    private bool _syncing;

    public MediaFlyout()
    {
        InitializeComponent();
        MediaService.Changed += Refresh;
        _timer.Tick += (_, _) => UpdateTimeline();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            MediaService.Changed -= Refresh;
        };
        Refresh();
    }

    private void Refresh()
    {
        bool has = MediaService.HasSession;
        EmptyText.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = has ? (MediaService.Title.Length > 0 ? MediaService.Title : Loc.T("Sin título")) : "";
        ArtistText.Text = MediaService.Artist;
        SourceText.Text = MediaService.AppName;
        CoverBrush.ImageSource = MediaService.Cover;
        Backdrop.Source = MediaService.Cover;
        PlayIcon.Symbol = MediaService.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        PlayButton.ToolTip = MediaService.IsPlaying ? Loc.T("Pausar") : Loc.T("Reproducir");
        PlayButton.IsEnabled = has;
        PreviousButton.IsEnabled = has && MediaService.CanPrevious;
        NextButton.IsEnabled = has && MediaService.CanNext;
        PositionSlider.IsEnabled = MediaService.CanSeek;

        var sources = MediaService.Sources;
        SourceBox.Visibility = sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        SourceBox.ItemsSource = sources;
        SourceBox.SelectedItem = sources.FirstOrDefault(s => s.Id == MediaService.CurrentSourceId);
        _syncing = false;
        UpdateTimeline();
    }

    private void UpdateTimeline()
    {
        var (position, duration) = MediaService.Timeline();
        // Transmisiones en vivo y algunas apps no dan duración: se oculta la barra.
        TimelinePanel.Visibility = duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        if (duration <= TimeSpan.Zero)
            return;
        DurationText.Text = Format(duration);
        if (_dragging)
            return;
        PositionSlider.Maximum = duration.TotalSeconds;
        PositionSlider.Value = position.TotalSeconds;
        PositionText.Text = Format(position);
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void Position_DragStart(object sender, MouseButtonEventArgs e) => _dragging = true;

    private void Position_DragEnd(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        MediaService.Seek(TimeSpan.FromSeconds(PositionSlider.Value));
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => MediaService.PlayPause();

    private void Previous_Click(object sender, RoutedEventArgs e) => MediaService.Previous();

    private void Next_Click(object sender, RoutedEventArgs e) => MediaService.Next();

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && SourceBox.SelectedItem is MediaSource source)
            MediaService.Choose(source.Id);
    }
}
