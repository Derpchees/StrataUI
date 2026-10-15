using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Strata.Shell.Services.Widgets;

/// <summary>
/// Espectro de lo que suena en la salida de audio (captura de bucle de WASAPI, la misma mezcla que
/// se oye) en <see cref="Bands"/> bandas logarítmicas de 40 Hz a 16 kHz, de 0 a 1. Una sola captura
/// para todos los visualizadores; corre solo mientras alguno la usa.
/// </summary>
public static class AudioSpectrum
{
    public const int Bands = 48;
    internal const int FftSize = 2048;
    private const int FftLog = 11;

    private static readonly object Gate = new();
    private static WasapiLoopbackCapture? _capture;
    private static int _users;
    private static readonly float[] Ring = new float[FftSize];
    private static int _ringPos;
    private static int _sinceFft;
    private static int _sampleRate = 48000;
    private static readonly float[] Levels = new float[Bands];
    private static readonly Complex[] Fft = new Complex[FftSize];
    private static DateTime _lastSound;

    /// <summary>Hubo sonido en el último segundo y medio (si no, los visualizadores se quedan quietos).</summary>
    public static bool HasSound => DateTime.UtcNow - _lastSound < TimeSpan.FromSeconds(1.5);

    /// <summary>Copia los niveles actuales (0–1) en <paramref name="target"/>.</summary>
    public static void Read(float[] target)
    {
        lock (Gate)
            Array.Copy(Levels, target, Math.Min(target.Length, Levels.Length));
    }

    public static void Subscribe()
    {
        lock (Gate)
        {
            if (_users++ == 0)
                Start();
        }
    }

    public static void Unsubscribe()
    {
        lock (Gate)
        {
            if (_users == 0 || --_users > 0)
                return;
            Stop();
        }
    }

    private static void Start()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo escuchar la salida de audio para el visualizador", ex);
            _capture?.Dispose();
            _capture = null;
        }
    }

    private static void Stop()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null)
            return;
        capture.DataAvailable -= OnData;
        capture.RecordingStopped -= OnStopped;
        try
        {
            capture.StopRecording();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }
        capture.Dispose();
        Array.Clear(Levels);
    }

    /// <summary>Cambió el dispositivo de salida (o se desconectó): se vuelve a empezar con el nuevo.</summary>
    private static void OnStopped(object? sender, StoppedEventArgs e)
    {
        lock (Gate)
        {
            if (_users == 0 || !ReferenceEquals(sender, _capture))
                return;
            Stop();
            Start();
        }
    }

    private static void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture)
            return;
        var format = capture.WaveFormat;
        int channels = format.Channels;
        // La mezcla de WASAPI es flotante de 32 bits (a veces declarada como "extensible").
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
                       || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
        int bytesPerSample = format.BitsPerSample / 8;
        int frames = e.BytesRecorded / (bytesPerSample * channels);
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int offset = (f * channels + c) * bytesPerSample;
                sum += isFloat ? BitConverter.ToSingle(e.Buffer, offset)
                     : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, offset) / 32768f : 0;
            }
            Ring[_ringPos] = sum / channels;
            _ringPos = (_ringPos + 1) % FftSize;
            // Unas 30 veces por segundo es suficiente para que se vea fluido.
            if (++_sinceFft >= _sampleRate / 30)
            {
                _sinceFft = 0;
                Analyze();
            }
        }
    }

    private static void Analyze()
    {
        var window = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            window[i] = Ring[(_ringPos + i) % FftSize];
        bool loud;
        lock (Gate)
            loud = ComputeBands(window, _sampleRate, Levels, Fft);
        if (loud)
            _lastSound = DateTime.UtcNow;
    }

    /// <summary>
    /// FFT de una ventana de <see cref="FftSize"/> muestras y niveles por banda (sube rápido, baja
    /// despacio). Devuelve si alguna banda tiene sonido apreciable.
    /// </summary>
    internal static bool ComputeBands(float[] window, int sampleRate, float[] levels, Complex[] fft)
    {
        for (int i = 0; i < FftSize; i++)
        {
            fft[i].X = (float)(window[i] * FastFourierTransform.HannWindow(i, FftSize));
            fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftLog, fft);

        double minHz = 40, maxHz = Math.Min(16000, sampleRate / 2.0);
        double hzPerBin = (double)sampleRate / FftSize;
        bool loud = false;
        for (int b = 0; b < levels.Length; b++)
        {
            // Bandas logarítmicas: los graves ocupan tanto como los agudos, como se oye.
            double from = minHz * Math.Pow(maxHz / minHz, (double)b / levels.Length);
            double to = minHz * Math.Pow(maxHz / minHz, (double)(b + 1) / levels.Length);
            int first = Math.Max(1, (int)(from / hzPerBin));
            int last = Math.Max(first, Math.Min(FftSize / 2 - 1, (int)(to / hzPerBin)));
            double peak = 0;
            for (int i = first; i <= last; i++)
                peak = Math.Max(peak, Math.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y));
            // A decibeles (-60 dB → 0, 0 dB → 1), con un poco más de peso en los agudos.
            double db = 20 * Math.Log10(peak * (1 + b / (double)levels.Length) + 1e-9);
            float level = (float)Math.Clamp((db + 60) / 60, 0, 1);
            levels[b] = level > levels[b] ? level : levels[b] * 0.85f + level * 0.15f;
            if (level > 0.08f)
                loud = true;
        }
        return loud;
    }
}
