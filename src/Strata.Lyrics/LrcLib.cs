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

    /// <summary>
    /// Las canciones que no se encontraron antes de mejorar la búsqueda se vuelven a buscar en vez de
    /// esperar la semana de la caché.
    /// </summary>
    private static readonly DateTime SearchImproved = new(2026, 11, 11, 21, 30, 0, DateTimeKind.Utc);

    public static async Task<LyricsResult?> FindAsync(string title, string artist, string album, TimeSpan duration, bool fresh, CancellationToken cancel)
    {
        var candidates = Candidates(title, artist).ToList();
        if (candidates.Count == 0)
            return null;
        string cache = Path.Combine(CacheFolder, Hash($"{candidates[0].Artist}\n{candidates[0].Title}".ToLowerInvariant()) + ".json");
        if (!fresh && ReadCache(cache) is { } cached)
            return cached.IsEmpty ? null : cached;

        LyricsResult? found = null;
        // 1) Artista y canción, en cada forma posible (exacta con la duración y luego la búsqueda).
        foreach (var (a, t) in candidates)
        {
            found = await FetchAsync(t, a, album, duration, cancel);
            if (found != null)
                break;
        }
        // 2) Búsqueda libre («artista canción»): encuentra lo que está guardado con otro nombre de
        //    artista o del tema, pero solo vale si el título y el artista coinciden de verdad. Una sola
        //    vez, con la forma más completa: la búsqueda libre ya tolera las variantes del nombre.
        if (found == null && candidates.FirstOrDefault(c => c.Artist.Length > 0) is { Artist.Length: > 0 } free)
            found = Best(await SearchAsync($"q={Uri.EscapeDataString($"{free.Artist} {free.Title}")}", cancel),
                         free.Title, free.Artist, duration, sameRecording: false);
        // 3) Solo el título: muchas versiones se suben con otro artista (recopilaciones, covers con el
        //    nombre del autor…). Se acepta solo si dura lo mismo (±3 s): es la misma grabación.
        if (found == null && duration > TimeSpan.Zero)
        {
            string t = candidates[0].Title;
            var list = await SearchAsync($"track_name={Uri.EscapeDataString(t)}", cancel);
            found = Best(list, t, "", duration, sameRecording: true, tolerance: 3);
            // Otra grabación de la misma canción (casi igual de larga): el texto sirve, los tiempos no.
            if (found == null && Best(list, t, "", duration, sameRecording: true, tolerance: 10) is { Plain.Length: > 0 } cover)
                found = cover with { Synced = [] };
        }
        WriteCache(cache, found);
        return found;
    }

    /// <summary>
    /// Qué buscar. Los sitios de video suelen anunciar «Artista - Canción (Official Video)» como
    /// título y el canal como artista: se prueba también así, sin lo que va entre paréntesis. Spotify
    /// agrega « - Remastered 2011», « - En vivo»…: se quita. Con varios artistas, se prueba también
    /// solo con el primero.
    /// </summary>
    private static IEnumerable<(string Artist, string Title)> Candidates(string title, string artist)
    {
        string cleanTitle = Clean(title);
        string cleanArtist = TopicSuffix().Replace(artist, "").Trim();
        if (cleanTitle.Length == 0)
            yield break;
        var seen = new HashSet<string>();
        bool New(string a, string t) => a.Length + t.Length > 0 && t.Length > 0 && seen.Add(Normalize(a) + "\n" + Normalize(t));

        if (cleanArtist.Length > 0)
        {
            string bare = VersionSuffix().Replace(cleanTitle, "").Trim();
            string first = ArtistSeparator().Split(cleanArtist)[0].Trim();
            if (New(cleanArtist, bare)) yield return (cleanArtist, bare);
            if (New(first, bare)) yield return (first, bare);
            if (New(cleanArtist, cleanTitle)) yield return (cleanArtist, cleanTitle);
        }
        int dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && dash < cleanTitle.Length - 3)
        {
            string a = cleanTitle[..dash].Trim(), t = cleanTitle[(dash + 3)..].Trim();
            if (New(a, t)) yield return (a, t);
        }
        else if (cleanArtist.Length == 0 && New("", cleanTitle))
            yield return ("", cleanTitle);
    }

    private static string Clean(string title)
    {
        title = Decorations().Replace(title, " ");
        title = Featuring().Replace(title, " ");
        return Spaces().Replace(title, " ").Trim();
    }

    /// <summary>Para comparar nombres: sin acentos, mayúsculas, signos ni lo que va entre paréntesis.</summary>
    internal static string Normalize(string text)
    {
        text = Clean(text).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    private static bool SameTitle(string found, string wanted)
    {
        string a = Normalize(VersionSuffix().Replace(found, "")), b = Normalize(wanted);
        return a.Length > 0 && (a == b || (b.Length >= 4 && (a.StartsWith(b + " ", StringComparison.Ordinal) || b.StartsWith(a + " ", StringComparison.Ordinal))));
    }

    /// <summary>Algún artista en común (basta uno: «A, B» encuentra «A»).</summary>
    private static bool SameArtist(string found, string wanted)
    {
        var a = ArtistSeparator().Split(found).Select(Normalize).Where(x => x.Length > 0).ToList();
        var b = ArtistSeparator().Split(wanted).Select(Normalize).Where(x => x.Length > 0).ToList();
        return a.Any(x => b.Any(y => x == y || x.Contains(y) || y.Contains(x)));
    }

    private static async Task<JsonElement[]> SearchAsync(string query, CancellationToken cancel)
    {
        using var found = await Http.Value.GetAsync($"https://lrclib.net/api/search?{query}", cancel);
        if (!found.IsSuccessStatusCode)
        {
            if (found.StatusCode != HttpStatusCode.NotFound)
                Log.Info($"Letras: LRCLIB respondió {(int)found.StatusCode}");
            return [];
        }
        await using var body = await found.Content.ReadAsStreamAsync(cancel);
        using var list = await JsonDocument.ParseAsync(body, cancellationToken: cancel);
        return list.RootElement.ValueKind == JsonValueKind.Array
            ? list.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray()
            : [];
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
                await using var body = await response.Content.ReadAsStreamAsync(cancel);
                using var doc = await JsonDocument.ParseAsync(body, cancellationToken: cancel);
                if (Parse(doc.RootElement) is { IsEmpty: false } exact)
                    return exact;
            }
            else if (response.StatusCode != HttpStatusCode.NotFound)
                Log.Info($"Letras: LRCLIB respondió {(int)response.StatusCode}");
        }

        var list = await SearchAsync($"track_name={Uri.EscapeDataString(title)}"
                                     + (artist.Length > 0 ? $"&artist_name={Uri.EscapeDataString(artist)}" : ""), cancel);
        return Best(list, title, artist, duration, sameRecording: false);
    }

    /// <summary>
    /// De los resultados, el que sea esa canción (mismo título y, si se sabe, mismo artista), con letra
    /// sincronizada y una duración parecida; si no, cualquiera con letra. Con <paramref name="sameRecording"/>
    /// solo vale la misma duración (±3 s).
    /// </summary>
    private static LyricsResult? Best(IEnumerable<JsonElement> list, string title, string artist, TimeSpan duration, bool sameRecording, double tolerance = 3)
    {
        LyricsResult? best = null;
        double bestScore = double.MaxValue;
        foreach (var item in list)
        {
            if (Parse(item) is not { IsEmpty: false } result)
                continue;
            string track = item.TryGetProperty("trackName", out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString()! : "";
            string by = item.TryGetProperty("artistName", out var an) && an.ValueKind == JsonValueKind.String ? an.GetString()! : "";
            if (!SameTitle(track, title) || (artist.Length > 0 && !SameArtist(by, artist)))
                continue;
            double length = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
            bool known = duration > TimeSpan.Zero && length > 0;
            double off = known ? Math.Abs(length - duration.TotalSeconds) : 500;
            if (sameRecording && (!known || off > tolerance))
                continue;
            double score = (result.Synced.Count > 0 ? 0 : 1000) + off;
            if (known && off > 15)
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
                double seconds = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 60
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
            if (result.IsEmpty && (DateTime.UtcNow - entry.Saved > RetryMissingAfter || entry.Saved < SearchImproved))
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

    [GeneratedRegex(@"\s+-\s+(?=[^-]*\b(?:remaster(?:ed|izad[oa])?|live|en vivo|ao vivo|en directo|version|versi[oó]n|edit|mix|remix|mono|stereo|ac[uú]stic[oa]|acoustic|from|bonus|demo|single|instrumental)\b)[^-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s*(?:,|&|/|;|\s+x\s+|\s+feat\.?\s+|\s+ft\.?\s+|\s+and\s+)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparator();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
