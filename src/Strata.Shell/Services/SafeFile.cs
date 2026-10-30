using System.IO;
using System.Text;

namespace Strata.Shell.Services;

/// <summary>
/// Archivos que no se pueden perder (la configuración, el respaldo de las optimizaciones): se
/// escriben hasta el disco antes de cambiar el anterior, que queda como copia en <c>.bak</c>.
/// Si el principal está dañado (un apagón justo al guardar lo dejaba vacío), se aparta y se lee la copia.
/// </summary>
internal static class SafeFile
{
    private static readonly object Gate = new();

    public static string BackupOf(string path) => path + ".bak";

    /// <summary>Escribe el texto (UTF-8 sin BOM). Lanza IOException o UnauthorizedAccessException si no se puede.</summary>
    public static void Write(string path, string text)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(new UTF8Encoding(false).GetBytes(text));
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(tmp, path, BackupOf(path), ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Lee y convierte el archivo con <paramref name="parse"/>; si falla, guarda el dañado aparte
    /// (<c>nombre.invalid-fecha.ext</c>) y prueba con la copia anterior. Default si no existe o no hay nada legible.
    /// </summary>
    public static T? Read<T>(string path, Func<string, T> parse, string what)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            return parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Error($"{what}: el archivo no se pudo leer", ex);
        }
        try
        {
            File.Copy(path, Path.ChangeExtension(path, $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        string backup = BackupOf(path);
        if (!File.Exists(backup))
            return default;
        try
        {
            var value = parse(File.ReadAllText(backup));
            Log.Info($"{what}: recuperado de la copia anterior ({Path.GetFileName(backup)})");
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Error($"{what}: la copia anterior tampoco se pudo leer", ex);
            return default;
        }
    }
}
