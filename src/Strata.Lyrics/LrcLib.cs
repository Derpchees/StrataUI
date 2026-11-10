using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Strata.Shell.Services;

namespace Strata.Shell.Lyrics;

/// <summary>Una línea de una letra sincronizada: cuándo empieza y qué dice.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>La letra de una canción: sincronizada (con tiempos), solo el texto, o instrumental.</summary>
public sealed record LyricsResult(IReadOnlyList<LyricLine> Synced, string Plain, bool Instrumental)
{
    public bool IsEmpty => Synced.Count == 0 && Plain.Length == 0 && !Instrumental;
}

/// <summary>
/// Busca letras en LRCLIB (lrclib.net: gratis, sin cuenta ni clave, con letras sincronizadas) y las
/// guarda en %LOCALAPPDATA%\StrataUI\Lyrics para no volver a pedirlas. Si no hay letra se recuerda
/// por una semana.
/// </summary>
internal static partial class LrcLib
{
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"StrataUI/{App.Version}");
        return client;
    });

    private static string CacheFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "Lyrics");

    public static async Task<LyricsResult?> FindAsync(string title, string artist, string album, TimeSpan duration, bool fresh, CancellationToken cancel)
    {
        var candidates = Candidates(title, artist).ToList();
        if (candidates.Count == 0)
            return null;
        string cache = Path.Combine(CacheFolder, Hash($"{candidates[0].Artist}\n{candidates[0].Title}".ToLowerInvariant()) + ".json");
        if (!fresh && ReadCache(cache) is { } cached)
            return cached.IsEmpty ? null : cached;

        LyricsResult? found = null;
        foreach (var (a, t) in candidates)
        {
            found = await FetchAsync(t, a, album, duration, cancel);
            if (found != null)
                break;
        }
        WriteCache(cache, found);
        return found;
    }

    /// <summary>
    /// Qué buscar. Los sitios de video suelen anunciar «Artista - Canción (Official Video)» como
    /// título y el canal como artista: se prueba también así, sin lo que va entre paréntesis.
    /// </summary>
    private static IEnumerable<(string Artist, string Title)> Candidates(string title, string artist)
    {
        string cleanTitle = Clean(title);
        string cleanArtist = TopicSuffix().Replace(artist, "").Trim();
        if (cleanTitle.Length == 0)
            yield break;
        if (cleanArtist.Length > 0)
            yield return (cleanArtist, cleanTitle);
        int dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && dash < cleanTitle.Length - 3)
            yield return (cleanTitle[..dash].Trim(), cleanTitle[(dash + 3)..].Trim());
        else if (cleanArtist.Length == 0)
            yield return ("", cleanTitle);
    }

    private static string Clean(string title)
    {
        title = Decorations().Replace(title, " ");
        title = Featuring().Replace(title, " ");
        return Spaces().Replace(title, " ").Trim();
    }

    private static async Task<LyricsResult?> FetchAsync(string title, string artist, string album, TimeSpan duration, CancellationToken cancel)
    {
        // Búsqueda exacta (necesita la duración, con ±2 s de margen); si no, la búsqueda libre.
        if (artist.Length > 0 && duration > TimeSpan.Zero)
        {
            string url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}"
                         + (album.Length > 0 ? $"&album_name={Uri.EscapeDataString(album)}" : "")
                         + $"&duration={(int)Math.Round(duration.TotalSeconds)}";
            using var response = await Http.Value.GetAsync(url, cancel);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                if (Parse(doc.RootElement) is { IsEmpty: false } exact)
                    return exact;
            }
            else if (response.StatusCode != HttpStatusCode.NotFound)
                Log.Info($"Letras: LRCLIB respondió {(int)response.StatusCode}");
        }

        string search = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}"
                        + (artist.Length > 0 ? $"&artist_name={Uri.EscapeDataString(artist)}" : "");
        using var found = await Http.Value.GetAsync(search, cancel);
        if (!found.IsSuccessStatusCode)
            return null;
        using var list = JsonDocument.Parse(await found.Content.ReadAsStringAsync(cancel));
        if (list.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        // La que tenga letra sincronizada y una duración parecida; si no, cualquiera con letra.
        LyricsResult? best = null;
        double bestScore = double.MaxValue;
        foreach (var item in list.RootElement.EnumerateArray())
        {
            if (Parse(item) is not { IsEmpty: false } result)
                continue;
            double length = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
            bool known = duration > TimeSpan.Zero && length > 0;
            double score = (result.Synced.Count > 0 ? 0 : 1000) + (known ? Math.Abs(length - duration.TotalSeconds) : 500);
            if (known && Math.Abs(length - duration.TotalSeconds) > 15)
                score += 2000; // otra versión de la canción: solo si no hay nada mejor
            if (score < bestScore)
                (best, bestScore) = (result, score);
        }
        return best;
    }

    private static LyricsResult? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        bool instrumental = item.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True;
        string synced = item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "";
        string plain = item.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
        return new LyricsResult(ParseLrc(synced), plain.Trim(), instrumental);
    }

    /// <summary>Formato LRC: «[mm:ss.xx] texto», a veces con varios tiempos en la misma línea.</summary>
    internal static List<LyricLine> ParseLrc(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var stamps = Stamp().Matches(raw);
            if (stamps.Count == 0)
                continue;
            string text = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match m in stamps)
            {
                double seconds = int.Parse(m.Groups[1].Value) * 60
                                 + double.Parse(m.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromSeconds(seconds), text));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private sealed record CacheEntry(DateTime Saved, string Synced, string Plain, bool Instrumental);

    private static LyricsResult? ReadCache(string file)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file));
            if (entry is null)
                return null;
            var result = new LyricsResult(ParseLrc(entry.Synced), entry.Plain, entry.Instrumental);
            if (result.IsEmpty && DateTime.UtcNow - entry.Saved > RetryMissingAfter)
                return null;
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteCache(string file, LyricsResult? result)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            string synced = result is null ? "" : string.Join("\n", result.Synced.Select(l =>
                $"[{(int)l.Time.TotalMinutes:00}:{l.Time.Seconds:00}.{l.Time.Milliseconds / 10:00}]{l.Text}"));
            File.WriteAllText(file, JsonSerializer.Serialize(new CacheEntry(DateTime.UtcNow, synced, result?.Plain ?? "", result?.Instrumental ?? false)));
        }
        catch (Exception ex)
        {
            Log.Info($"Letras: no se pudo guardar en caché ({ex.Message})");
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    [GeneratedRegex(@"\[(\d+):(\d+(?:[.,]\d+)?)\]")]
    private static partial Regex Stamp();

    // (Official Video), [Lyrics], (Audio), (Remastered 2011)… y lo demás entre paréntesis o corchetes.
    [GeneratedRegex(@"\s*[\(\[【][^\)\]】]*[\)\]】]")]
    private static partial Regex Decorations();

    [GeneratedRegex(@"\s+(?:ft\.?|feat\.?|featuring)\s+.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s*-\s*Topic$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
