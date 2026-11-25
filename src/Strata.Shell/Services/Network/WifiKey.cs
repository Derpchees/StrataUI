using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace Strata.Shell.Services.Network;

/// <summary>
/// La contraseña de la red Wi-Fi a la que está conectado el equipo (la que Windows guardó en su
/// perfil). Windows solo la entrega en claro a un administrador: primero se intenta sin permisos y,
/// si viene protegida, se abre <c>StrataUI.exe --wifi-key &lt;tubería&gt;</c> elevado (UAC). Ese
/// proceso no recibe nada: lee solo la clave de la conexión actual y la devuelve por una tubería con
/// nombre aleatorio (nunca por la línea de comandos ni a un archivo).
/// </summary>
public static class WifiKey
{
    public const string Arg = "--wifi-key";
    private const string PipePrefix = "StrataUI-wifikey-";

    /// <summary>Resultado: la clave (vacía en una red abierta) o por qué no se pudo.</summary>
    public sealed record Result(string? Key, bool Cancelled = false, bool NotConnected = false);

    /// <summary>La clave de la red actual; pide UAC solo si hace falta.</summary>
    public static async Task<Result> GetAsync()
    {
        var plain = await Task.Run(ReadCurrent);
        if (plain.Status == ReadStatus.NotConnected)
            return new Result(null, NotConnected: true);
        if (plain.Status == ReadStatus.Ok)
            return new Result(plain.Key);
        return await GetElevatedAsync();
    }

    private static async Task<Result> GetElevatedAsync()
    {
        string name = PipePrefix + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var process = Tweaks.Elevation.RunSelfAsync(Arg, name);
        using var stopWaiting = new CancellationTokenSource();
        var connect = pipe.WaitForConnectionAsync(stopWaiting.Token);
        if (await Task.WhenAny(connect, process) != connect)
        {
            stopWaiting.Cancel();
            try
            {
                await connect;
            }
            catch (OperationCanceledException)
            {
            }
            int early = await process;
            return new Result(null, Cancelled: early == Tweaks.Elevation.Cancelled);
        }
        using var reader = new StreamReader(pipe, new UTF8Encoding(false));
        string? line = await reader.ReadLineAsync();
        int code = await process;
        return line switch
        {
            ['K', '\t', .. var key] when code == 0 => new Result(key),
            "N" => new Result(null, NotConnected: true),
            _ => new Result(null),
        };
    }

    /// <summary>Punto de entrada de <c>--wifi-key &lt;tubería&gt;</c> (elevado).</summary>
    internal static int Run(string[] args)
    {
        if (args.Length != 2 || !args[1].StartsWith(PipePrefix, StringComparison.Ordinal) || args[1].Length != PipePrefix.Length + 32)
            return 2;
        try
        {
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out);
            pipe.Connect(15000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            var read = ReadCurrent();
            switch (read.Status)
            {
                case ReadStatus.Ok:
                    writer.WriteLine("K\t" + read.Key!.Replace('\r', ' ').Replace('\n', ' '));
                    return 0;
                case ReadStatus.NotConnected:
                    writer.WriteLine("N");
                    return 0;
                default:
                    writer.WriteLine("E");
                    return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Info($"Contraseña Wi-Fi (administrador): sin tubería ({ex.Message})");
            return 1;
        }
    }

    // ── API de Wi-Fi de Windows (wlanapi) ───────────────────────────────────

    internal enum ReadStatus { Ok, NotConnected, Protected, Failed }

    internal readonly record struct Read(ReadStatus Status, string? Key = null);

    /// <summary>Lee el perfil de la conexión actual pidiendo la clave en claro.</summary>
    private static Read ReadCurrent()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out IntPtr handle) != 0)
            return new Read(ReadStatus.Failed);
        IntPtr list = IntPtr.Zero;
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0)
                return new Read(ReadStatus.Failed);
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < count; i++)
            {
                // WLAN_INTERFACE_INFO: GUID (16) + descripción (256 WCHAR) + estado (4) = 532 bytes, tras 8 de cabecera.
                IntPtr item = list + 8 + i * 532;
                var guid = Marshal.PtrToStructure<Guid>(item);
                if (Marshal.ReadInt32(item + 16 + 512) != 1) // wlan_interface_state_connected
                    continue;
                if (WlanQueryInterface(handle, ref guid, 7, IntPtr.Zero, out _, out IntPtr attributes, out _) != 0)
                    continue;
                string profile;
                try
                {
                    // WLAN_CONNECTION_ATTRIBUTES: estado (4) + modo (4) + nombre del perfil (256 WCHAR).
                    profile = Marshal.PtrToStringUni(attributes + 8) ?? "";
                }
                finally
                {
                    WlanFreeMemory(attributes);
                }
                if (profile.Length == 0)
                    continue;
                uint flags = GetPlaintextKey;
                if (WlanGetProfile(handle, ref guid, profile, IntPtr.Zero, out IntPtr xml, ref flags, out _) != 0)
                    return new Read(ReadStatus.Failed);
                try
                {
                    return ParseProfile(Marshal.PtrToStringUni(xml) ?? "");
                }
                finally
                {
                    WlanFreeMemory(xml);
                }
            }
            return new Read(ReadStatus.NotConnected);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new Read(ReadStatus.Failed); // sin servicio de Wi-Fi (equipos sin adaptador)
        }
        finally
        {
            if (list != IntPtr.Zero)
                WlanFreeMemory(list);
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    /// <summary>La clave del XML del perfil: "" si la red es abierta, protegida si Windows no la dio en claro.</summary>
    internal static Read ParseProfile(string xml)
    {
        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            doc.LoadXml(xml);
        }
        catch (XmlException)
        {
            return new Read(ReadStatus.Failed);
        }
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");
        var shared = doc.SelectSingleNode("//w:sharedKey", ns);
        if (shared is null)
            return new Read(ReadStatus.Ok, ""); // abierta (o empresarial, con usuario y no con clave)
        if (shared.SelectSingleNode("w:protected", ns)?.InnerText.Trim() == "true")
            return new Read(ReadStatus.Protected);
        return shared.SelectSingleNode("w:keyMaterial", ns)?.InnerText is { } key
            ? new Read(ReadStatus.Ok, key)
            : new Read(ReadStatus.Protected);
    }

    private const uint GetPlaintextKey = 4; // WLAN_PROFILE_GET_PLAINTEXT_KEY

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opCode, IntPtr reserved,
                                                 out uint dataSize, out IntPtr data, out int valueType);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanGetProfile(IntPtr handle, ref Guid interfaceGuid, string profileName, IntPtr reserved,
                                             out IntPtr profileXml, ref uint flags, out uint grantedAccess);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
