using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes.Glass;

namespace Strata.Shell.Controls;

/// <summary>
/// Fondo de vidrio líquido: muestra, a través de <see cref="LiquidGlassEffect"/>, lo que hay en
/// pantalla detrás del control. Se coloca como capa inferior de la barra o de un panel.
/// </summary>
public sealed class LiquidGlassSurface : Grid
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly LiquidGlassEffect _effect = new();
    private BackdropCapture? _capture;
    private WriteableBitmap? _bitmap;
    private Window? _window;
    private bool _loggedFirstFrame;

    /// <summary>
    /// Solo desarrollo (STRATAUI_GLASS_PREVIEW=1): no excluye la ventana de capturas y toma el fondo
    /// de la franja contigua, para poder fotografiar el efecto sin que el vidrio se capture a sí mismo.
    /// </summary>
    private static readonly bool PreviewMode = Environment.GetEnvironmentVariable("STRATAUI_GLASS_PREVIEW") == "1";

    public LiquidGlassSurface()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        _image.Effect = _effect;
        Children.Add(_image);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        SizeChanged += (_, _) => UpdateGeometry();
    }

    /// <summary>
    /// El vidrio es un fondo: no debe pedir tamaño. Si no, en paneles que se ajustan a su contenido
    /// la imagen capturada agrandaba la ventana, la ventana capturaba más área, y así en bucle.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(new Size(0, 0));
        return new Size(0, 0);
    }

    /// <summary>Redondeo del vidrio (DIP).</summary>
    public double CornerRadius { get => _effect.Radius; set => _effect.Radius = value; }

    /// <summary>Cuánto contenido de alrededor se dobla hacia dentro en los bordes (DIP).</summary>
    public double Refraction
    {
        get => _effect.Refraction;
        set
        {
            _effect.Refraction = value;
            _effect.Edge = Math.Max(10, value * 1.2);
            UpdateGeometry();
        }
    }

    /// <summary>Factor de reducción de la captura: más alto = más esmerilado.</summary>
    public int Frost { get; set; } = 3;

    public void SetLook(bool light)
    {
        _effect.Tint = light ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(70, 8, 8, 12);
        _effect.Brightness = light ? 1.08 : 0.92;
        _effect.Saturation = 1.7;
        _effect.Specular = light ? 0.7 : 0.5;
    }

    private void Start()
    {
        if (!LiquidGlassEffect.IsSupported)
            return;
        _window = Window.GetWindow(this);
        if (_window is null)
            return;
        var hwnd = new WindowInteropHelper(_window).EnsureHandle();
        if (!PreviewMode)
            BackdropCapture.ExcludeFromCapture(hwnd, true);

        _capture = new BackdropCapture(Frost, 30);
        _capture.FrameCaptured += OnFrame;
        _window.LocationChanged += OnWindowMoved;
        UpdateGeometry();
    }

    private void Stop()
    {
        if (_window != null)
        {
            _window.LocationChanged -= OnWindowMoved;
            BackdropCapture.ExcludeFromCapture(new WindowInteropHelper(_window).Handle, false);
        }
        _capture?.Dispose();
        _capture = null;
    }

    private void OnWindowMoved(object? sender, EventArgs e) => UpdateGeometry();

    /// <summary>La imagen sobresale <see cref="Refraction"/> por cada lado para poder refractar lo de alrededor.</summary>
    private void UpdateGeometry()
    {
        double pad = _effect.Refraction;
        _image.Margin = new Thickness(-pad);
        _effect.Padding = pad;
        _effect.ImageSize = new Point(Math.Max(1, ActualWidth + 2 * pad), Math.Max(1, ActualHeight + 2 * pad));

        if (_capture is null || !IsLoaded || PresentationSource.FromVisual(this) is null)
            return;
        var topLeft = PointToScreen(new Point(-pad, -pad));
        var bottomRight = PointToScreen(new Point(ActualWidth + pad, ActualHeight + pad));
        int width = (int)(bottomRight.X - topLeft.X), height = (int)(bottomRight.Y - topLeft.Y);
        int y = PreviewMode ? (int)topLeft.Y - height : (int)topLeft.Y;
        _capture.SetArea(new Int32Rect((int)topLeft.X, y, width, height));
    }

    private void OnFrame(byte[] pixels, int width, int height)
    {
        // Copia en el hilo de captura; WriteableBitmap solo se toca en el de la UI.
        var copy = (byte[])pixels.Clone();
        Dispatcher.BeginInvoke(() =>
        {
            if (_capture is null)
                return;
            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                _image.Source = _bitmap;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), copy, width * 4, 0);
            if (!_loggedFirstFrame)
            {
                _loggedFirstFrame = true;
                Log.Info($"Liquid Glass activo: captura {width}x{height}, shader {(LiquidGlassEffect.IsSupported ? "OK" : "no soportado")}");
            }
        }, System.Windows.Threading.DispatcherPriority.Render);
    }
}
