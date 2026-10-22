using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;

namespace Strata.Shell.Files;

/// <summary>
/// Visor de Archivos para imágenes, video y audio: ajusta la imagen a la ventana (rueda para acercar
/// donde está el mouse, arrastrar para moverla, doble clic para el tamaño real), reproduce video y
/// audio con Media Foundation y pasa al anterior o al siguiente de la carpeta con las flechas. Una
/// sola ventana: abrir otro archivo la reutiliza.
/// </summary>
public partial class MediaViewerWindow : FluentWindow
{
    private static MediaViewerWindow? _current;
    private static double _volume = 0.8;
    private static bool _muted;

    private IReadOnlyList<string> _items = [];
    private int _index;
    private MediaKind _kind;
    private bool _playing, _seeking, _updatingSeek, _fullScreen;
    private Rect _boundsBefore;
    private WindowState _stateBefore;
    private int _rotation;
    private BitmapSource? _bitmap;
    private Point? _panFrom;
    private Matrix _panStart;
    private CancellationTokenSource? _loading;
    private readonly DispatcherTimer _clock, _idle;

    /// <summary>Muestra <paramref name="items"/>[<paramref name="index"/>]; las flechas recorren la lista.</summary>
    public static void Open(IReadOnlyList<string> items, int index)
    {
        var viewer = _current ??= new MediaViewerWindow();
        viewer.Show();
        if (viewer.WindowState == WindowState.Minimized)
            viewer.WindowState = WindowState.Normal;
        viewer.Topmost = true;
        viewer.Activate();
        viewer.Topmost = false;
        viewer._items = items;
        viewer.ShowItem(Math.Clamp(index, 0, items.Count - 1));
    }

    private MediaViewerWindow()
    {
        InitializeComponent();
        FluentBackdrop.Follow(this);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => UpdatePosition();
        // Con video sonando, los controles y el puntero se esconden si el mouse no se mueve.
        _idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_kind == MediaKind.Video && _playing && !Controls.IsMouseOver)
                SetChromeVisible(false);
        };
        MouseMove += (_, _) =>
        {
            SetChromeVisible(true);
            _idle.Stop();
            _idle.Start();
        };
        PreviewKeyDown += Window_PreviewKeyDown;
        VolumeSlider.Value = _volume;
        UpdateVolumeIcon();
        Closed += (_, _) =>
        {
            _loading?.Cancel();
            _clock.Stop();
            _idle.Stop();
            Video.Stop();
            Video.Source = null;
            _current = null;
            MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
        };
        Loaded += (_, _) => Motion.In(Root, Root, TaskbarPosition.Bottom, 0.8);
    }

    // ── Mostrar ─────────────────────────────────────────────────────────────

    private void ShowItem(int index)
    {
        _loading?.Cancel();
        _clock.Stop();
        Video.Stop();
        Video.Source = null;
        _playing = false;
        _bitmap = null;
        Picture.Source = null;

        _index = index;
        string path = _items[index];
        _kind = MediaKinds.Of(path);
        string name = System.IO.Path.GetFileName(path);
        Title = name;
        ViewerTitleBar.Title = name;
        CounterText.Text = _items.Count > 1 ? Loc.F("{0} de {1}", index + 1, _items.Count) : "";
        PreviousButton.IsEnabled = NextButton.IsEnabled = _items.Count > 1;

        bool media = _kind is MediaKind.Video or MediaKind.Audio;
        ImageHost.Visibility = _kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        Video.Visibility = _kind == MediaKind.Video ? Visibility.Visible : media ? Visibility.Hidden : Visibility.Collapsed;
        AudioCard.Visibility = _kind == MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = Visibility.Collapsed;
        TimeRow.Visibility = PlaybackButtons.Visibility = VolumePanel.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        ZoomButtons.Visibility = _kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        FullScreenButton.Visibility = _kind == MediaKind.Audio ? Visibility.Collapsed : Visibility.Visible;
        SetChromeVisible(true);
        Stage.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180)));

        switch (_kind)
        {
            case MediaKind.Image:
                _ = LoadImageAsync(path);
                break;
            case MediaKind.Video or MediaKind.Audio:
                PositionText.Text = DurationText.Text = "";
                _updatingSeek = true;
                Seek.Value = 0;
                _updatingSeek = false;
                if (_kind == MediaKind.Audio)
                    _ = LoadSongAsync(path);
                Video.Source = new Uri(path);
                ApplyVolume();
                Play();
                break;
            default:
                ShowError(Loc.T("Este tipo de archivo no se puede ver aquí."));
                break;
        }
    }

    private void ShowError(string message)
    {
        ImageHost.Visibility = AudioCard.Visibility = Visibility.Collapsed;
        Video.Visibility = Visibility.Collapsed;
        TimeRow.Visibility = PlaybackButtons.Visibility = VolumePanel.Visibility = ZoomButtons.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorState.Visibility = Visibility.Visible;
    }

    private void SetChromeVisible(bool visible)
    {
        double target = visible ? 1 : 0;
        foreach (var element in new UIElement[] { Controls, PreviousButton, NextButton })
        {
            if (Math.Abs(element.Opacity - target) > 0.01)
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(visible ? 120 : 400)));
        }
        Controls.IsHitTestVisible = visible;
        Stage.Cursor = visible ? null : Cursors.None;
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => Step(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Step(1);

    private void Step(int delta)
    {
        if (_items.Count > 1)
            ShowItem((_index + delta + _items.Count) % _items.Count);
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;
        Video.Pause();
        SetPlaying(false);
        FileOperations.Open(_items[_index]);
    }

    // ── Imagen ──────────────────────────────────────────────────────────────

    private async Task LoadImageAsync(string path)
    {
        var cts = _loading = new CancellationTokenSource();
        BitmapSource? bitmap;
        try
        {
            bitmap = await Task.Run(() => LoadOriented(path), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested)
            return;
        if (bitmap is null)
        {
            ShowError(Loc.T("El archivo está dañado o Windows no tiene el códec para este formato."));
            return;
        }
        _bitmap = bitmap;
        _rotation = 0;
        Picture.LayoutTransform = Transform.Identity;
        Picture.Source = bitmap;
        _ = Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// La imagen girada como la tomó la cámara (orientación EXIF: WPF no la aplica) y, de un .ico, el
    /// tamaño más grande. Null si no se puede decodificar.
    /// </summary>
    private static BitmapSource? LoadOriented(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            ushort orientation = 1;
            try
            {
                if (frame is BitmapFrame { Metadata: BitmapMetadata metadata } && metadata.GetQuery("System.Photo.Orientation") is ushort value)
                    orientation = value;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                // Formatos sin metadatos (PNG, BMP…).
            }
            Transform? turn = orientation switch
            {
                2 => new ScaleTransform(-1, 1),
                3 => new RotateTransform(180),
                4 => new ScaleTransform(1, -1),
                5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
                6 => new RotateTransform(90),
                7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
                8 => new RotateTransform(270),
                _ => null,
            };
            if (turn != null)
                frame = new TransformedBitmap(frame, turn);
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException
                                       or ArgumentException or InvalidOperationException or FileFormatException
                                       or System.Runtime.InteropServices.COMException)
        {
            Log.Info($"Visor: no se pudo abrir {path} ({ex.Message.Trim()})");
            return null;
        }
    }

    /// <summary>Puntos de pantalla por píxel de la imagen cuando está ajustada a la ventana.</summary>
    private double FitPixelScale => _bitmap is { PixelWidth: > 0 } bitmap && Picture.ActualWidth > 0
        ? Picture.ActualWidth * VisualTreeHelper.GetDpi(this).DpiScaleX / bitmap.PixelWidth
        : 1;

    private double Zoom => PictureTransform.Matrix.M11;

    private void Fit()
    {
        PictureTransform.Matrix = Matrix.Identity;
        UpdateZoomText();
    }

    /// <summary>Acerca o aleja <paramref name="factor"/> veces, dejando quieto el punto <paramref name="at"/> (de la imagen).</summary>
    private void ZoomBy(double factor, Point? at = null)
    {
        if (_bitmap is null)
            return;
        var matrix = PictureTransform.Matrix;
        double target = Math.Clamp(Zoom * factor, 1, 40 / FitPixelScale);
        factor = target / Zoom;
        var center = matrix.Transform(at ?? new Point(Picture.ActualWidth / 2, Picture.ActualHeight / 2));
        matrix.ScaleAt(factor, factor, center.X, center.Y);
        PictureTransform.Matrix = target <= 1.001 ? Matrix.Identity : matrix;
        UpdateZoomText();
    }

    private void UpdateZoomText() => ZoomText.Text = $"{Math.Round(Zoom * FitPixelScale * 100)} %";

    private void ImageHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(Picture));
        e.Handled = true;
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Doble clic: tamaño real donde está el mouse, o de vuelta a ajustada.
            if (Zoom > 1.001)
                Fit();
            else
                ZoomBy(1 / FitPixelScale, e.GetPosition(Picture));
            return;
        }
        if (Zoom <= 1.001)
            return;
        _panFrom = e.GetPosition(ImageHost);
        _panStart = PictureTransform.Matrix;
        ImageHost.CaptureMouse();
        ImageHost.Cursor = Cursors.SizeAll;
    }

    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is not { } from)
            return;
        var delta = e.GetPosition(ImageHost) - from;
        var matrix = _panStart;
        matrix.Translate(delta.X, delta.Y);
        PictureTransform.Matrix = matrix;
    }

    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _panFrom = null;
        ImageHost.ReleaseMouseCapture();
        ImageHost.Cursor = Cursors.Hand;
    }

    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateZoomText();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);
    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();
    private void ActualSize_Click(object sender, RoutedEventArgs e) => ActualSize();
    private void Rotate_Click(object sender, RoutedEventArgs e) => Rotate();

    private void ActualSize()
    {
        Fit();
        ZoomBy(1 / FitPixelScale);
    }

    private void Rotate()
    {
        if (_bitmap is null)
            return;
        _rotation = (_rotation + 90) % 360;
        Picture.LayoutTransform = new RotateTransform(_rotation);
        Fit();
    }

    // ── Video y audio ───────────────────────────────────────────────────────

    private async Task LoadSongAsync(string path)
    {
        AudioTitle.Text = System.IO.Path.GetFileNameWithoutExtension(path);
        AudioArtist.Text = "";
        var accent = ThemeManager.Accent;
        AudioGradientBottom.Color = accent;
        AudioGradientTop.Color = Palette.Blend(accent, Colors.White, 0.35);
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var music = await file.Properties.GetMusicPropertiesAsync();
            if (_items.Count == 0 || _items[_index] != path)
                return;
            if (!string.IsNullOrWhiteSpace(music.Title))
                AudioTitle.Text = music.Title;
            AudioArtist.Text = string.Join(" · ", new[] { music.Artist, music.Album }.Where(t => !string.IsNullOrWhiteSpace(t)));
        }
        catch (Exception ex)
        {
            // Sin etiquetas (o un formato que Windows no lee): queda el nombre del archivo.
            Log.Info($"Visor: sin datos de la canción {path} ({ex.Message.Trim()})");
        }
    }

    private void Play()
    {
        Video.Play();
        SetPlaying(true);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        PlayIcon.Symbol = playing ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        if (playing)
        {
            _clock.Start();
            _idle.Start();
        }
        else
        {
            _clock.Stop();
            SetChromeVisible(true);
        }
    }

    private void TogglePlay()
    {
        if (_kind is not (MediaKind.Video or MediaKind.Audio))
            return;
        if (_playing)
        {
            Video.Pause();
            SetPlaying(false);
        }
        else
        {
            Play();
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void Video_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TogglePlay();

    private void Media_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Video.NaturalDuration.HasTimeSpan)
        {
            Seek.Maximum = Math.Max(1, Video.NaturalDuration.TimeSpan.TotalSeconds);
            DurationText.Text = Format(Video.NaturalDuration.TimeSpan);
        }
        UpdatePosition();
    }

    private void Media_MediaEnded(object sender, RoutedEventArgs e)
    {
        // El audio sigue con el siguiente de la carpeta (como una lista); el video se queda al final.
        if (_kind == MediaKind.Audio && _items.Count > 1 && _index < _items.Count - 1
            && MediaKinds.Of(_items[_index + 1]) == MediaKind.Audio)
        {
            ShowItem(_index + 1);
            return;
        }
        Video.Pause();
        Video.Position = TimeSpan.Zero;
        SetPlaying(false);
        UpdatePosition();
    }

    private void Media_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Log.Info($"Visor: no se pudo reproducir {_items[_index]} ({e.ErrorException?.Message.Trim()})");
        SetPlaying(false);
        ShowError(Loc.T("Windows no tiene el códec para reproducir este formato."));
    }

    private void UpdatePosition()
    {
        if (_seeking)
            return;
        var position = Video.Position;
        _updatingSeek = true;
        Seek.Value = Math.Min(Seek.Maximum, position.TotalSeconds);
        _updatingSeek = false;
        PositionText.Text = Format(position);
    }

    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeek)
            return;
        Video.Position = TimeSpan.FromSeconds(e.NewValue);
        PositionText.Text = Format(Video.Position);
    }

    private void Seek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void Seek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        Video.Position = TimeSpan.FromSeconds(Seek.Value);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _volume = e.NewValue;
        _muted = false;
        ApplyVolume();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        Video.Volume = _muted ? 0 : _volume;
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon() =>
        VolumeIcon.Symbol = _muted || _volume <= 0.001 ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;

    // ── Pantalla completa ───────────────────────────────────────────────────

    /// <summary>Sobre todo el monitor, también encima de la barra (maximizar respeta el área de trabajo).</summary>
    private void SetFullScreen(bool full)
    {
        if (full == _fullScreen)
            return;
        _fullScreen = full;
        if (full)
        {
            _stateBefore = WindowState;
            _boundsBefore = new Rect(Left, Top, Width, Height);
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
            var dpi = VisualTreeHelper.GetDpi(this);
            WindowState = WindowState.Normal;
            ViewerTitleBar.Visibility = Visibility.Collapsed;
            Topmost = true;
            Left = screen.Left / dpi.DpiScaleX;
            Top = screen.Top / dpi.DpiScaleY;
            Width = screen.Width / dpi.DpiScaleX;
            Height = screen.Height / dpi.DpiScaleY;
        }
        else
        {
            Topmost = false;
            ViewerTitleBar.Visibility = Visibility.Visible;
            (Left, Top, Width, Height) = (_boundsBefore.Left, _boundsBefore.Top, _boundsBefore.Width, _boundsBefore.Height);
            WindowState = _stateBefore;
        }
        FullScreenIcon.Symbol = full ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24;
        Fit();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

    // ── Teclado ─────────────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool handled = true;
        switch (e.Key)
        {
            case Key.Left or Key.PageUp:
                Step(-1);
                break;
            case Key.Right or Key.PageDown:
                Step(1);
                break;
            case Key.Home when _items.Count > 0:
                ShowItem(0);
                break;
            case Key.End when _items.Count > 0:
                ShowItem(_items.Count - 1);
                break;
            case Key.Space:
                TogglePlay();
                break;
            case Key.F11 or Key.F:
                if (_kind != MediaKind.Audio)
                    SetFullScreen(!_fullScreen);
                break;
            case Key.Escape:
                if (_fullScreen)
                    SetFullScreen(false);
                else
                    Close();
                break;
            case Key.OemPlus or Key.Add:
                ZoomBy(1.25);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomBy(1 / 1.25);
                break;
            case Key.D0 or Key.NumPad0:
                Fit();
                break;
            case Key.D1 or Key.NumPad1:
                ActualSize();
                break;
            case Key.R:
                Rotate();
                break;
            case Key.M:
                _muted = !_muted;
                ApplyVolume();
                break;
            default:
                handled = false;
                break;
        }
        e.Handled = handled;
    }
}
