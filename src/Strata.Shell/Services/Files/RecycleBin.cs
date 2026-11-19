using System.IO;
using System.Security.Principal;
using System.Text;

namespace Strata.Shell.Services.Files;

/// <summary>Algo que está en la Papelera: su nombre y lugar de antes, y dónde quedó guardado ($R…).</summary>
public sealed record RecycledItem(string OriginalPath, DateTime Deleted, long Size, bool IsFolder, string DataPath, string InfoPath)
{
    public string Name => Path.GetFileName(OriginalPath);

    /// <summary>Un $R… que se quedó sin su $I (un borrado que no terminó): no se sabe de dónde salió, solo se puede eliminar.</summary>
    public bool Orphan => InfoPath.Length == 0;
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
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var info in Directory.EnumerateFiles(folder, "$I*"))
                {
                    string data = Path.Combine(folder, "$R" + Path.GetFileName(info)[2..]);
                    known.Add(data);
                    bool isFolder = Directory.Exists(data);
                    if (!isFolder && !File.Exists(data))
                    {
                        ForgetStale(info);
                        continue;
                    }
                    if (Parse(File.ReadAllBytes(info)) is { } parsed)
                        items.Add(new RecycledItem(parsed.Path, parsed.Deleted, parsed.Size, isFolder, data, info));
                }
                // Los $R… sin ficha: se muestran con su nombre interno para poder eliminarlos (si no, ocupan sin verse).
                foreach (var data in Directory.EnumerateFileSystemEntries(folder, "$R*"))
                {
                    if (known.Contains(data))
                        continue;
                    bool isFolder = Directory.Exists(data);
                    var date = isFolder ? Directory.GetLastWriteTime(data) : File.GetLastWriteTime(data);
                    items.Add(new RecycledItem(data, date, isFolder ? -1 : new FileInfo(data).Length, isFolder, data, ""));
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
        if (item.Orphan)
            throw new IOException("No se sabe de dónde salió: solo se puede eliminar");
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

    /// <summary>
    /// Lo borra para siempre: su contenido ($R) archivo por archivo (sin el Shell de Windows, que con
    /// carpetas de miles de archivos fallaba a medias) y después su ficha ($I). Lo que no se pudo
    /// borrar (en uso, sin permiso) se salta y se devuelve; la ficha queda si quedó algo.
    /// </summary>
    public static List<string> DeleteForever(RecycledItem item)
    {
        var failed = new List<string>();
        // Si ya se está borrando (se pidió otra vez mientras tanto), dos borrados del mismo árbol
        // chocan entre sí y cada uno da por fallado lo que el otro tenía abierto.
        lock (Deleting)
            if (!Deleting.Add(item.DataPath))
                return failed;
        try
        {
            if (item.IsFolder)
                DeleteTree(item.DataPath, failed);
            else
                DeleteFile(item.DataPath, failed);
            // Lo que ya no está (se borró igual, por otro lado) no cuenta como fallado.
            failed.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
            bool gone = !File.Exists(item.DataPath) && !Directory.Exists(item.DataPath);
            if (gone && !item.Orphan)
                DeleteFile(item.InfoPath, failed);
            return failed;
        }
        finally
        {
            lock (Deleting)
                Deleting.Remove(item.DataPath);
        }
    }

    /// <summary>
    /// Una ficha ($I) cuyo contenido ya no está (un borrado que terminó a medias): no se ve en ninguna
    /// Papelera, así que se quita. Solo si tiene más de un minuto, por si Windows la está escribiendo.
    /// </summary>
    private static void ForgetStale(string info)
    {
        try
        {
            if (DateTime.Now - File.GetLastWriteTime(info) > TimeSpan.FromMinutes(1))
                File.Delete(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static readonly HashSet<string> Deleting =new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Si ya se está borrando (para no pedirlo dos veces).</summary>
    public static bool IsDeleting(RecycledItem item)
    {
        lock (Deleting)
            return Deleting.Contains(item.DataPath);
    }

    private static void DeleteTree(string folder, List<string> failed)
    {
        try
        {
            // Un enlace (junction, symlink) se quita sin entrar: lo de adentro no es de la Papelera.
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo)
                        DeleteTree(entry.FullName, failed);
                    else
                        DeleteFile(entry.FullName, failed);
                }
            }
            new DirectoryInfo(folder) { Attributes = FileAttributes.Directory }.Delete();
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add(folder);
        }
    }

    private static void DeleteFile(string path, List<string> failed)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                return;
            if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                file.Attributes &= ~FileAttributes.ReadOnly;
            file.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add(path);
        }
    }

    /// <summary>Avisa al Shell que cambió la Papelera (su ícono lleno/vacío en el escritorio).</summary>
    public static void NotifyChanged()
    {
        try
        {
            SHUpdateRecycleBinIcon();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHUpdateRecycleBinIcon();
}
