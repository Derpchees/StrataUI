using Strata.Shell.Services;

namespace Strata.Tests;

/// <summary>El contador de FPS no depende de cuándo entrega ETW los eventos (llegan en tandas).</summary>
public sealed class FrameTrackTests
{
    /// <summary>
    /// Un juego a ritmo fijo cuyos cuadros llegan en tandas irregulares (cada 0,6–1,4 s) y se leen
    /// cada segundo, desfasado: cada lectura da el ritmo real, sin el vaivén de contar por llegada.
    /// </summary>
    [Theory]
    [InlineData(112.0)]
    [InlineData(59.94)]
    [InlineData(240.0)]
    public void Steady_rate_is_steady_despite_batches(double fps)
    {
        var random = new Random(7);
        var track = new Sensors.FrameTrack();
        double interval = 1000 / fps, nextFrame = 0, delivered = 0, latest = 0;
        var readings = new List<int>();
        for (double clock = 0; clock < 20_000; clock += 1000)
        {
            // Tandas que llegaron antes de esta lectura.
            while (delivered + 600 + random.NextDouble() * 800 < clock)
            {
                delivered += 600 + random.NextDouble() * 800;
                for (; nextFrame <= delivered; nextFrame += interval)
                {
                    track.Add(nextFrame);
                    latest = nextFrame;
                }
            }
            if (clock >= 3000)
                readings.Add(track.Rate(latest));
        }
        Assert.All(readings, r => Assert.InRange(r, Math.Floor(fps) - 1, Math.Ceiling(fps) + 1));
    }

    [Fact]
    public void Rate_falls_to_zero_when_frames_stop()
    {
        var track = new Sensors.FrameTrack();
        for (double t = 0; t <= 2000; t += 10)
            track.Add(t);
        Assert.InRange(track.Rate(2000), 99, 101);
        Assert.InRange(track.Rate(2600), 1, 50);   // la app se pausó: va bajando
        Assert.Equal(0, track.Rate(3100));
    }
}
