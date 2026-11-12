using System.Runtime.InteropServices;
using System.Text;

namespace Strata.Shell.Services.Files.Remote;

/// <summary>
/// Las contraseñas de los servidores, en el Administrador de credenciales de Windows (cifradas con la
/// cuenta del usuario; nunca en settings.json). Se guardan como "StrataUI:ftp:{id del sitio}".
/// </summary>
public static class RemoteCredentials
{
    private const int Generic = 1, LocalMachine = 2;

    private static string Target(RemoteSite site) => "StrataUI:ftp:" + site.Id;

    public static string? Read(RemoteSite site)
    {
        if (!CredRead(Target(site), Generic, 0, out var pointer))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero ? ""
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>Guarda (o, con null o vacía, borra) la contraseña de un sitio.</summary>
    public static void Write(RemoteSite site, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            Delete(site);
            return;
        }
        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = Target(site),
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachine,
                UserName = site.User,
                Comment = "Strata UI · " + site.Host,
            };
            if (!CredWrite(ref credential, 0))
                Log.Error($"Archivos: no se pudo guardar la contraseña de {site.Host} (error {Marshal.GetLastWin32Error()})");
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
        }
    }

    public static void Delete(RemoteSite site) => CredDelete(Target(site), Generic, 0);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public int LastWrittenLow, LastWrittenHigh; // FILETIME
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
