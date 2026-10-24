using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Strata.Shell.Services.Audio;

/// <summary>
/// "Sonar también en": lo que suena en la salida principal se reproduce además en otra (los parlantes
/// de dos monitores, por ejemplo). Captura de bucle de WASAPI de la salida principal → otra salida en
/// modo compartido (Windows convierte el formato si son distintos). Sigue a la salida principal
/// cuando cambia y se pausa si la principal pasa a ser la misma que la segunda.
/// </summary>
public static class AudioMirror
{
    // Búferes cortos: la segunda salida va un poco detrás de la principal; con más búfer, el desfase
    // se oiría como eco entre los dos parlantes.
    private const int CaptureMilliseconds = 10, OutputMilliseconds = 25;
    // Lo que se deja esperando entre la captura y la segunda salida (más el retraso elegido) y cuánto
    // puede alejarse antes de corregir. Los relojes de las dos salidas no van exactamente a la misma
    // velocidad: sin corregir, el desfase crecía (o se achicaba) con los minutos.
    private const int BaseBufferedMs = 15, ToleranceMs = 25;

    private static WasapiCapture? _capture;
    private static WasapiOut? _output;
    private static BufferedWaveProvider? _buffer;
    private static string? _sourceId, _targetId;

    /// <summary>Se encendió, se apagó o cambió de salida.</summary>
    public static event Action? Changed;

    /// <summary>La segunda salida que está sonando ahora (null si no hay duplicación activa).</summary>
    public static string? ActiveTarget => _output != null ? _targetId : null;

    /// <summary>Aplica lo elegido (<see cref="StrataSettings.AudioMirrorDevice"/>) a las salidas de ahora.</summary>
    public static void Update()
    {
        string? target = Settings.Current.AudioMirrorDevice;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? source = enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID
                : null;
            MMDevice? targetDevice = null;
            if (target != null && source != null && target != source)
            {
                targetDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .FirstOrDefault(d => d.ID == target);
            }
            if (targetDevice is null)
            {
                Stop();
                return;
            }
            if (_output != null && _sourceId == source && _targetId == target)
                return;
            Stop();
            Start(enumerator.GetDevice(source!), targetDevice);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo duplicar el sonido en la otra salida", ex);
            Stop();
        }
    }

    private static void Start(MMDevice source, MMDevice target)
    {
        var capture = new LoopbackCapture(source);
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1),
            ReadFully = true, // sin sonido en la principal, silencio (no se corta la salida)
        };
        var format = capture.WaveFormat;
        byte[] scratch = [];
        capture.DataAvailable += (_, e) =>
        {
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            // Mantiene la espera en lo justo: se tira solo lo que sobra (la segunda salida se atrasó) o
            // se agrega silencio (se adelantó o se pidió más retraso), de a poco y sin vaciar todo.
            int target = BaseBufferedMs + Math.Clamp(Settings.Current.AudioMirrorDelay, 0, MaxDelay);
            double buffered = buffer.BufferedDuration.TotalMilliseconds;
            if (buffered > target + ToleranceMs || buffered < target - ToleranceMs)
            {
                int bytes = Bytes(format, Math.Abs(buffered - target));
                if (scratch.Length < bytes)
                    scratch = new byte[bytes];
                if (buffered > target)
                {
                    buffer.Read(scratch, 0, bytes);
                }
                else
                {
                    Array.Clear(scratch, 0, bytes);
                    buffer.AddSamples(scratch, 0, bytes);
                }
            }
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
                Log.Error("Sonar también en: se detuvo la captura", e.Exception);
        };
        var output = new WasapiOut(target, AudioClientShareMode.Shared, true, OutputMilliseconds);
        output.Init(buffer);
        output.Play();
        capture.StartRecording();
        (_capture, _output, _buffer, _sourceId, _targetId) = (capture, output, buffer, source.ID, target.ID);
        Log.Info($"Sonar también en: {source.FriendlyName} → {target.FriendlyName} ({capture.WaveFormat})");
        Changed?.Invoke();
    }

    public static void Stop()
    {
        if (_capture is null && _output is null)
            return;
        try
        {
            _capture?.StopRecording();
            _output?.Stop();
        }
        catch (Exception ex)
        {
            Log.Error("Sonar también en: no se pudo detener", ex);
        }
        _capture?.Dispose();
        _output?.Dispose();
        (_capture, _output, _buffer, _sourceId, _targetId) = (null, null, null, null, null);
        Log.Info("Sonar también en: detenido");
        Changed?.Invoke();
    }

    /// <summary>Volumen de una salida (0–1) en Windows; null si no existe.</summary>
    public static float? GetVolume(string deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceId).AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void SetVolume(string deviceId, float volume)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            enumerator.GetDevice(deviceId).AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0, 1);
        }
        catch (Exception ex)
        {
            Log.Error("Sonar también en: no se pudo cambiar el volumen", ex);
        }
    }

    /// <summary>El mayor retraso que se puede elegir para la segunda salida, en ms.</summary>
    public const int MaxDelay = 400;

    /// <summary>Bytes de audio para tantos milisegundos, en cuadros completos.</summary>
    private static int Bytes(WaveFormat format, double milliseconds)
    {
        int bytes = (int)(format.AverageBytesPerSecond * milliseconds / 1000);
        return bytes - bytes % format.BlockAlign;
    }

    /// <summary>Captura de bucle con búfer corto (la de NAudio usa 100 ms y no deja cambiarlo).</summary>
    private sealed class LoopbackCapture(MMDevice device) : WasapiCapture(device, false, CaptureMilliseconds)
    {
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
    }
}
