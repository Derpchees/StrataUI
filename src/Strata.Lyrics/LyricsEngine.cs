using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Media;
using Media = Strata.Shell.Services.Media.MediaService;

namespace Strata.Shell.Lyrics;

public enum LyricsState { None, Searching, Found, NotFound, Failed }

/// <summary>
/// Sigue lo que suena en cualquier app (MediaService): busca la letra de cada canción nueva y, si
/// está sincronizada, marca la línea que se canta según el avance. Todo en el hilo de la UI.
/// </summary>
internal static class LyricsEngine
{
    /// <summary>Las letras sincronizadas suelen marcar el inicio justo: se adelanta un poco para leerla a tiempo.</summary>
    private static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(250);

    private static bool _started;
    private static bool _fresh;
    private static string _key = "";
    private static CancellationTokenSource? _search;
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public static LyricsState State { get; private set; }
    public static LyricsResult? Current { get; private set; }
    public static string Title { get; private set; } = "";
    public static string Artist { get; private set; } = "";

    /// <summary>Índice de la línea sincronizada que se canta, o -1.</summary>
    public static int LineIndex { get; private set; } = -1;

    /// <summary>Cambió la canción, el estado de la búsqueda o la letra.</summary>
    public static event Action? Changed;

    /// <summary>Cambió la línea que se canta.</summary>
    public static event Action? LineChanged;

    public static void Start()
    {
        if (_started)
            return;
        _started = true;
        Media.Changed += OnMediaChanged;
        Timer.Tick += (_, _) => Tick();
        OnMediaChanged();
    }

    /// <summary>Vuelve a buscar la canción actual (botón del panel), sin la caché de «no encontrada».</summary>
    public static void Retry()
    {
        _key = "";
        _fresh = true;
        OnMediaChanged();
    }

    private static void OnMediaChanged()
    {
        string key = Media.HasSession ? $"{Media.Title}\n{Media.Artist}" : "";
        if (key != _key)
        {
            _key = key;
            _search?.Cancel();
            _search = null;
            Current = null;
            SetLine(-1);
            Title = Media.Title;
            Artist = Media.Artist;
            if (key.Length == 0 || Media.Title.Length == 0)
                State = LyricsState.None;
            else
            {
                State = LyricsState.Searching;
                _search = new CancellationTokenSource();
                _ = SearchAsync(Media.Title, Media.Artist, Media.Album, _fresh, _search.Token);
                _fresh = false;
            }
            Changed?.Invoke();
        }
        UpdateTimer();
    }

    private static async Task SearchAsync(string title, string artist, string album, bool fresh, CancellationToken cancel)
    {
        try
        {
            // Las apps anuncian la duración un momento después que el título.
            await Task.Delay(700, cancel);
            var duration = Media.Timeline().Duration;
            var result = await LrcLib.FindAsync(title, artist, album, duration, fresh, cancel);
            if (cancel.IsCancellationRequested)
                return;
            Current = result;
            State = result != null ? LyricsState.Found : LyricsState.NotFound;
            Log.Info(result != null
                ? $"Letras: encontrada para «{title}» ({(result.Synced.Count > 0 ? "sincronizada" : result.Instrumental ? "instrumental" : "sin tiempos")})"
                : $"Letras: no hay letra para «{title}» de «{artist}» ({duration:m\\:ss})");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Current = null;
            State = LyricsState.Failed;
            Log.Info($"Letras: no se pudo buscar ({ex.Message})");
            _key = ""; // la próxima vez que cambie algo se reintenta
        }
        Changed?.Invoke();
        UpdateTimer();
        Tick();
    }

    private static void UpdateTimer()
    {
        // En pausa la línea no avanza: no hace falta mirar el avance cinco veces por segundo.
        bool synced = Current is { Synced.Count: > 0 } && Media.HasSession;
        bool run = synced && Media.IsPlaying;
        if (run && !Timer.IsEnabled)
            Timer.Start();
        else if (!run && Timer.IsEnabled)
            Timer.Stop();
        if (synced)
            Tick();
    }

    private static void Tick()
    {
        if (Current is not { Synced.Count: > 0 } lyrics)
        {
            SetLine(-1);
            return;
        }
        var position = Media.Timeline().Position + Lead;
        // Búsqueda binaria de la última línea que ya empezó.
        int lo = 0, hi = lyrics.Synced.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lyrics.Synced[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }
        SetLine(found);
    }

    private static void SetLine(int index)
    {
        if (index == LineIndex)
            return;
        LineIndex = index;
        LyricsLine.Set(index >= 0 && Current is { } lyrics ? lyrics.Synced[index].Text : "");
        LineChanged?.Invoke();
    }
}
