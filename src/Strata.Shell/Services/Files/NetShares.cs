using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Strata.Shell.Services.Files;

/// <summary>Una carpeta compartida en la red: su nombre (\\equipo\nombre) y su ruta.</summary>
public sealed record NetShare(string Name, string Path, bool ReadOnly);

/// <summary>
/// Compartir carpetas en la red (la pestaña Compartir de Propiedades). Leer cuáles hay no pide
/// permiso (se lee del registro del servicio Servidor); crear o quitar uno sí, y lo hace el proceso
/// elevado de <see cref="ElevatedFiles"/> (ops "share" y "unshare").
/// </summary>
public static class NetShares
{
    private const string SharesKey = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares";
    private const string SecurityKey = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares\Security";

    // Todos: lectura (0x1200a9) o cambio (0x1301bf), como "Lectura" y "Lectura y escritura" de Windows.
    private const string ReadSddl = "D:(A;;0x1200a9;;;WD)";
    private const string ChangeSddl = "D:(A;;0x1301bf;;;WD)";

    /// <summary>Los recursos que apuntan a esa carpeta (sin los administrativos, que terminan en $).</summary>
    public static List<NetShare> For(string folder)
    {
        var result = new List<NetShare>();
        string target = Normalize(folder);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SharesKey);
            if (key is null)
                return result;
            foreach (string name in key.GetValueNames())
            {
                if (name.EndsWith('$') || key.GetValue(name) is not string[] fields)
                    continue;
                string? path = fields.FirstOrDefault(f => f.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?[5..];
                if (path is null || !string.Equals(Normalize(path), target, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new NetShare(name, path, IsReadOnly(name)));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("Archivos: no se pudieron leer las carpetas compartidas", ex);
        }
        return result;
    }

    /// <summary>Si el permiso del recurso es solo lectura (lo que pone Strata UI o "Lectura" de Windows).</summary>
    private static bool IsReadOnly(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SecurityKey);
            if (key?.GetValue(name) is not byte[] bytes)
                return true; // sin descriptor propio, Windows da lectura a Todos
            var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
            const int write = 0x2; // FILE_WRITE_DATA
            return descriptor.DiscretionaryAcl is null
                ? false
                : !descriptor.DiscretionaryAcl.OfType<System.Security.AccessControl.CommonAce>()
                      .Any(ace => ace.AceType == System.Security.AccessControl.AceType.AccessAllowed && (ace.AccessMask & write) != 0);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>La ruta de red para un nombre de recurso: \\EQUIPO\nombre.</summary>
    public static string UncPath(string name) => $@"\\{Environment.MachineName}\{name}";

    /// <summary>Un nombre que Windows acepta para un recurso (no administrativo, sin caracteres prohibidos).</summary>
    public static bool ValidName(string? name) =>
        name is { Length: > 0 and <= 80 } && name.Trim() == name && !name.EndsWith('$')
        && name.IndexOfAny(['\\', '/', '[', ']', ':', '|', '<', '>', '+', ';', ',', '?', '*', '=', '"', '\0']) < 0;

    /// <summary>Un nombre libre para compartir esa carpeta (el de la carpeta, o "nombre 2"…).</summary>
    public static string SuggestName(string folder)
    {
        string baseName = new string(Path.GetFileName(folder.TrimEnd('\\')).Where(c => !"\\/[]:|<>+;,?*=\"".Contains(c)).ToArray()).Trim().TrimEnd('$');
        if (baseName.Length == 0)
            baseName = Localization.Loc.T("Compartida");
        if (baseName.Length > 70)
            baseName = baseName[..70];
        var used = Existing();
        string name = baseName;
        for (int i = 2; used.Contains(name); i++)
            name = $"{baseName} {i}";
        return name;
    }

    private static HashSet<string> Existing()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SharesKey);
            foreach (string name in key?.GetValueNames() ?? [])
                used.Add(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return used;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.TrimEnd('\\');
        }
    }

    // ---- Solo en el proceso elevado ----

    /// <summary>Comparte la carpeta (proceso elevado). Null si salió bien; si no, el error.</summary>
    internal static string? Add(string folder, string name, bool readOnly)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(readOnly ? ReadSddl : ChangeSddl, 1, out IntPtr descriptor, out _))
            return new System.ComponentModel.Win32Exception().Message;
        try
        {
            var info = new ShareInfo502
            {
                NetName = name,
                Type = 0, // STYPE_DISKTREE
                Remark = "",
                Permissions = 0,
                MaxUses = -1,
                CurrentUses = 0,
                Path = folder,
                Password = null,
                Reserved = 0,
                SecurityDescriptor = descriptor,
            };
            int result = NetShareAdd(null, 502, ref info, out _);
            return result == 0 ? null : new System.ComponentModel.Win32Exception(result).Message;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    /// <summary>Deja de compartir (proceso elevado), solo si el recurso es de esa carpeta.</summary>
    internal static string? Remove(string folder, string name)
    {
        if (!For(folder).Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Localization.Loc.T("El recurso no es de esa carpeta.");
        int result = NetShareDel(null, name, 0);
        return result == 0 ? null : new System.ComponentModel.Win32Exception(result).Message;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo502
    {
        public string NetName;
        public int Type;
        public string Remark;
        public int Permissions;
        public int MaxUses;
        public int CurrentUses;
        public string Path;
        public string? Password;
        public int Reserved;
        public IntPtr SecurityDescriptor;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareAdd(string? server, int level, ref ShareInfo502 buffer, out int error);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareDel(string? server, string name, int reserved);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int revision, out IntPtr descriptor, out int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
