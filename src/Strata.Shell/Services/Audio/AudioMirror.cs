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
    private const int CaptureMilliseconds = 20, OutputMilliseconds = 30;
    private static readonly TimeSpan MaxBuffered = TimeSpan.FromMilliseconds(90);

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
        capture.DataAvailable += (_, e) =>
        {
            // Si se acumuló (la salida se atrasó un momento), se vuelve a sincronizar.
            if (buffer.BufferedDuration > MaxBuffered)
                buffer.ClearBuffer();
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
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

    /// <summary>Captura de bucle con búfer corto (la de NAudio usa 100 ms y no deja cambiarlo).</summary>
    private sealed class LoopbackCapture(MMDevice device) : WasapiCapture(device, false, CaptureMilliseconds)
    {
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
    }
}
