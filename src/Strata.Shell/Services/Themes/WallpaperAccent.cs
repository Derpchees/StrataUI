using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Color de acento sacado del fondo de pantalla (Hub › Apariencia › "Del fondo de pantalla"). Con
/// Wallpaper Engine se usa la vista previa del fondo que tiene puesto (los animados no dejan otra
/// imagen fija); si no, el archivo del fondo de Windows o su color sólido. Se recalcula cuando
/// cambia el fondo (aviso de Windows o config.json de Wallpaper Engine), no con temporizadores.
/// </summary>
public static class WallpaperAccent
{
    private static int _generation;
    private static bool _started;

    /// <summary>Último color calculado; null mientras no hay ninguno.</summary>
    public static Color? Current { get; private set; }

    /// <summary>Cambió <see cref="Current"/> (en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>Empieza a seguir el fondo si la opción está activa (y deja de hacerlo si no).</summary>
    public static void Update()
    {
        bool wanted = Settings.Current.AccentFromWallpaper;
        if (wanted == _started)
            return;
        _started = wanted;
        if (wanted)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            WallpaperEngine.Changed += Refresh;
            Refresh();
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            WallpaperEngine.Changed -= Refresh;
        }
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        WallpaperEngine.Changed -= Refresh;
        _started = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // El fondo de Windows avisa con "Desktop".
        if (e.Category == UserPreferenceCategory.Desktop)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    /// <summary>Vuelve a calcular el color en segundo plano.</summary>
    public static void Refresh()
    {
        int generation = ++_generation;
        _ = Task.Run(() =>
        {
            string? source = null;
            Color? color = null;
            try
            {
                source = WallpaperEngine.CurrentWallpaperImage() ?? Wallpaper.Current;
                color = string.IsNullOrEmpty(source) || !File.Exists(source)
                    ? SolidBackground() is { } solid ? Normalize(solid) : null
                    : FromImage(source);
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo sacar el color del fondo", ex);
            }
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (generation != _generation || color == Current)
                    return;
                Current = color;
                Log.Info($"Acento del fondo: {color} ({source})");
                Changed?.Invoke();
            });
        });
    }

    private static Color? FromImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        // Muestra pequeña: basta para el color dominante y no gasta memoria.
        double scale = Math.Min(1, 96.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource small = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return Pick(pixels);
    }

    /// <summary>Color sólido del escritorio ("Control Panel\Colors\Background" = "R G B").</summary>
    private static Color? SolidBackground()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (key?.GetValue("Background") as string)?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: 3 } && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g)
               && byte.TryParse(parts[2], out var b)
            ? Color.FromRgb(r, g, b)
            : null;
    }

    /// <summary>
    /// Color dominante de una imagen (píxeles BGRA): el tono con más peso entre los colores vivos
    /// (saturación × brillo), promediado con sus vecinos. Si casi no hay color, el promedio (gris).
    /// </summary>
    internal static Color Pick(byte[] bgra)
    {
        const int bins = 36;
        var weight = new double[bins];
        var r = new double[bins];
        var g = new double[bins];
        var b = new double[bins];
        double total = 0, sumR = 0, sumG = 0, sumB = 0;
        int count = 0;

        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128)
                continue;
            byte pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
            count++;
            sumR += pr;
            sumG += pg;
            sumB += pb;
            var (h, s, v) = ToHsv(Color.FromRgb(pr, pg, pb));
            if (s < 0.25 || v < 0.22)
                continue;
            double w = s * v * v;
            int bin = (int)(h / 360 * bins) % bins;
            weight[bin] += w;
            r[bin] += pr * w;
            g[bin] += pg * w;
            b[bin] += pb * w;
            total += w;
        }
        if (count == 0)
            return Normalize(Colors.Gray);

        // Muy poco color (fondo gris, negro o blanco): un gris con un toque del promedio.
        if (total / count < 0.02)
            return Normalize(Color.FromRgb((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count)));

        int best = 0;
        double bestScore = -1;
        for (int i = 0; i < bins; i++)
        {
            double score = weight[i] + 0.5 * (weight[(i + bins - 1) % bins] + weight[(i + 1) % bins]);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        double tw = 0, tr = 0, tg = 0, tb = 0;
        foreach (int i in new[] { (best + bins - 1) % bins, best, (best + 1) % bins })
        {
            tw += weight[i];
            tr += r[i];
            tg += g[i];
            tb += b[i];
        }
        return Normalize(Color.FromRgb((byte)(tr / tw), (byte)(tg / tw), (byte)(tb / tw)));
    }

    /// <summary>Lo deja como acento: color vivo (si lo tiene) y brillo medio, ni negro ni blanco.</summary>
    internal static Color Normalize(Color color)
    {
        var (h, s, v) = ToHsv(color);
        if (s >= 0.15)
            s = Math.Clamp(s, 0.5, 0.9);
        v = Math.Clamp(v, 0.5, 0.85);
        return FromHsv(h, s, v);
    }

    internal static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (h < 0)
            h += 360;
        return (h, max == 0 ? 0 : delta / max, max);
    }

    internal static Color FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
