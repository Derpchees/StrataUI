using System.IO;

namespace Strata.Shell.Services;

/// <summary>Registro sencillo en %LOCALAPPDATA%\StrataUI\logs\strataui.log.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StrataUI", "logs", "strataui.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    /// <summary>Al pasar de este tamaño, el registro pasa a strataui.old.log y se empieza otro.</summary>
    private const long MaxBytes = 5 * 1024 * 1024;

    private static int _writes;

    // Sin excepción ante texto inválido (un emoji partido en el tooltip de un ícono de la bandeja
    // tumbaba la escritura): el carácter suelto se cambia por el de reemplazo.
    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Se revisa al primer mensaje y luego cada tanto (un error repetido llegó a 165 MB).
                if (_writes++ % 200 == 0)
                {
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Utf8);
            }
        }
        catch (IOException)
        {
            // El registro nunca debe tumbar el shell.
        }
    }
}
