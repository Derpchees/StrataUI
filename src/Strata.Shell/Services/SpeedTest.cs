using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services;

/// <summary>Resultado de una prueba de velocidad (se guarda el último).</summary>
public sealed record SpeedTestResult(double DownMbps, double UpMbps, double PingMs, double JitterMs, DateTime When, string? Server);

/// <summary>
/// Prueba de velocidad completa (ping, bajada y subida) contra los servidores de Cloudflare, los
/// mismos de speed.cloudflare.com: el más cercano responde solo. Varias conexiones a la vez, como
/// las pruebas de los navegadores, para llenar la línea; se descarta el primer momento (la conexión
/// todavía acelera). Sigue aunque se cierre el panel y avisa al terminar.
/// </summary>
public static class SpeedTest
{
    public enum Phase { Idle, Ping, Download, Upload, Done, Failed }

    private const string Host = "https://speed.cloudflare.com";
    private static readonly TimeSpan DownloadTime = TimeSpan.FromSeconds(8), UploadTime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(1.5);
    private const int DownloadStreams = 4, UploadStreams = 3;

    private static CancellationTokenSource? _cancel;

    public static Phase Current { get; private set; }
    public static bool Running => Current is Phase.Ping or Phase.Download or Phase.Upload;

    /// <summary>Lo que se va midiendo (Mbps o ms) y el avance de la prueba (0 a 1).</summary>
    public static double LiveValue { get; private set; }
    public static double Progress { get; private set; }

    /// <summary>Lo medido hasta ahora en esta prueba.</summary>
    public static double? PingMs { get; private set; }
    public static double? JitterMs { get; private set; }
    public static double? DownMbps { get; private set; }
    public static double? UpMbps { get; private set; }
    public static string? Server { get; private set; }

    /// <summary>Cambió algo (en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>Terminó una prueba (para avisar si no se está mirando).</summary>
    public static event Action<SpeedTestResult>? Finished;

    public static void Cancel() => _cancel?.Cancel();

    public static async void Start()
    {
        if (Running)
            return;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        PingMs = JitterMs = DownMbps = UpMbps = null;
        Server = null;
        Progress = LiveValue = 0;
        using var http = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = DownloadStreams + 1,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        }) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            Set(Phase.Ping);
            var (ping, jitter) = await MeasurePing(http, token);
            (PingMs, JitterMs) = (ping, jitter);
            Report();

            Set(Phase.Download);
            DownMbps = await Measure(DownloadTime, DownloadStreams, (counter, t) => DownloadLoop(http, counter, t), 0.1, 0.5, token);
            Report();

            Set(Phase.Upload);
            UpMbps = await Measure(UploadTime, UploadStreams, (counter, t) => UploadLoop(http, counter, t), 0.6, 0.4, token);

            var result = new SpeedTestResult(DownMbps ?? 0, UpMbps ?? 0, PingMs ?? 0, JitterMs ?? 0, DateTime.Now, Server);
            Settings.Update(s => s.LastSpeedTest = result);
            Progress = 1;
            Set(Phase.Done);
            Log.Info($"Prueba de velocidad: {result.DownMbps:0.0} Mbps de bajada, {result.UpMbps:0.0} de subida, {result.PingMs:0} ms ({Server})");
            Finished?.Invoke(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Set(Phase.Idle);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Log.Info($"La prueba de velocidad falló: {ex.Message}");
            Set(Phase.Failed);
        }
    }

    private static void Set(Phase phase)
    {
        Current = phase;
        LiveValue = 0;
        Report();
    }

    private static void Report() => Changed?.Invoke();

    /// <summary>Tiempo hasta la respuesta de pedidos vacíos (el primero abre la conexión y no cuenta).</summary>
    private static async Task<(double Ping, double Jitter)> MeasurePing(HttpClient http, CancellationToken token)
    {
        var samples = new List<double>();
        for (int i = 0; i < 11; i++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await http.GetAsync(Host + "/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, token);
            watch.Stop();
            response.EnsureSuccessStatusCode();
            // "cf-ray: 8a1b…-QRO": la ciudad del servidor que respondió.
            if (Server is null && response.Headers.TryGetValues("cf-ray", out var ray) && ray.FirstOrDefault() is { } id
                && id.LastIndexOf('-') is var dash and > 0)
                Server = id[(dash + 1)..];
            if (i == 0)
                continue;
            // Como speed.cloudflare.com: sin lo que tardó el servidor en atender ("Server-Timing: cfSpeedWorker;dur=22").
            double ms = watch.Elapsed.TotalMilliseconds - ServerTime(response);
            samples.Add(Math.Max(0, ms));
            LiveValue = Math.Max(0, ms);
            Progress = i / 10.0 * 0.1;
            Report();
        }
        samples.Sort();
        double ping = samples[samples.Count / 2];
        double jitter = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).DefaultIfEmpty(0).Average();
        return (ping, jitter);
    }

    internal static double ServerTime(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Server-Timing", out var values) ? ServerTime(string.Join(",", values)) : 0;

    /// <summary>Lo que el servidor dice que tardó (la suma de los "dur=": cfSpeedEdge, cfSpeedWorker…).</summary>
    internal static double ServerTime(string header) =>
        System.Text.RegularExpressions.Regex.Matches(header, @";\s*dur=([\d.]+)")
            .Sum(m => double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out double ms) ? ms : 0);

    /// <summary>
    /// Varias conexiones a la vez durante <paramref name="duration"/>; la velocidad es la de lo que
    /// pasó después del primer momento. Avisa la velocidad en vivo cada cuarto de segundo.
    /// </summary>
    private static async Task<double> Measure(TimeSpan duration, int streams, Func<long[], CancellationToken, Task> loop,
                                              double progressFrom, double progressSpan, CancellationToken token)
    {
        var counter = new long[1];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var watch = Stopwatch.StartNew();
        // Fuera del hilo de la UI: leer a cientos de Mbps no debe trabar la barra.
        var tasks = Enumerable.Range(0, streams).Select(_ => Task.Run(() => loop(counter, stop.Token))).ToList();
        long startBytes = 0;
        TimeSpan startTime = TimeSpan.Zero;
        long lastBytes = 0;
        TimeSpan lastTime = TimeSpan.Zero;
        while (watch.Elapsed < duration)
        {
            await Task.Delay(250, token);
            long bytes = Interlocked.Read(ref counter[0]);
            var now = watch.Elapsed;
            if (startTime == TimeSpan.Zero && now >= WarmUp)
                (startBytes, startTime) = (bytes, now);
            // En vivo: lo del último medio segundo, suavizado.
            double instant = (bytes - lastBytes) * 8 / 1e6 / Math.Max(0.05, (now - lastTime).TotalSeconds);
            LiveValue = LiveValue == 0 ? instant : LiveValue * 0.6 + instant * 0.4;
            (lastBytes, lastTime) = (bytes, now);
            Progress = progressFrom + progressSpan * Math.Min(1, now / duration);
            Report();
            if (tasks.All(t => t.IsCompleted))
                break;
        }
        stop.Cancel();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
        token.ThrowIfCancellationRequested();
        var faulted = tasks.FirstOrDefault(t => t.IsFaulted);
        long total = Interlocked.Read(ref counter[0]);
        double seconds = (watch.Elapsed - startTime).TotalSeconds;
        if (startTime == TimeSpan.Zero || seconds <= 0)
        {
            if (faulted?.Exception?.InnerException is { } error)
                throw error;
            seconds = watch.Elapsed.TotalSeconds;
            startBytes = 0;
        }
        return (total - startBytes) * 8 / 1e6 / seconds;
    }

    private static async Task DownloadLoop(HttpClient http, long[] counter, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var response = await http.GetAsync(Host + "/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                    Interlocked.Add(ref counter[0], read);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static readonly byte[] UploadChunk = CreateChunk();

    private static byte[] CreateChunk()
    {
        // Datos al azar: si fueran ceros, alguien en el camino podría comprimirlos.
        var chunk = new byte[256 * 1024];
        Random.Shared.NextBytes(chunk);
        return chunk;
    }

    private static async Task UploadLoop(HttpClient http, long[] counter, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var content = new CountingContent(25 * 1024 * 1024, counter, token);
                using var response = await http.PostAsync(Host + "/__up", content, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (HttpRequestException) when (token.IsCancellationRequested)
        {
        }
    }

    /// <summary>Cuerpo de la subida que cuenta lo que se va enviando.</summary>
    private sealed class CountingContent(long length, long[] counter, CancellationToken token) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            long sent = 0;
            while (sent < length && !token.IsCancellationRequested)
            {
                int size = (int)Math.Min(UploadChunk.Length, length - sent);
                await stream.WriteAsync(UploadChunk.AsMemory(0, size), token);
                sent += size;
                Interlocked.Add(ref counter[0], size);
            }
        }

        protected override bool TryComputeLength(out long size)
        {
            size = length;
            return true;
        }
    }

    /// <summary>"245 Mbps", "8.4 Mbps".</summary>
    public static string Mbps(double? value) => value is { } v
        ? v.ToString(v >= 100 ? "0" : "0.0", System.Globalization.CultureInfo.CurrentCulture) + " Mbps"
        : "— Mbps";

    public static string Ms(double? value) => value is { } v ? Math.Round(v).ToString() + " ms" : "— ms";

    /// <summary>Texto del aviso al terminar.</summary>
    public static string Summary(SpeedTestResult r) =>
        Loc.F("Bajada {0} · Subida {1} · Ping {2}", Mbps(r.DownMbps), Mbps(r.UpMbps), Ms(r.PingMs));
}
