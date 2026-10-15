using NAudio.Dsp;
using Strata.Shell.Services.Widgets;

namespace Strata.Tests;

/// <summary>El visualizador pone cada sonido en su banda.</summary>
public sealed class AudioSpectrumTests
{
    private static float[] Bands(Func<int, double> sample, int sampleRate = 48000)
    {
        var window = new float[AudioSpectrum.FftSize];
        for (int i = 0; i < window.Length; i++)
            window[i] = (float)sample(i);
        var levels = new float[AudioSpectrum.Bands];
        AudioSpectrum.ComputeBands(window, sampleRate, levels, new Complex[AudioSpectrum.FftSize]);
        return levels;
    }

    [Fact]
    public void A_tone_lights_its_own_band()
    {
        const int rate = 48000;
        var levels = Bands(i => 0.5 * Math.Sin(2 * Math.PI * 1000 * i / rate), rate);
        int loudest = Array.IndexOf(levels, levels.Max());
        // Banda de 1 kHz en la escala logarítmica de 40 Hz a 16 kHz.
        int expected = (int)(Math.Log(1000.0 / 40) / Math.Log(16000.0 / 40) * AudioSpectrum.Bands);
        Assert.InRange(loudest, expected - 1, expected + 1);
        Assert.True(levels[loudest] > 0.5f);
        Assert.True(levels[0] < 0.3f && levels[^1] < 0.3f); // graves y agudos apagados
    }

    [Fact]
    public void Silence_is_flat() =>
        Assert.All(Bands(_ => 0), level => Assert.True(level < 0.01f));
}
