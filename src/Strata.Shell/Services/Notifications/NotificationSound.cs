using System.IO;
using System.Windows;
using System.Windows.Media;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// El sonido "Notificación" del esquema de sonidos de Windows, preparado para que se oiga:
/// - En laptops la tarjeta de sonido se duerme para ahorrar energía y tarda en despertar: lo primero
///   que le llega se pierde, y en un sonido de un segundo era casi todo. Delante va un ruido
///   inaudible que la despierta (con silencio digital no alcanzaba: hay controladores que no lo
///   cuentan como actividad).
/// - El de Windows es suave: se lleva su pico al máximo sin saturar.
/// El resultado se guarda una vez como .wav en la carpeta de datos y se reproduce con WASAPI (en la
/// PC real PlaySound devolvía éxito y no se oía nada, y luego MediaPlayer tampoco sonaba).
/// </summary>
public static class NotificationSound
{
    private static readonly TimeSpan WakeUp = TimeSpan.FromMilliseconds(1200);
    private const float WakeNoise = 0.001f;   // -60 dB: no se oye
    private const float TargetPeak = 0.95f;

    private static WasapiOut? _output;
    private static WaveFileReader? _reader;

    private static string Folder => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "Sounds");

    /// <summary>El sonido elegido (importado o el de Strata UI); con <paramref name="source"/>, ese (vista previa).</summary>
    public static void Play(string? source = null)
    {
        source ??= SoundLibrary.Current;
        Task.Run(() =>
        {
            try
            {
                var prepared = source is null ? PrepareChime() : Prepare(source);
                Application.Current?.Dispatcher.BeginInvoke(() => PlayFile(prepared));
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo preparar el sonido de notificación", ex);
            }
        });
    }

    /// <summary>
    /// Directo al dispositivo de salida con WASAPI (modo compartido, como cualquier app): MediaPlayer
    /// de WPF dejó de sonar dentro de Strata UI sin avisar de ningún error, y así cada falla queda en
    /// el registro.
    /// </summary>
    private static void PlayFile(string file)
    {
        try
        {
            Stop();
            _reader = new WaveFileReader(file);
            _output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            var output = _output;
            output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null)
                    Log.Error("El sonido de notificación se cortó", e.Exception);
                if (ReferenceEquals(output, _output))
                    Stop();
            };
            _output.Init(_reader);
            _output.Play();
            Log.Info("Sonido de notificación: " + Path.GetFileName(file));
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo reproducir el sonido de notificación", ex);
            Stop();
        }
    }

    private static void Stop()
    {
        var output = _output;
        _output = null;
        try
        {
            output?.Stop();
            output?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }
        _reader?.Dispose();
        _reader = null;
    }

    // ── Sonido propio ───────────────────────────────────────────────────────

    private const int Rate = 48000;

    /// <summary>
    /// El sonido de Strata UI: un "tin" de vaso de vidrio, simple y rápido. Un golpe casi instantáneo
    /// con un clic suave de impacto, el tono del cristal (~1,6 kHz) con una gemela apenas desafinada
    /// (el leve vibrar del vidrio) y los parciales no armónicos de un vaso, que se apagan primero.
    /// Menos de medio segundo. Se arma en código (no hay archivo que distribuir) y se guarda una vez como .wav.
    /// </summary>
    internal static string PrepareChime()
    {
        const string name = "strata-glass-2.wav";
        string target = Path.Combine(Folder, name);
        if (File.Exists(target))
            return target;
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.EnumerateFiles(Folder, "*.wav"))
            File.Delete(old);

        var chime = Chime();
        var noise = new SignalGenerator(Rate, 1) { Type = SignalGeneratorType.White, Gain = WakeNoise }.Take(WakeUp);
        var wake = new float[(int)(Rate * WakeUp.TotalSeconds)];
        int got = noise.Read(wake, 0, wake.Length);

        string temp = target + ".tmp";
        using (var writer = new WaveFileWriter(temp, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)))
        {
            foreach (var mono in new[] { wake.AsSpan(0, got).ToArray(), chime })
            {
                var stereo = new float[mono.Length * 2];
                for (int i = 0; i < mono.Length; i++)
                    stereo[2 * i] = stereo[2 * i + 1] = mono[i];
                writer.WriteSamples(stereo, 0, stereo.Length);
            }
        }
        File.Move(temp, target, overwrite: true);
        Log.Info("Sonido de notificación preparado: " + name);
        return target;
    }

    /// <summary>Las muestras del vaso (mono, 48 kHz, pico en <see cref="GlassPeak"/>).</summary>
    internal static float[] Chime()
    {
        const double fundamental = 1568;   // Sol6: un vaso de cristal algo grande, no una copa fina
        var samples = new float[(int)(Rate * 0.45)];
        // Parciales del cristal: (proporción con el tono, nivel, caída en segundos). Los agudos se apagan antes.
        (double Ratio, double Level, double Decay)[] partials =
        [
            (1.0, 1.0, 0.2),
            (1.0021, 0.55, 0.2),    // gemela desafinada: el vibrar del vidrio
            (2.73, 0.22, 0.05),
            (5.38, 0.08, 0.02),
        ];
        var random = new Random(7); // siempre el mismo clic
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / Rate;
            double attack = t < 0.0015 ? t / 0.0015 : 1; // golpe: 1,5 ms
            double tone = 0;
            foreach (var (ratio, level, decay) in partials)
                tone += level * Math.Sin(2 * Math.PI * fundamental * ratio * t) * Math.Exp(-t / decay);
            // El clic del impacto: un instante de ruido que se apaga en 3 ms.
            double click = t < 0.006 ? (random.NextDouble() * 2 - 1) * 0.25 * Math.Exp(-t / 0.0015) : 0;
            samples[i] = (float)(attack * tone + click);
        }
        // Final sin chasquido: los últimos 60 ms bajan a cero.
        int fade = (int)(Rate * 0.06);
        for (int i = 0; i < fade; i++)
            samples[samples.Length - 1 - i] *= (float)i / fade;
        float peak = samples.Max(Math.Abs);
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= GlassPeak / peak;
        return samples;
    }

    /// <summary>Un poco más bajo que el resto: un agudo de cristal al máximo molesta.</summary>
    private const float GlassPeak = 0.7f;

    /// <summary>El .wav preparado para ese sonido (se arma la primera vez o si cambió el original).</summary>
    internal static string Prepare(string source)
    {
        var info = new FileInfo(source);
        string name = $"{Path.GetFileNameWithoutExtension(source)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}.wav";
        string target = Path.Combine(Folder, name);
        if (File.Exists(target))
            return target;

        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.EnumerateFiles(Folder, "*.wav"))
            File.Delete(old);

        // .wav directo; .mp3, .m4a y .wma con Media Foundation (el de Windows).
        using WaveStream reader = Path.GetExtension(source).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? new WaveFileReader(source)
            : new MediaFoundationReader(source);
        var input = reader.ToSampleProvider();
        var format = input.WaveFormat;
        var samples = new List<float>();
        var buffer = new float[format.SampleRate * format.Channels];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        float peak = samples.Count > 0 ? samples.Max(Math.Abs) : 0;
        float gain = peak > 0.001f ? Math.Min(TargetPeak / peak, 8f) : 1f;

        var noise = new SignalGenerator(format.SampleRate, format.Channels) { Type = SignalGeneratorType.White, Gain = WakeNoise }
            .Take(WakeUp);
        var wake = new float[(int)(format.SampleRate * WakeUp.TotalSeconds) * format.Channels];
        int got = noise.Read(wake, 0, wake.Length);

        string temp = target + ".tmp";
        using (var writer = new WaveFileWriter(temp, WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)))
        {
            writer.WriteSamples(wake, 0, got);
            var loud = samples.Select(s => Math.Clamp(s * gain, -1f, 1f)).ToArray();
            writer.WriteSamples(loud, 0, loud.Length);
        }
        File.Move(temp, target, overwrite: true);
        Log.Info($"Sonido de notificación preparado: {name} (×{gain:0.0})");
        return target;
    }

    /// <summary>El archivo del sonido "Notificación" del esquema (null si el usuario lo quitó).</summary>
    private static string? FileOfScheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\Notification.Default\.Current");
        if (key?.GetValue("") is not string { Length: > 0 } value)
            return null;
        string path = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", path);
        return File.Exists(path) ? path : null;
    }
}
