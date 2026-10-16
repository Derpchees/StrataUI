using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using Strata.Shell.Services.Themes.Glass;

namespace Strata.Shell;

/// <summary>
/// Barra totalmente transparente (tema Transparente total, o el estilo de la barra de ese monitor):
/// sin fondo, borde ni desenfoque. Para que se lea sobre cualquier
/// fondo, cada parte (Inicio, cada ventana, multimedia, cada módulo, el reloj) toma texto e íconos
/// claros u oscuros según lo que tenga detrás; si el fondo es claro de un lado y oscuro del otro,
/// cada lado se ve distinto.
/// </summary>
/// <remarks>
/// Lo de detrás se lee de la pantalla con la barra excluida de la captura solo durante la lectura
/// (si ya lo estaba por el vidrio líquido, se deja así). Detrás de una barra casi siempre está el
/// fondo de pantalla (las ventanas maximizadas no la tapan): se lee cuando algo cambia (el fondo, el
/// tamaño o el contenido de la barra) y, como los fondos animados (Wallpaper Engine) cambian solos,
/// también cada pocos segundos con un único temporizador para todas las barras transparentes.
/// </remarks>
public partial class Taskbar
{
    private bool _transparent;
    private bool _contrastHooked;
    private bool _sampling;

    private static readonly Brush DarkText = Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush LightText = Frozen(Colors.White);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Aplica (o quita) el modo transparente. True si la barra quedó transparente.</summary>
    private bool ApplyTransparency()
    {
        bool wanted = ThemeManager.ClearBarFor(Device) && !_closed;
        bool changed = wanted != _transparent;
        _transparent = wanted;
        UpdateContrastRefresh();
        if (!wanted)
        {
            if (changed)
            {
                BarBorder.SetResourceReference(Border.BackgroundProperty, "StrataTaskbarBackgroundBrush");
                BarBorder.SetResourceReference(Border.BorderBrushProperty, "StrataTaskbarEdgeBrush");
                GlassHighlight.SetResourceReference(VisibilityProperty, "StrataGlassVisibility");
                ClearContrast();
            }
            return false;
        }

        // Transparent (no null) para que la barra siga recibiendo los clics en los huecos.
        BarBorder.Background = Brushes.Transparent;
        BarBorder.BorderBrush = Brushes.Transparent;
        GlassHighlight.Visibility = Visibility.Collapsed;
        BarBorder.Margin = FloatingMargin();
        UpdateGlassSurface();
        WindowEffects.SetBlur(this, BarTheme.Backdrop, clear: true);
        WindowEffects.SetRoundedRegion(this, null, 0);
        // WPF y WPF-UI terminan de aplicar lo suyo sobre la ventana después: se repite cuando quedan
        // inactivos, o la parte transparente puede quedar negra.
        if (changed)
            Dispatcher.BeginInvoke(() =>
            {
                if (_transparent)
                    WindowEffects.SetBlur(this, BarTheme.Backdrop, clear: true);
            }, DispatcherPriority.ApplicationIdle);
        HookContrast();
        ScheduleContrast();
        return true;
    }

    private void HookContrast()
    {
        if (_contrastHooked)
            return;
        _contrastHooked = true;
        // Lo que mueve las partes de la barra: su tamaño, el de la lista de ventanas o el área de estado.
        SizeChanged += (_, _) => ScheduleContrast();
        LocationChanged += (_, _) => ScheduleContrast();
        TaskList.SizeChanged += (_, _) => ScheduleContrast();
        TaskList.ItemContainerGenerator.ItemsChanged += (_, _) => ScheduleContrast();
        TaskScroller.ScrollChanged += (_, _) => ScheduleContrast();
        StatusPanel.SizeChanged += (_, _) => ScheduleContrast();
        TaskArea.SizeChanged += (_, _) => ScheduleContrast();
    }

    // ── Relectura periódica para fondos animados ────────────────────────────

    private static readonly HashSet<Taskbar> TransparentBars = new();
    private static DispatcherTimer? _contrastRefresh;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private bool _closed;

    private void UpdateContrastRefresh()
    {
        if (_transparent)
            TransparentBars.Add(this);
        else
            TransparentBars.Remove(this);
        if (TransparentBars.Count == 0)
        {
            _contrastRefresh?.Stop();
            return;
        }
        _contrastRefresh ??= new DispatcherTimer(RefreshInterval, DispatcherPriority.Background, (_, _) =>
        {
            foreach (var bar in TransparentBars.ToList())
                bar.SampleContrast();
        }, Dispatcher);
        _contrastRefresh.Start();
    }

    /// <summary>La barra se cierra: deja de leerse el fondo detrás de ella.</summary>
    private void StopContrast()
    {
        _closed = true;
        _transparent = false;
        PendingContrast.Remove(this);
        UpdateContrastRefresh();
    }

    // ── Lectura agrupada: un solo temporizador de un disparo para todas las barras ─────

    private static readonly HashSet<Taskbar> PendingContrast = new();
    private static DispatcherTimer? _contrastTimer;

    /// <summary>Pide leer el fondo de esta barra en un momento (varios cambios seguidos cuentan como uno).</summary>
    public void ScheduleContrast()
    {
        if (!_transparent)
            return;
        PendingContrast.Add(this);
        _contrastTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
        {
            _contrastTimer!.Stop();
            var bars = PendingContrast.ToList();
            PendingContrast.Clear();
            foreach (var bar in bars)
                bar.SampleContrast();
        }, Dispatcher);
        _contrastTimer.Stop();
        _contrastTimer.Start();
    }

    private async void SampleContrast()
    {
        if (!_transparent || _sampling || !IsVisible)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || ActualWidth <= 0)
            return;
        _sampling = true;
        bool toggle = !BackdropCapture.IsExcludedFromCapture(hwnd);
        try
        {
            if (toggle)
            {
                BackdropCapture.ExcludeFromCapture(hwnd, true);
                await Task.Delay(100); // que DWM componga al menos un cuadro sin la barra
            }
            var origin = PointToScreen(new Point(0, 0));
            var corner = PointToScreen(new Point(ActualWidth, ActualHeight));
            var area = new Int32Rect((int)origin.X, (int)origin.Y, (int)(corner.X - origin.X), (int)(corner.Y - origin.Y));
            if (area.Width <= 0 || area.Height <= 0)
                return;
            // A la mitad de resolución: sobra para un promedio y es la cuarta parte de trabajo.
            int w = Math.Max(1, area.Width / 2), h = Math.Max(1, area.Height / 2);
            var pixels = new byte[w * h * 4];
            bool ok = BackdropCapture.Grab(area, w, h, pixels);
            if (toggle)
            {
                BackdropCapture.ExcludeFromCapture(hwnd, false);
                toggle = false;
            }
            if (ok && _transparent)
                ApplyContrast(pixels, w, h, area);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer el fondo detrás de la barra", ex);
        }
        finally
        {
            if (toggle)
                BackdropCapture.ExcludeFromCapture(hwnd, false);
            _sampling = false;
        }
    }

    /// <summary>Partes de la barra que se colorean por separado.</summary>
    private IEnumerable<FrameworkElement> ContrastZones()
    {
        yield return StartButton;
        for (int i = 0; i < PinnedList.Items.Count; i++)
        {
            if (PinnedList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement pin)
                yield return pin;
        }
        for (int i = 0; i < TaskList.Items.Count; i++)
        {
            if (TaskList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement task)
                yield return task;
        }
        yield return MediaButton;
        yield return MediaPrevious;
        yield return MediaPlay;
        yield return MediaNext;
        yield return ChevronButton;
        yield return NetworkButton;
        yield return BluetoothButton;
        yield return BrightnessButton;
        yield return VolumeButton;
        yield return BatteryButton;
        yield return NotificationsButton;
        yield return CpuButton;
        yield return GpuButton;
        yield return RamButton;
        yield return FpsButton;
        yield return FreeRamButton;
        yield return ClockButton;
        yield return ShowDesktopArea;
    }

    private void ApplyContrast(byte[] pixels, int w, int h, Int32Rect area)
    {
        double sx = (double)w / area.Width, sy = (double)h / area.Height;
        foreach (var zone in ContrastZones())
        {
            if (!zone.IsVisible || zone.ActualWidth <= 0)
                continue;
            var a = zone.PointToScreen(new Point(0, 0));
            var b = zone.PointToScreen(new Point(zone.ActualWidth, zone.ActualHeight));
            int x0 = Math.Clamp((int)((Math.Min(a.X, b.X) - area.X) * sx), 0, w - 1);
            int x1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.X, b.X) - area.X) * sx), x0 + 1, w);
            int y0 = Math.Clamp((int)((Math.Min(a.Y, b.Y) - area.Y) * sy), 0, h - 1);
            int y1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.Y, b.Y) - area.Y) * sy), y0 + 1, h);
            double luminance = AverageLuminance(pixels, w, x0, x1, y0, y1);
            // Con margen: cerca del gris medio se mantiene el color que ya tenía (en fondos
            // animados, si no, parpadearía).
            var current = zone.Resources["StrataForegroundBrush"];
            var brush = ReferenceEquals(current, DarkText) ? (luminance > 0.5 ? DarkText : LightText)
                : ReferenceEquals(current, LightText) ? (luminance > 0.6 ? DarkText : LightText)
                : luminance > 0.55 ? DarkText : LightText;
            if (!ReferenceEquals(zone.Resources["StrataForegroundBrush"], brush))
                zone.Resources["StrataForegroundBrush"] = brush;
        }
    }

    /// <summary>Luminancia relativa promedio (0 negro, 1 blanco) de un rectángulo BGRA.</summary>
    internal static double AverageLuminance(byte[] pixels, int stride, int x0, int x1, int y0, int y1)
    {
        double sum = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * stride + x) * 4;
                sum += 0.0722 * Linear(pixels[i]) + 0.7152 * Linear(pixels[i + 1]) + 0.2126 * Linear(pixels[i + 2]);
                count++;
            }
        }
        // Se compara en escala perceptual (como sRGB) para que el umbral 0.55 sea "más claro que gris medio".
        return count == 0 ? 0 : Math.Pow(sum / count, 1 / 2.2);
    }

    private static double Linear(byte channel) => Math.Pow(channel / 255.0, 2.2);

    private void ClearContrast()
    {
        foreach (var zone in ContrastZones())
            zone.Resources.Remove("StrataForegroundBrush");
    }
}
