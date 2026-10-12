using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Winx.Shell.Services.Notifications;

/// <summary>Lo que la API de notificaciones no da y sí está en el XML original del aviso.</summary>
/// <param name="Domain">Sitio que la envió desde el navegador (atribución o dirección de origen).</param>
/// <param name="Picture">Imagen del aviso (foto del remitente, logotipo del sitio): ruta local.</param>
/// <param name="CircleCrop">Windows la recorta en círculo (hint-crop="circle").</param>
public sealed record ToastDetails(string? Domain, string? Picture, bool CircleCrop);

/// <summary>
/// Lee el XML de una notificación de la base de datos de Windows
/// (%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db, SQLite). El Id de la tabla es el
/// mismo de <c>UserNotification.Id</c>. Ahí está lo que <c>GetTextElements</c> esconde: el dominio
/// que los navegadores ponen como atribución ("web.whatsapp.com") y la imagen del aviso. Se usa el
/// SQLite que trae Windows (winsqlite3.dll), en solo lectura.
/// </summary>
public static class NotificationDb
{
    private static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    private static bool _failed;

    /// <summary>XML de la notificación; null si no está o no se puede leer.</summary>
    public static string? Payload(uint id)
    {
        if (_failed || !System.IO.File.Exists(File))
            return null;
        IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(Encoding.UTF8.GetBytes(File + "\0"), out db, OpenReadOnly, IntPtr.Zero) != Ok)
                return null;
            sqlite3_busy_timeout(db, 200);
            if (sqlite3_prepare16_v2(db, "SELECT Payload FROM Notification WHERE Id = ?1 AND Type = 'toast'", -1,
                                     out statement, IntPtr.Zero) != Ok)
                return null;
            sqlite3_bind_int64(statement, 1, id);
            if (sqlite3_step(statement) != Row)
                return null;
            int length = sqlite3_column_bytes(statement, 0);
            IntPtr data = sqlite3_column_blob(statement, 0);
            if (length <= 0 || data == IntPtr.Zero)
                return null;
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _failed = true;
            Log.Info($"No se puede leer la base de notificaciones de Windows: {ex.Message}");
            return null;
        }
        finally
        {
            if (statement != IntPtr.Zero)
                sqlite3_finalize(statement);
            if (db != IntPtr.Zero)
                sqlite3_close(db);
        }
    }

    /// <summary>Datos útiles de una notificación (null si no se pudo leer su XML).</summary>
    public static ToastDetails? Details(uint id) => Payload(id) is { } xml ? Parse(xml) : null;

    // Chromium (Brave, Chrome, Edge) pone en "launch" el origen de la página: "…|https://web.whatsapp.com/|…".
    private static readonly Regex Origin = new(@"https?://([a-z0-9-]+(\.[a-z0-9-]+)+)", RegexOptions.IgnoreCase);

    internal static ToastDetails? Parse(string xml)
    {
        try
        {
            var toast = XDocument.Parse(xml).Root;
            if (toast is null)
                return null;
            string? domain = toast.Descendants("text")
                .Where(t => (string?)t.Attribute("placement") == "attribution")
                .Select(t => WebApps.WebSites.AsDomain(t.Value))
                .FirstOrDefault(d => d != null);
            if (domain is null && (string?)toast.Attribute("launch") is { } launch && Origin.Match(launch) is { Success: true } origin)
                domain = origin.Groups[1].Value.ToLowerInvariant();

            var logo = toast.Descendants("image").FirstOrDefault(i => (string?)i.Attribute("placement") == "appLogoOverride");
            string? picture = LocalPath((string?)logo?.Attribute("src"));
            bool circle = (string?)logo?.Attribute("hint-crop") == "circle";
            return new ToastDetails(domain, picture, circle);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Solo imágenes en disco ("C:\…" o "file:///C:/…"); las de internet o del paquete, no.</summary>
    internal static string? LocalPath(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return null;
        try
        {
            string path = src.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(src).LocalPath : src;
            return Path.IsPathFullyQualified(path) && System.IO.File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    private const int Ok = 0, Row = 100, OpenReadOnly = 0x1;

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_prepare16_v2(IntPtr db, [MarshalAs(UnmanagedType.LPWStr)] string sql, int bytes,
                                                   out IntPtr statement, IntPtr tail);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    [DllImport("winsqlite3.dll")]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll")]
    private static extern int sqlite3_close(IntPtr db);
}
