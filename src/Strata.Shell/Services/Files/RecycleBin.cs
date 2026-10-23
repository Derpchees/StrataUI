using System.IO;
using System.Security.Principal;
using System.Text;

namespace Strata.Shell.Services.Files;

/// <summary>Algo que está en la Papelera: su nombre y lugar de antes, y dónde quedó guardado ($R…).</summary>
public sealed record RecycledItem(string OriginalPath, DateTime Deleted, long Size, bool IsFolder, string DataPath, string InfoPath)
{
    public string Name => Path.GetFileName(OriginalPath);
    public string OriginalFolder => Path.GetDirectoryName(OriginalPath) ?? OriginalPath;
}

/// <summary>
/// La Papelera leída directamente de <c>X:\$Recycle.Bin\&lt;SID&gt;</c> de cada unidad (sin el Shell):
/// por cada elemento, un <c>$I…</c> con la ruta original, el tamaño y la fecha, y un <c>$R…</c>
/// (mismo sufijo) con el contenido. Restaurar es moverlo de vuelta; eliminar, borrar los dos.
/// </summary>
public static class RecycleBin
{
    /// <summary>Todo lo que hay en la Papelera del usuario, en todas las unidades.</summary>
    public static List<RecycledItem> Read()
    {
        var items = new List<RecycledItem>();
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null)
            return items;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                    continue;
                string folder = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (!Directory.Exists(folder))
                    continue;
                foreach (var info in Directory.EnumerateFiles(folder, "$I*"))
                {
                    string data = Path.Combine(folder, "$R" + Path.GetFileName(info)[2..]);
                    bool isFolder = Directory.Exists(data);
                    if ((isFolder || File.Exists(data)) && Parse(File.ReadAllBytes(info)) is { } parsed)
                        items.Add(new RecycledItem(parsed.Path, parsed.Deleted, parsed.Size, isFolder, data, info));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Una unidad sin permiso o que se desconectó: lo demás se muestra igual.
            }
        }
        return items;
    }

    /// <summary>
    /// El <c>$I</c>: versión (8 bytes), tamaño (8), fecha de borrado (FILETIME, 8) y la ruta: en la
    /// versión 2 (Windows 10 y 11) su largo (4) y el texto; en la 1, 260 caracteres fijos.
    /// </summary>
    internal static (string Path, DateTime Deleted, long Size)? Parse(byte[] data)
    {
        if (data.Length < 24)
            return null;
        long version = BitConverter.ToInt64(data, 0);
        long size = BitConverter.ToInt64(data, 8);
        var deleted = DateTime.FromFileTime(BitConverter.ToInt64(data, 16));
        string path;
        if (version == 2 && data.Length >= 28)
        {
            int chars = BitConverter.ToInt32(data, 24);
            if (chars <= 0 || 28 + chars * 2 > data.Length)
                return null;
            path = Encoding.Unicode.GetString(data, 28, chars * 2);
        }
        else if (version == 1 && data.Length >= 24 + 520)
        {
            path = Encoding.Unicode.GetString(data, 24, 520);
        }
        else
        {
            return null;
        }
        path = path.TrimEnd('\0');
        return path.Length > 0 ? (path, deleted, size) : null;
    }

    /// <summary>Lo devuelve a su carpeta (la crea si ya no existe); si ahí hay uno con ese nombre, "nombre (2)".</summary>
    public static string Restore(RecycledItem item)
    {
        Directory.CreateDirectory(item.OriginalFolder);
        string target = item.OriginalPath;
        string name = Path.GetFileNameWithoutExtension(item.OriginalPath), extension = item.IsFolder ? "" : Path.GetExtension(item.OriginalPath);
        if (item.IsFolder)
            name = Path.GetFileName(item.OriginalPath);
        for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(item.OriginalFolder, $"{name} ({i}){extension}");
        if (item.IsFolder)
            Directory.Move(item.DataPath, target);
        else
            File.Move(item.DataPath, target);
        File.Delete(item.InfoPath);
        return target;
    }

    /// <summary>Quita su ficha ($I) después de borrar su contenido ($R) para siempre.</summary>
    public static void Forget(RecycledItem item)
    {
        if (!File.Exists(item.DataPath) && !Directory.Exists(item.DataPath) && File.Exists(item.InfoPath))
            File.Delete(item.InfoPath);
    }
}
