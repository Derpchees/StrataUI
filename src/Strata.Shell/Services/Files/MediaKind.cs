using System.IO;

namespace Strata.Shell.Services.Files;

/// <summary>Qué abre el visor de Archivos (lo demás se abre con su app).</summary>
public enum MediaKind { None, Image, Video, Audio }

public static class MediaKinds
{
    // Lo que decodifican WPF (imágenes, con los códecs de Windows) y Media Foundation (video y audio).
    // Un formato sin códec instalado (webm sin la extensión de VP9, p. ej.) avisa en el visor y ofrece
    // abrirlo con su app.
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico",
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".3gp", ".mpg", ".mpeg", ".ts", ".m2ts",
    };

    private static readonly HashSet<string> Sounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".aiff", ".aif",
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
