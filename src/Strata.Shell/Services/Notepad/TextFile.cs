using System.IO;
using System.Text;

namespace Strata.Shell.Services.Notepad;

public enum TextEncodingKind
{
    Utf8,
    Utf8Bom,
    Utf16Le,
    Utf16Be,
    /// <summary>La página de códigos del sistema (Windows-1252 en español): archivos viejos sin UTF-8.</summary>
    Ansi,
}

public enum LineEnding
{
    CrLf,
    Lf,
    Cr,
}

/// <summary>Un archivo de texto leído: el texto (con saltos CRLF, como los escribe el editor) y cómo estaba guardado.</summary>
public sealed record TextFileContent(string Text, TextEncodingKind Encoding, LineEnding LineEnding);

/// <summary>
/// Lee y guarda archivos de texto para el Bloc de notas: reconoce la codificación (marca BOM, UTF-8
/// válido o la página de códigos del sistema) y los saltos de línea, y guarda con los mismos.
/// </summary>
public static class TextFile
{
    /// <summary>Más grande, el cuadro de texto se vuelve muy lento: se ofrece otro programa.</summary>
    public const long MaxSize = 48L * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Extensiones que se abren en el Bloc de notas (las de texto puro, no documentos ni scripts que se ejecutan).</summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".txt", ".log", ".ini", ".cfg", ".conf", ".md", ".markdown", ".csv", ".tsv", ".json", ".jsonc", ".xml",
        ".yml", ".yaml", ".toml", ".nfo", ".diz", ".srt", ".vtt", ".lrc", ".gitignore", ".gitattributes",
        ".editorconfig", ".env", ".properties", ".reg", ".inf",
    ];

    public static bool IsTextFile(string path)
    {
        string ext = Path.GetExtension(path);
        // .gitignore y compañía no tienen nombre: la "extensión" es todo el nombre.
        if (ext.Length == 0)
            return false;
        foreach (string known in Extensions)
            if (string.Equals(ext, known, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static Encoding? _ansi;

    /// <summary>La página de códigos ANSI de Windows (1252 en español e inglés).</summary>
    public static Encoding Ansi
    {
        get
        {
            if (_ansi is null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                int codePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                try
                {
                    _ansi = Encoding.GetEncoding(codePage);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    _ansi = Encoding.Latin1;
                }
            }
            return _ansi;
        }
    }

    public static TextFileContent Read(string path) => Decode(File.ReadAllBytes(path));

    public static TextFileContent Decode(ReadOnlySpan<byte> bytes)
    {
        TextEncodingKind kind;
        string text;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            kind = TextEncodingKind.Utf8Bom;
            text = Encoding.UTF8.GetString(bytes[3..]);
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            kind = TextEncodingKind.Utf16Le;
            text = Encoding.Unicode.GetString(bytes[2..]);
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            kind = TextEncodingKind.Utf16Be;
            text = Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }
        else if (LooksLikeUtf16Le(bytes))
        {
            kind = TextEncodingKind.Utf16Le;
            text = Encoding.Unicode.GetString(bytes);
        }
        else
        {
            try
            {
                text = StrictUtf8.GetString(bytes);
                kind = TextEncodingKind.Utf8;
            }
            catch (DecoderFallbackException)
            {
                text = Ansi.GetString(bytes);
                kind = TextEncodingKind.Ansi;
            }
        }
        var ending = DetectLineEnding(text);
        return new TextFileContent(NormalizeLineEndings(text), kind, ending);
    }

    /// <summary>UTF-16 sin marca (lo que deja reg export o algunos registros): texto ASCII con un cero cada dos bytes.</summary>
    private static bool LooksLikeUtf16Le(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;
        int sample = Math.Min(bytes.Length, 512), zeros = 0;
        for (int i = 1; i < sample; i += 2)
            if (bytes[i] == 0 && bytes[i - 1] != 0)
                zeros++;
        return zeros * 2 >= sample * 9 / 10;
    }

    /// <summary>El salto más usado; sin saltos, el de Windows.</summary>
    public static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                    cr++;
            }
            else if (text[i] == '\n')
                lf++;
        }
        return lf > crlf && lf >= cr ? LineEnding.Lf : cr > crlf && cr > lf ? LineEnding.Cr : LineEnding.CrLf;
    }

    /// <summary>Todos los saltos como CRLF (lo que inserta Enter en el cuadro de texto).</summary>
    public static string NormalizeLineEndings(string text)
    {
        if (text.IndexOfAny(['\r', '\n']) < 0)
            return text;
        var sb = new StringBuilder(text.Length + text.Length / 32);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append("\r\n");
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
            }
            else if (c == '\n')
                sb.Append("\r\n");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    public static string ApplyLineEnding(string text, LineEnding ending)
    {
        string normalized = NormalizeLineEndings(text);
        return ending switch
        {
            LineEnding.Lf => normalized.Replace("\r\n", "\n"),
            LineEnding.Cr => normalized.Replace("\r\n", "\r"),
            _ => normalized,
        };
    }

    public static byte[] Encode(string text, TextEncodingKind kind, LineEnding ending)
    {
        string content = ApplyLineEnding(text, ending);
        return kind switch
        {
            TextEncodingKind.Utf8Bom => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)],
            TextEncodingKind.Utf16Le => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(content)],
            TextEncodingKind.Utf16Be => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(content)],
            TextEncodingKind.Ansi => Ansi.GetBytes(content),
            _ => new UTF8Encoding(false).GetBytes(content),
        };
    }

    /// <summary>Lo que se perdería al guardar en ANSI (caracteres que esa página de códigos no tiene).</summary>
    public static bool FitsAnsi(string text)
    {
        var strict = Encoding.GetEncoding(Ansi.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            strict.GetBytes(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Guarda sin dejar el archivo a medias: escribe al lado y luego reemplaza (si eso no se puede,
    /// p. ej. en una carpeta de red, escribe directo). Conserva los atributos del original.
    /// </summary>
    public static void Write(string path, string text, TextEncodingKind kind, LineEnding ending)
    {
        byte[] bytes = Encode(text, kind, ending);
        string temp = path + ".strata-tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path))
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            TryDelete(temp);
            // Sin permiso para crear al lado (o el reemplazo no se puede en esa unidad): directo.
            if (ex is UnauthorizedAccessException && !File.Exists(path))
                throw;
            File.WriteAllBytes(path, bytes);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Parece binario: tiene bytes nulos (y no es UTF-16).</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || LooksLikeUtf16Le(bytes))
            return false;
        return bytes[..Math.Min(bytes.Length, 8000)].IndexOf((byte)0) >= 0;
    }
}
