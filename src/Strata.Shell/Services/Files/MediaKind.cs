using System.IO;

namespace Strata.Shell.Services.Files;

/// <summary>Qué abre el visor de Archivos (lo demás se abre con su app).</summary>
public enum MediaKind { None, Image, Video, Audio }

public static class MediaKinds
{
    // Imágenes: las decodifica WPF con los códecs de Windows (RAW, HEIF y AVIF con sus extensiones de
    // la Tienda); SVG y GIF animado los dibuja Previews. Video y audio: el reproductor de Windows
    // (FramePlayer), con los códecs de la Tienda. Un formato sin códec avisa en el visor y ofrece
    // abrirlo con su app. PDF y documentos de oficina se abren con su app (la que eligió el usuario).
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".ico",
        ".jxr", ".wdp", ".svg",
        // RAW de cámara (con la extensión RAW de la Tienda)
        ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".orf", ".rw2", ".raf", ".pef", ".srw", ".erf",
        ".kdc", ".3fr", ".mef", ".mos", ".x3f",
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".3gp", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts",
        ".ogv", ".flv", ".hevc",
    };

    private static readonly HashSet<string> Sounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".oga", ".opus", ".aiff", ".aif", ".amr", ".ac3",
        ".m4b", ".weba",
    };

    public static MediaKind Of(string path)
    {
        string extension = Path.GetExtension(path);
        return Images.Contains(extension) ? MediaKind.Image
            : Videos.Contains(extension) ? MediaKind.Video
            : Sounds.Contains(extension) ? MediaKind.Audio
            : MediaKind.None;
    }
}
