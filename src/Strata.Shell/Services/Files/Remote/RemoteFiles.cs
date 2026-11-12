using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentFTP;
using FluentFTP.Exceptions;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files.Remote;

/// <summary>Una carpeta o archivo de un servidor guardado: el sitio y su ruta allá ("/public_html/img").</summary>
public readonly record struct RemoteLocation(RemoteSite Site, string Path);

/// <summary>El certificado que mostró un servidor y por qué no es de confianza.</summary>
public sealed record RemoteCertificate(string Subject, string Issuer, DateTime ValidFrom, DateTime ValidTo,
                                       string Thumbprint, SslPolicyErrors Errors);

/// <summary>El servidor mostró un certificado que no es de confianza: el usuario decide si lo acepta.</summary>
public sealed class RemoteCertificateException(RemoteSite site, RemoteCertificate certificate)
    : IOException(Loc.F("El certificado de {0} no es de confianza.", site.Host))
{
    public RemoteSite Site { get; } = site;
    public RemoteCertificate Certificate { get; } = certificate;
}

/// <summary>Hace falta la contraseña (se pide al conectar, o la guardada no sirvió).</summary>
public sealed class RemotePasswordException(RemoteSite site, bool rejected)
    : IOException(rejected ? Loc.T("El usuario o la contraseña no son correctos.") : Loc.T("Escribe la contraseña para conectarte."))
{
    public RemoteSite Site { get; } = site;
    /// <summary>El servidor rechazó la que había (si no, todavía no había ninguna).</summary>
    public bool Rejected { get; } = rejected;
}

/// <summary>
/// Archivos en servidores FTP y FTPS, con las rutas "ftp://Sitio/carpeta" que muestra el explorador
/// (el sitio por su nombre en el administrador de sitios). Cada sitio tiene una conexión para navegar
/// (de a una orden por vez); las transferencias abren la suya para no trabar la navegación.
/// </summary>
public static class RemoteFiles
{
    public const string Scheme = "ftp://";

    static RemoteFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static List<RemoteSite> Sites => Settings.Current.FilesRemoteSites;

    public static bool IsRemote(string? path) => path != null && path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static RemoteSite? SiteNamed(string name) =>
        Sites.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>El sitio y la ruta de allá de "ftp://Sitio/carpeta" (null si no es remota o el sitio ya no existe).</summary>
    public static RemoteLocation? Locate(string? path)
    {
        if (!IsRemote(path))
            return null;
        string rest = path![Scheme.Length..];
        int slash = rest.IndexOf('/');
        string name = slash < 0 ? rest : rest[..slash];
        return SiteNamed(name) is { } site ? new RemoteLocation(site, Clean(slash < 0 ? "/" : rest[slash..])) : null;
    }

    /// <summary>"/a//b/" → "/a/b"; siempre empieza con "/".</summary>
    private static string Clean(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', parts);
    }

    public static string PathOf(RemoteSite site, string remote) =>
        Scheme + site.Name + (Clean(remote) is "/" ? "" : Clean(remote));

    public static string RootOf(RemoteSite site) => PathOf(site, "/");

    /// <summary>Donde se abre el sitio: su carpeta inicial (o la raíz).</summary>
    public static string StartOf(RemoteSite site) => PathOf(site, site.InitialFolder);

    /// <summary>Una ruta escrita ("ftp://sitio\a/", con el nombre del sitio en otras mayúsculas) como la de siempre.</summary>
    public static string? Normalize(string path) => Locate(path.Trim().Trim('"')) is { } at ? PathOf(at.Site, at.Path) : null;

    /// <summary>La carpeta de arriba (null en la raíz del sitio).</summary>
    public static string? Parent(string path)
    {
        if (Locate(path) is not { } at || at.Path == "/")
            return null;
        int slash = at.Path.LastIndexOf('/');
        return PathOf(at.Site, slash <= 0 ? "/" : at.Path[..slash]);
    }

    public static string Combine(string folder, string name) => folder.TrimEnd('/') + "/" + name;

    /// <summary>Dos rutas del mismo servidor y la misma carpeta (las del servidor distinguen mayúsculas).</summary>
    public static bool SamePath(string? a, string? b) =>
        a != null && b != null && IsRemote(a) && IsRemote(b) && string.Equals(Normalize(a) ?? a, Normalize(b) ?? b, StringComparison.Ordinal);

    public static string NameOf(string path)
    {
        if (Locate(path) is not { } at)
            return path;
        return at.Path == "/" ? at.Site.Name : at.Path[(at.Path.LastIndexOf('/') + 1)..];
    }

    /// <summary>Las carpetas desde la raíz del sitio hasta esta (para la ruta de navegación).</summary>
    public static List<(string Name, string Path)> Crumbs(string path)
    {
        var crumbs = new List<(string, string)>();
        if (Locate(path) is not { } at)
            return crumbs;
        crumbs.Add((at.Site.Name, RootOf(at.Site)));
        string current = "";
        foreach (var part in at.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            crumbs.Add((part, PathOf(at.Site, current)));
        }
        return crumbs;
    }

    // ---- Contraseñas de esta sesión (las de "pedir al conectar") ----

    private static readonly Dictionary<string, string> _sessionPasswords = new();

    /// <summary>La contraseña que se escribió al conectar; con <paramref name="save"/>, además queda guardada.</summary>
    public static void SetPassword(RemoteSite site, string password, bool save)
    {
        lock (_sessionPasswords)
            _sessionPasswords[site.Id] = password;
        if (save)
            RemoteCredentials.Write(site, password);
        Forget(site);
    }

    private static string? PasswordOf(RemoteSite site)
    {
        lock (_sessionPasswords)
            if (_sessionPasswords.TryGetValue(site.Id, out var typed))
                return typed;
        return site.Logon == RemoteLogon.Normal ? RemoteCredentials.Read(site) : null;
    }

    /// <summary>Acepta para siempre el certificado que mostró el servidor (lo guarda con el sitio).</summary>
    public static void TrustCertificate(RemoteSite site, RemoteCertificate certificate)
    {
        Settings.Update(_ =>
        {
            if (Sites.FirstOrDefault(s => s.Id == site.Id) is { } saved)
                saved.TrustedCertificate = certificate.Thumbprint;
        });
        site.TrustedCertificate = certificate.Thumbprint;
        Forget(site);
    }

    // ---- Conexiones ----

    private sealed class Connection
    {
        public AsyncFtpClient? Client;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }

    private static readonly Dictionary<string, Connection> _connections = new();

    /// <summary>Cierra la conexión de un sitio (cambió su configuración o se borró).</summary>
    public static void Forget(RemoteSite site)
    {
        Connection? connection;
        lock (_connections)
            if (_connections.Remove(site.Id, out connection) && connection.Client is { } client)
                _ = Task.Run(async () =>
                {
                    try { await client.Disconnect(); } catch { }
                    client.Dispose();
                });
    }

    /// <summary>Un cliente nuevo, configurado como dice el sitio (todavía sin conectar).</summary>
    private static AsyncFtpClient CreateClient(RemoteSite site, string? password, Action<RemoteCertificate> rejected)
    {
        bool anonymous = site.Logon == RemoteLogon.Anonymous;
        if (!anonymous && password is null)
            throw new RemotePasswordException(site, rejected: false);
        int timeout = Math.Clamp(site.TimeoutSeconds, 5, 600) * 1000;
        var config = new FtpConfig
        {
            EncryptionMode = site.Encryption switch
            {
                RemoteEncryption.RequireExplicit => FtpEncryptionMode.Explicit,
                RemoteEncryption.Implicit => FtpEncryptionMode.Implicit,
                RemoteEncryption.None => FtpEncryptionMode.None,
                _ => FtpEncryptionMode.Auto,
            },
            DataConnectionType = site.TransferMode switch
            {
                RemoteTransferMode.Active => FtpDataConnectionType.AutoActive,
                // PASV como FileZilla: hay servidores (Pure-FTPd con TLS) que responden 500 a EPSV.
                _ => FtpDataConnectionType.PASV,
            },
            ConnectTimeout = timeout,
            ReadTimeout = timeout,
            DataConnectionConnectTimeout = timeout,
            DataConnectionReadTimeout = timeout,
            RetryAttempts = 1,
            // Las fechas de la lista, en la hora de este equipo.
            TimeConversion = FtpDate.LocalTime,
        };
        var client = new AsyncFtpClient(site.Host.Trim(), anonymous ? "anonymous" : site.User,
                                        anonymous ? "anonymous@" : password, site.EffectivePort, config);
        if (!string.IsNullOrWhiteSpace(site.Encoding))
        {
            try { client.Encoding = Encoding.GetEncoding(site.Encoding); }
            catch (ArgumentException) { }
        }
        client.ValidateCertificate += (_, e) =>
        {
            if (e.PolicyErrors == SslPolicyErrors.None)
            {
                e.Accept = true;
                return;
            }
            var certificate = Describe(e.Certificate, e.PolicyErrors);
            e.Accept = site.TrustedCertificate != null
                       && string.Equals(site.TrustedCertificate, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
            if (!e.Accept)
                rejected(certificate);
        };
        return client;
    }

    private static RemoteCertificate Describe(X509Certificate certificate, SslPolicyErrors errors)
    {
        using var full = new X509Certificate2(certificate);
        return new RemoteCertificate(full.Subject, full.Issuer, full.NotBefore, full.NotAfter,
                                     Convert.ToHexString(SHA256.HashData(full.RawData)), errors);
    }

    /// <summary>Conecta un cliente y traduce lo que falla a algo que el usuario entienda.</summary>
    private static async Task<AsyncFtpClient> ConnectAsync(RemoteSite site, string? password, CancellationToken token)
    {
        RemoteCertificate? untrusted = null;
        var client = CreateClient(site, password, c => untrusted = c);
        try
        {
            await client.Connect(token);
            return client;
        }
        catch (Exception ex)
        {
            client.Dispose();
            if (untrusted != null)
                throw new RemoteCertificateException(site, untrusted);
            throw Translate(site, ex);
        }
    }

    /// <summary>Un error de FluentFTP o de la red, como IOException con un mensaje claro (lo demás, igual).</summary>
    private static Exception Translate(RemoteSite site, Exception ex) => ex switch
    {
        OperationCanceledException or RemoteCertificateException or RemotePasswordException => ex,
        FtpAuthenticationException => new RemotePasswordException(site, rejected: true),
        AuthenticationException => new IOException(Loc.F("No se pudo establecer una conexión segura con {0}.", site.Host), ex),
        FtpSecurityNotAvailableException => new IOException(Loc.F("{0} no admite conexiones cifradas (TLS).", site.Host), ex),
        TimeoutException => new IOException(Loc.F("{0} no responde.", site.Host), ex),
        SocketException or FtpException { InnerException: SocketException }
            => new IOException(Loc.F("No se pudo conectar con {0}.", site.Host), ex),
        FtpCommandException command => new IOException(Loc.F("El servidor respondió: {0}", command.Message), ex),
        IOException => ex,
        _ => new IOException(ex.Message, ex),
    };

    /// <summary>Hace algo con la conexión de navegación del sitio (conecta, o reconecta si se cortó).</summary>
    private static async Task<T> UseAsync<T>(RemoteSite site, Func<AsyncFtpClient, Task<T>> work, CancellationToken token)
    {
        Connection connection;
        lock (_connections)
        {
            if (!_connections.TryGetValue(site.Id, out connection!))
                _connections[site.Id] = connection = new Connection();
        }
        await connection.Gate.WaitAsync(token);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (connection.Client is not { IsConnected: true })
                {
                    connection.Client?.Dispose();
                    connection.Client = null;
                    connection.Client = await ConnectAsync(site, PasswordOf(site), token);
                }
                try
                {
                    return await work(connection.Client);
                }
                catch (Exception ex) when (attempt == 0 && !token.IsCancellationRequested && Lost(ex))
                {
                    // Los servidores cierran las conexiones quietas: se reconecta una vez.
                    connection.Client?.Dispose();
                    connection.Client = null;
                }
                catch (Exception ex)
                {
                    throw Translate(site, ex);
                }
            }
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    private static bool Lost(Exception ex) => ex is IOException or SocketException or TimeoutException
        or FtpException { InnerException: IOException or SocketException }
        or FtpCommandException { CompletionCode: "421" };

    private static Task UseAsync(RemoteSite site, Func<AsyncFtpClient, Task> work, CancellationToken token) =>
        UseAsync<bool>(site, async client => { await work(client); return true; }, token);

    /// <summary>Prueba un sitio (sin guardarlo): null si se conectó, o el error.</summary>
    public static async Task<Exception?> TestAsync(RemoteSite site, string? password, CancellationToken token = default)
    {
        try
        {
            using var client = await ConnectAsync(site, site.Logon == RemoteLogon.Anonymous ? "" : password ?? "", token);
            await client.GetListing(string.IsNullOrWhiteSpace(site.InitialFolder) ? "/" : Clean(site.InitialFolder), token);
            await client.Disconnect(token);
            return null;
        }
        catch (Exception ex)
        {
            return Translate(site, ex);
        }
    }

    // ---- Lectura ----

    /// <summary>Lo que hay en una carpeta del servidor.</summary>
    public static Task<List<FileEntry>> ListAsync(RemoteLocation at, CancellationToken token) => UseAsync(at.Site, async client =>
    {
        var items = await ListingAsync(at.Site, client, at.Path, token);
        var entries = new List<FileEntry>(items.Length);
        foreach (var item in items)
        {
            if (item.Name is "." or ".." or "")
                continue;
            // Los enlaces de los servidores casi siempre son a carpetas ("www" → "public_html").
            bool folder = item.Type is FtpObjectType.Directory or FtpObjectType.Link;
            string path = PathOf(at.Site, Combine(at.Path, item.Name));
            entries.Add(new FileEntry(item.Name, path, folder, folder ? -1 : item.Size, item.Modified,
                                      FileIcons.TypeName(folder ? "" : Path.GetExtension(item.Name), folder),
                                      hidden: item.Name.StartsWith('.')));
        }
        return entries;
    }, token);

    /// <summary>Los sitios que no entienden "-a" (los ocultos) en la lista: se pide sin eso.</summary>
    private static readonly HashSet<string> _plainListing = new();

    /// <summary>La lista con los ocultos si el servidor lo permite (algunos responden 500 a "MLSD -a").</summary>
    private static async Task<FtpListItem[]> ListingAsync(RemoteSite site, AsyncFtpClient client, string path, CancellationToken token)
    {
        bool plain;
        lock (_plainListing)
            plain = _plainListing.Contains(site.Id);
        if (!plain)
        {
            try
            {
                return await client.GetListing(path, FtpListOption.AllFiles, token);
            }
            catch (FtpCommandException ex) when (ex.CompletionCode?.StartsWith('5') == true)
            {
                lock (_plainListing)
                    _plainListing.Add(site.Id);
                Log.Info($"Archivos: {site.Host} no admite listas con ocultos ({ex.Message}); se piden sin ellos");
            }
        }
        return await client.GetListing(path, FtpListOption.Auto, token);
    }

    /// <summary>Recorre una carpeta del servidor y anota lo que hay que bajar (carpeta por carpeta: no todos aceptan listas recursivas).</summary>
    private static async Task WalkAsync(RemoteSite site, AsyncFtpClient client, string remote, string local,
                                        List<string> folders, List<Job> jobs, CancellationToken token)
    {
        foreach (var item in await ListingAsync(site, client, remote, token))
        {
            if (item.Name is "." or ".." or "")
                continue;
            string target = Path.Combine(local, item.Name);
            string path = Combine(remote, item.Name);
            if (item.Type == FtpObjectType.Directory)
            {
                folders.Add(target);
                await WalkAsync(site, client, path, target, folders, jobs, token);
            }
            else if (item.Type == FtpObjectType.File)
                jobs.Add(new Job(target, path, item.Size));
        }
    }

    public static async Task<bool> ExistsAsync(string path, CancellationToken token = default)
    {
        if (Locate(path) is not { } at)
            return false;
        return await UseAsync(at.Site, async client =>
            await client.FileExists(at.Path, token) || await client.DirectoryExists(at.Path, token), token);
    }

    // ---- Cambios ----

    public static async Task CreateFolderAsync(string path, CancellationToken token = default)
    {
        if (Locate(path) is { } at)
            await UseAsync(at.Site, client => client.CreateDirectory(at.Path, false, token), token);
    }

    /// <summary>Cambia el nombre (o mueve dentro del mismo servidor) y devuelve la ruta nueva.</summary>
    public static async Task<string> RenameAsync(string path, string newName, CancellationToken token = default)
    {
        if (Locate(path) is not { } at)
            throw new IOException(Loc.T("Esta carpeta ya no existe."));
        int slash = at.Path.LastIndexOf('/');
        string target = Combine(slash <= 0 ? "/" : at.Path[..slash], newName);
        await UseAsync(at.Site, async client =>
        {
            if (await client.FileExists(target, token) || await client.DirectoryExists(target, token))
                throw new IOException(Loc.F("Ya hay algo que se llama «{0}».", newName));
            await client.Rename(at.Path, target, token);
        }, token);
        return PathOf(at.Site, target);
    }

    /// <summary>Borra para siempre (en un servidor no hay papelera); las carpetas, con todo lo de adentro.</summary>
    public static FileTransfer Delete(IReadOnlyList<FileEntry> entries, string folder) =>
        FileTransfers.Run(new FileTransfer(TransferKind.Delete, entries.Select(e => e.FullPath).ToList(), folder, TransferConflict.Replace),
            async transfer =>
            {
                var at = Locate(folder) ?? throw new IOException(Loc.T("Esta carpeta ya no existe."));
                transfer.TotalFiles = entries.Count;
                transfer.StateValue = TransferState.Running;
                foreach (var entry in entries)
                {
                    transfer.Cancellation.Token.ThrowIfCancellationRequested();
                    transfer.CurrentName = entry.Name;
                    try
                    {
                        var target = Locate(entry.FullPath)!.Value.Path;
                        await UseAsync(at.Site, client => entry.IsFolder
                            ? client.DeleteDirectory(target, transfer.Cancellation.Token)
                            : client.DeleteFile(target, transfer.Cancellation.Token), transfer.Cancellation.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        transfer.AddError(Loc.F("{0}: {1}", entry.Name, Translate(at.Site, ex).Message), admin: false, entry.FullPath);
                    }
                    transfer.DoneFiles++;
                }
            });

    // ---- Transferencias ----

    /// <summary>Una conexión propia para una transferencia (se cierra al terminar).</summary>
    private static async Task<AsyncFtpClient> TransferClientAsync(RemoteSite site, CancellationToken token)
    {
        try
        {
            return await ConnectAsync(site, PasswordOf(site), token);
        }
        catch (Exception ex)
        {
            throw Translate(site, ex);
        }
    }

    private sealed record Job(string Local, string Remote, long Size);

    /// <summary>Sube archivos y carpetas del equipo a una carpeta del servidor.</summary>
    public static FileTransfer Upload(IReadOnlyList<string> sources, string folder, TransferConflict conflict) =>
        FileTransfers.Run(new FileTransfer(TransferKind.Upload, sources, folder, conflict), async transfer =>
        {
            var at = Locate(folder) ?? throw new IOException(Loc.T("Esta carpeta ya no existe."));
            var token = transfer.Cancellation.Token;
            using var client = await TransferClientAsync(at.Site, token);
            var listing = (await client.GetListing(at.Path, token)).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

            // Primero todo lo que hay que subir (para el progreso), con el nombre que tendrá allá.
            var folders = new List<string>();
            var jobs = new List<Job>();
            foreach (var source in sources)
            {
                string name = Path.GetFileName(source.TrimEnd('\\'));
                if (conflict != TransferConflict.Replace)
                    name = FreeName(name, listing, conflict, Directory.Exists(source));
                listing.Add(name);
                string target = Combine(at.Path, name);
                transfer.Resolved[source] = PathOf(at.Site, target);
                if (Directory.Exists(source))
                {
                    folders.Add(target);
                    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                        folders.Add(Combine(target, Path.GetRelativePath(source, directory).Replace('\\', '/')));
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                        jobs.Add(new Job(file, Combine(target, Path.GetRelativePath(source, file).Replace('\\', '/')), new FileInfo(file).Length));
                }
                else if (File.Exists(source))
                    jobs.Add(new Job(source, target, new FileInfo(source).Length));
            }
            transfer.TotalFiles = jobs.Count;
            transfer.TotalBytes = jobs.Sum(j => j.Size);
            transfer.StateValue = TransferState.Running;
            if (sources.Count == 1)
                transfer.Result = transfer.Resolved.Values.FirstOrDefault();

            foreach (var directory in folders)
            {
                token.ThrowIfCancellationRequested();
                try { await client.CreateDirectory(directory, true, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    transfer.AddError(Loc.F("{0}: {1}", directory, Translate(at.Site, ex).Message), admin: false);
                }
            }
            long before = 0;
            foreach (var job in jobs)
            {
                token.ThrowIfCancellationRequested();
                transfer.CurrentName = Path.GetFileName(job.Local);
                long start = before;
                var progress = new Progress(p => Interlocked.Exchange(ref transfer.DoneBytes, start + p));
                try
                {
                    var status = await client.UploadFile(job.Local, job.Remote, FtpRemoteExists.Overwrite, true,
                                                         FtpVerify.None, progress, token);
                    if (status == FtpStatus.Failed)
                        transfer.AddError(Loc.F("No se pudo subir {0}.", Path.GetFileName(job.Local)), admin: false, job.Local);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    transfer.AddError(Loc.F("{0}: {1}", Path.GetFileName(job.Local), Translate(at.Site, ex).Message), admin: false, job.Local);
                }
                before += job.Size;
                Interlocked.Exchange(ref transfer.DoneBytes, before);
                transfer.DoneFiles++;
            }
            try { await client.Disconnect(token); } catch { }
        });

    /// <summary>Baja archivos y carpetas del servidor a una carpeta del equipo.</summary>
    public static FileTransfer Download(IReadOnlyList<FileEntry> entries, string folder, TransferConflict conflict) =>
        FileTransfers.Run(new FileTransfer(TransferKind.Download, entries.Select(e => e.FullPath).ToList(), folder, conflict),
            transfer => DownloadAsync(transfer, entries, folder, conflict));

    private static async Task DownloadAsync(FileTransfer transfer, IReadOnlyList<FileEntry> entries, string folder, TransferConflict conflict)
    {
        if (entries.Count == 0 || Locate(entries[0].FullPath) is not { } first)
            return;
        var site = first.Site;
        var token = transfer.Cancellation.Token;
        using var client = await TransferClientAsync(site, token);
        var taken = Directory.Exists(folder)
            ? Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jobs = new List<Job>();
        var folders = new List<string>();
        foreach (var entry in entries)
        {
            string remote = Locate(entry.FullPath)!.Value.Path;
            string name = conflict == TransferConflict.Replace ? entry.Name : FreeName(entry.Name, taken, conflict, entry.IsFolder);
            taken.Add(name);
            string local = Path.Combine(folder, name);
            transfer.Resolved[entry.FullPath] = local;
            if (entry.IsFolder)
            {
                folders.Add(local);
                await WalkAsync(site, client, remote, local, folders, jobs, token);
            }
            else
                jobs.Add(new Job(local, remote, Math.Max(0, entry.Size)));
        }
        transfer.TotalFiles = jobs.Count;
        transfer.TotalBytes = jobs.Sum(j => j.Size);
        transfer.StateValue = TransferState.Running;
        if (entries.Count == 1)
            transfer.Result = transfer.Resolved.Values.FirstOrDefault();

        foreach (var directory in folders)
            Directory.CreateDirectory(directory);
        long before = 0;
        foreach (var job in jobs)
        {
            token.ThrowIfCancellationRequested();
            transfer.CurrentName = Path.GetFileName(job.Local);
            long start = before;
            var progress = new Progress(p => Interlocked.Exchange(ref transfer.DoneBytes, start + p));
            try
            {
                var status = await client.DownloadFile(job.Local, job.Remote, FtpLocalExists.Overwrite, FtpVerify.None, progress, token);
                if (status == FtpStatus.Failed)
                    transfer.AddError(Loc.F("No se pudo descargar {0}.", Path.GetFileName(job.Local)), admin: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                transfer.AddError(Loc.F("{0}: {1}", Path.GetFileName(job.Local), Translate(site, ex).Message), admin: false);
            }
            before += job.Size;
            Interlocked.Exchange(ref transfer.DoneBytes, before);
            transfer.DoneFiles++;
        }
        try { await client.Disconnect(token); } catch { }
    }

    /// <summary>
    /// Baja archivos a una carpeta temporal propia (para abrirlos o copiarlos al portapapeles) y
    /// devuelve dónde quedaron; la tarjeta de progreso se ve mientras tanto.
    /// </summary>
    public static async Task<List<string>?> DownloadToTempAsync(IReadOnlyList<FileEntry> entries)
    {
        if (entries.Count == 0 || Locate(entries[0].FullPath) is not { } at)
            return null;
        string folder = Path.Combine(Path.GetTempPath(), "StrataUI", "ftp", at.Site.Id,
                                     Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Parent(entries[0].FullPath) ?? "")))[..12]);
        Directory.CreateDirectory(folder);
        var transfer = Download(entries, folder, TransferConflict.Replace);
        return await transfer.Finished.Task ? entries.Select(e => transfer.Resolved[e.FullPath]).ToList() : null;
    }

    /// <summary>Sube un archivo local sobre uno del servidor (al guardar uno abierto desde allá).</summary>
    public static Task<bool> UploadOverAsync(string local, string remote)
    {
        string folder = Parent(remote) ?? remote;
        return FileTransfers.Run(new FileTransfer(TransferKind.Upload, [local], folder, TransferConflict.Replace), async transfer =>
        {
            var at = Locate(remote) ?? throw new IOException(Loc.T("Esta carpeta ya no existe."));
            var token = transfer.Cancellation.Token;
            using var client = await TransferClientAsync(at.Site, token);
            transfer.TotalFiles = 1;
            transfer.TotalBytes = new FileInfo(local).Length;
            transfer.StateValue = TransferState.Running;
            transfer.CurrentName = Path.GetFileName(local);
            var status = await client.UploadFile(local, at.Path, FtpRemoteExists.Overwrite, false, FtpVerify.None,
                                                 new Progress(p => Interlocked.Exchange(ref transfer.DoneBytes, p)), token);
            if (status == FtpStatus.Failed)
                transfer.AddError(Loc.F("No se pudo subir {0}.", Path.GetFileName(local)), admin: false);
            transfer.DoneFiles = 1;
            try { await client.Disconnect(token); } catch { }
        }).Finished.Task;
    }

    /// <summary>"nombre (2).txt" o "nombre - copia.txt" que no esté en <paramref name="taken"/>.</summary>
    private static string FreeName(string name, ISet<string> taken, TransferConflict conflict, bool folder)
    {
        if (!taken.Contains(name))
            return name;
        string stem = folder ? name : Path.GetFileNameWithoutExtension(name), extension = folder ? "" : Path.GetExtension(name);
        if (conflict == TransferConflict.Duplicate)
        {
            string copy = Loc.F("{0} - copia", stem) + extension;
            if (!taken.Contains(copy))
                return copy;
            stem = Loc.F("{0} - copia", stem);
        }
        for (int i = 2; ; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Progreso de FluentFTP sin pasar por el hilo de la UI (los bytes del archivo actual).</summary>
    private sealed class Progress(Action<long> report) : IProgress<FtpProgress>
    {
        public void Report(FtpProgress value) => report(value.TransferredBytes);
    }
}
