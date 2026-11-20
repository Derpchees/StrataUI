using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace Strata.Shell.Services.Files;

/// <summary>Una carpeta compartida en la red: su nombre (\\equipo\nombre) y su ruta.</summary>
public sealed record NetShare(string Name, string Path, bool ReadOnly);

/// <summary>Un permiso del recurso compartido: a quién (SID) y qué se le permite o niega.</summary>
public sealed class ShareAce
{
    public string Sid { get; set; } = "";
    public int Allow { get; set; }
    public int Deny { get; set; }
}

/// <summary>
/// Lo que se puede cambiar de un recurso en el uso compartido avanzado (como el de Windows): quitarlo
/// o crearlo, el comentario, cuántos se conectan a la vez (-1 = sin límite), los archivos sin conexión
/// (<see cref="NetShares.CacheManual"/>…) y los permisos (null = no se tocan).
/// </summary>
public sealed class ShareEdit
{
    public string Name { get; set; } = "";
    public bool Remove { get; set; }
    public bool IsNew { get; set; }
    public string Remark { get; set; } = "";
    public int MaxUses { get; set; } = -1;
    public int Cache { get; set; }
    public List<ShareAce>? Aces { get; set; }
}

/// <summary>
/// Compartir carpetas en la red (la pestaña Compartir de Propiedades). Leer cuáles hay no pide
/// permiso (se lee del registro del servicio Servidor); crear o quitar uno sí, y lo hace el proceso
/// elevado de <see cref="ElevatedFiles"/> (ops "share" y "unshare").
/// </summary>
public static class NetShares
{
    private const string SharesKey = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares";
    private const string SecurityKey = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares\Security";

    /// <summary>Los permisos de recurso de Windows: Lectura, Cambiar y Control total (cada uno incluye al anterior).</summary>
    public const int ReadMask = 0x1200a9, ChangeMask = 0x1301bf, FullMask = 0x1f01ff;

    /// <summary>Archivos sin conexión (CSCFlags): los que elija el usuario, todos, todos optimizado o ninguno.</summary>
    public const int CacheManual = 0x00, CacheAuto = 0x10, CacheOptimized = 0x20, CacheNone = 0x30;
    private const int CacheBits = 0x30;

    /// <summary>Todos (S-1-1-0).</summary>
    public const string EveryoneSid = "S-1-1-0";

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
            var descriptor = new RawSecurityDescriptor(bytes, 0);
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

    /// <summary>
    /// El recurso tal como está (sin permiso de administrador: del registro del servicio Servidor). Los
    /// permisos quedan en null si no se pudieron leer.
    /// </summary>
    public static ShareEdit Details(string name)
    {
        var edit = new ShareEdit { Name = name };
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SharesKey);
            if (key?.GetValue(name) is string[] fields)
                foreach (string field in fields)
                {
                    int equals = field.IndexOf('=');
                    if (equals <= 0)
                        continue;
                    string value = field[(equals + 1)..];
                    switch (field[..equals].ToLowerInvariant())
                    {
                        case "remark":
                            edit.Remark = value;
                            break;
                        case "maxuses" when uint.TryParse(value, out uint uses):
                            edit.MaxUses = uses is 0 or > 16_777_216 ? -1 : (int)uses;
                            break;
                        case "cscflags" when int.TryParse(value, out int flags):
                            edit.Cache = flags & CacheBits;
                            break;
                    }
                }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("Archivos: no se pudo leer el recurso compartido", ex);
        }
        edit.Aces = ReadAces(name);
        return edit;
    }

    private static List<ShareAce>? ReadAces(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SecurityKey);
            if (key?.GetValue(name) is not byte[] bytes)
                return [new ShareAce { Sid = EveryoneSid, Allow = ReadMask }];
            var descriptor = new RawSecurityDescriptor(bytes, 0);
            if (descriptor.DiscretionaryAcl is null)
                return [new ShareAce { Sid = EveryoneSid, Allow = FullMask }];
            var aces = new List<ShareAce>();
            foreach (var ace in descriptor.DiscretionaryAcl.OfType<CommonAce>())
            {
                string sid = ace.SecurityIdentifier.Value;
                var entry = aces.FirstOrDefault(a => a.Sid == sid);
                if (entry is null)
                    aces.Add(entry = new ShareAce { Sid = sid });
                if (ace.AceQualifier == AceQualifier.AccessAllowed)
                    entry.Allow |= ace.AccessMask;
                else if (ace.AceQualifier == AceQualifier.AccessDenied)
                    entry.Deny |= ace.AccessMask;
            }
            return aces;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Los cambios se pueden guardar: nombres, comentario, límite, sin conexión y permisos válidos.</summary>
    internal static bool ValidEdits(List<ShareEdit>? edits) =>
        edits is { Count: > 0 and <= 32 } && edits.All(e => ValidName(e.Name) && e.Remark.Length <= 256
            && (e.MaxUses == -1 || e.MaxUses is > 0 and <= 16_777_216)
            && e.Cache is CacheManual or CacheAuto or CacheOptimized or CacheNone
            && (e.Aces is null || (e.Aces.Count <= 64 && e.Aces.All(a => (a.Allow & ~FullMask) == 0 && (a.Deny & ~FullMask) == 0 && IsSid(a.Sid)))));

    private static bool IsSid(string sid)
    {
        try
        {
            _ = new SecurityIdentifier(sid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
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

    /// <summary>Ya hay un recurso con ese nombre en este equipo (de cualquier carpeta).</summary>
    public static bool Exists(string name) => Existing().Contains(name);

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
    internal static string? Add(string folder, string name, bool readOnly) =>
        AddCore(folder, name, "", -1, [new ShareAce { Sid = EveryoneSid, Allow = readOnly ? ReadMask : ChangeMask }]);

    private static string? AddCore(string folder, string name, string remark, int maxUses, List<ShareAce> aces)
    {
        var pinned = GCHandle.Alloc(Descriptor(aces), GCHandleType.Pinned);
        try
        {
            var info = new ShareInfo502
            {
                NetName = name,
                Type = 0, // STYPE_DISKTREE
                Remark = remark,
                Permissions = 0,
                MaxUses = maxUses,
                CurrentUses = 0,
                Path = folder,
                Password = null,
                Reserved = 0,
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
            };
            return Check(NetShareAdd(null, 502, ref info, out _));
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>Un descriptor con los permisos (primero los que niegan, como los ordena Windows).</summary>
    private static byte[] Descriptor(List<ShareAce> aces)
    {
        var acl = new RawAcl(GenericAcl.AclRevision, aces.Count * 2);
        int index = 0;
        foreach (var ace in aces.Where(a => a.Deny != 0))
            acl.InsertAce(index++, new CommonAce(AceFlags.None, AceQualifier.AccessDenied, ace.Deny, new SecurityIdentifier(ace.Sid), false, null));
        foreach (var ace in aces.Where(a => a.Allow != 0))
            acl.InsertAce(index++, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, ace.Allow, new SecurityIdentifier(ace.Sid), false, null));
        var descriptor = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative, null, null, null, acl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }

    /// <summary>
    /// Guarda el uso compartido avanzado de la carpeta (proceso elevado): quita, crea y cambia los
    /// recursos, solo los de esa carpeta. Null si todo salió bien; si no, los errores.
    /// </summary>
    internal static string? Save(string folder, List<ShareEdit> edits)
    {
        var errors = new List<string>();
        foreach (var edit in edits)
            if ((edit.Remove ? Remove(folder, edit.Name) : Apply(folder, edit)) is { } error)
                errors.Add($"{edit.Name}: {error}");
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    private static string? Apply(string folder, ShareEdit edit)
    {
        if (edit.IsNew)
        {
            if (AddCore(folder, edit.Name, edit.Remark, edit.MaxUses, edit.Aces ?? [new ShareAce { Sid = EveryoneSid, Allow = ReadMask }]) is { } added)
                return added;
        }
        else
        {
            if (!For(folder).Any(s => string.Equals(s.Name, edit.Name, StringComparison.OrdinalIgnoreCase)))
                return Localization.Loc.T("El recurso no es de esa carpeta.");
            var remark = new ShareInfo1004 { Remark = edit.Remark };
            if (Check(NetShareSetInfo(null, edit.Name, 1004, ref remark, out _)) is { } remarkError)
                return remarkError;
            var uses = new ShareInfo1006 { MaxUses = edit.MaxUses };
            if (Check(NetShareSetInfo(null, edit.Name, 1006, ref uses, out _)) is { } usesError)
                return usesError;
            if (edit.Aces != null)
            {
                var pinned = GCHandle.Alloc(Descriptor(edit.Aces), GCHandleType.Pinned);
                try
                {
                    var security = new ShareInfo1501 { Reserved = 0, SecurityDescriptor = pinned.AddrOfPinnedObject() };
                    if (Check(NetShareSetInfo(null, edit.Name, 1501, ref security, out _)) is { } securityError)
                        return securityError;
                }
                finally
                {
                    pinned.Free();
                }
            }
        }
        // Sin conexión: se conservan las demás marcas del recurso (enumeración según acceso, etc.).
        int flags = 0;
        if (NetShareGetInfo(null, edit.Name, 1005, out IntPtr buffer) == 0)
        {
            flags = Marshal.ReadInt32(buffer);
            NetApiBufferFree(buffer);
        }
        var cache = new ShareInfo1005 { Flags = (flags & ~CacheBits) | (edit.Cache & CacheBits) };
        return Check(NetShareSetInfo(null, edit.Name, 1005, ref cache, out _));
    }

    private static string? Check(int result) => result == 0 ? null : new System.ComponentModel.Win32Exception(result).Message;

    /// <summary>Deja de compartir (proceso elevado), solo si el recurso es de esa carpeta.</summary>
    internal static string? Remove(string folder, string name)
    {
        if (!For(folder).Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Localization.Loc.T("El recurso no es de esa carpeta.");
        return Check(NetShareDel(null, name, 0));
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo1004
    {
        public string Remark;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1005
    {
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1006
    {
        public int MaxUses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1501
    {
        public int Reserved;
        public IntPtr SecurityDescriptor;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareSetInfo(string? server, string name, int level, ref ShareInfo1004 buffer, out int error);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareSetInfo(string? server, string name, int level, ref ShareInfo1005 buffer, out int error);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareSetInfo(string? server, string name, int level, ref ShareInfo1006 buffer, out int error);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareSetInfo(string? server, string name, int level, ref ShareInfo1501 buffer, out int error);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareGetInfo(string? server, string name, int level, out IntPtr buffer);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareDel(string? server, string name, int reserved);

}
