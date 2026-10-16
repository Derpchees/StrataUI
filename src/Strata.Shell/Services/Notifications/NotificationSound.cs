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
/// El resultado se guarda una vez como .wav en la carpeta de datos y se reproduce con MediaPlayer
/// (en la PC real PlaySound devolvía éxito y no se oía nada).
/// </summary>
public static class NotificationSound
{
    private static readonly TimeSpan WakeUp = TimeSpan.FromMilliseconds(1200);
    private const float WakeNoise = 0.001f;   // -60 dB: no se oye
    private const float TargetPeak = 0.95f;

    private static MediaPlayer? _player;
    private static string? _playerFile;

    private static string Folder => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "Sounds");

    public static void Play()
    {
        Task.Run(() =>
        {
            try
            {
                var prepared = PrepareChime();
                Application.Current?.Dispatcher.BeginInvoke(() => PlayFile(prepared));
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo preparar el sonido de notificación", ex);
            }
        });
    }

    private static void PlayFile(string file)
    {
        try
        {
            _player ??= new MediaPlayer { Volume = 1 };
            if (_playerFile != file)
            {
                _player.Open(new Uri(file));
                _playerFile = file;
            }
            _player.Stop();
            _player.Position = TimeSpan.Zero;
            _player.Play();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo reproducir el sonido de notificación", ex);
        }
    }

    // ── Sonido propio ───────────────────────────────────────────────────────

    private const int Rate = 48000;

    /// <summary>
    /// El sonido de Strata UI: un solo tono corto y redondo, Do5 (523 Hz), como de vibráfono: una
    /// senoidal con una gemela apenas desafinada (calidez), armónicos muy suaves, ataque de 12 ms y
    /// medio segundo de caída. Se arma en código (no hay archivo que distribuir) y se guarda una vez como .wav.
    /// </summary>
    internal static string PrepareChime()
    {
        const string name = "strata-chime-3.wav";
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

    /// <summary>Las muestras de la campanita (mono, 48 kHz, pico en <see cref="TargetPeak"/>).</summary>
    internal static float[] Chime()
    {
        var samples = new float[(int)(Rate * 0.5)];
        void Note(double frequency, double start, double decay, double level)
        {
            int from = (int)(start * Rate);
            for (int i = from; i < samples.Length; i++)
            {
                double t = (double)(i - from) / Rate;
                // Ataque suave (seno), sin el golpe metálico de una campana.
                double attack = t < 0.012 ? Math.Sin(t / 0.012 * Math.PI / 2) : 1;
                double envelope = attack * Math.Exp(-t / decay) * level;
                double w = 2 * Math.PI * frequency * t;
                double tone = Math.Sin(w)
                              + 0.35 * Math.Sin(w * 1.0035)            // gemela desafinada: calidez
                              + 0.10 * Math.Sin(2 * w) * Math.Exp(-t / 0.12) // brillo solo al principio
                              + 0.025 * Math.Sin(3 * w) * Math.Exp(-t / 0.08);
                samples[i] += (float)(tone * envelope);
            }
        }
        Note(523.25, 0, 0.16, 1.0);     // Do5: un solo tono, corto
        // Final sin chasquido: los últimos 80 ms bajan a cero.
        int fade = (int)(Rate * 0.08);
        for (int i = 0; i < fade; i++)
            samples[samples.Length - 1 - i] *= (float)i / fade;
        float peak = samples.Max(Math.Abs);
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= TargetPeak / peak;
        return samples;
    }

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

        using var reader = new WaveFileReader(source);
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
