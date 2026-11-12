namespace Strata.Shell.Services.Files.Remote;

/// <summary>Cómo se cifra la conexión con un servidor FTP (las mismas opciones que FileZilla).</summary>
public enum RemoteEncryption
{
    /// <summary>FTP sobre TLS explícito si el servidor lo admite; si no, sin cifrar.</summary>
    ExplicitIfAvailable,
    /// <summary>Exigir FTP sobre TLS explícito (si el servidor no lo admite, no se conecta).</summary>
    RequireExplicit,
    /// <summary>FTP sobre TLS implícito (puerto 990).</summary>
    Implicit,
    /// <summary>FTP sin cifrar: la contraseña y los archivos viajan a la vista.</summary>
    None,
}

/// <summary>Cómo se inicia sesión.</summary>
public enum RemoteLogon
{
    /// <summary>Usuario y contraseña guardada.</summary>
    Normal,
    /// <summary>Anónimo (sin cuenta).</summary>
    Anonymous,
    /// <summary>Se pide la contraseña al conectar (no se guarda).</summary>
    Ask,
}

/// <summary>Modo de la conexión de datos.</summary>
public enum RemoteTransferMode { Default, Passive, Active }

/// <summary>
/// Un servidor guardado en el administrador de sitios de Archivos. Se guarda en settings.json sin la
/// contraseña, que va al Administrador de credenciales de Windows (<see cref="RemoteCredentials"/>).
/// </summary>
public sealed class RemoteSite
{
    /// <summary>Identificador estable (la credencial y las conexiones abiertas se buscan con él).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Nombre que se ve en la barra lateral y en las rutas ("ftp://Nombre/carpeta"); sin "/".</summary>
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    /// <summary>0 = el de siempre (21, o 990 con TLS implícito).</summary>
    public int Port { get; set; }
    public RemoteEncryption Encryption { get; set; } = RemoteEncryption.ExplicitIfAvailable;
    public RemoteLogon Logon { get; set; } = RemoteLogon.Normal;
    public string User { get; set; } = "";
    public RemoteTransferMode TransferMode { get; set; } = RemoteTransferMode.Default;
    /// <summary>Codificación de los nombres: null = automática (UTF-8 si el servidor lo admite).</summary>
    public string? Encoding { get; set; }
    /// <summary>Carpeta en la que se abre (vacía = la raíz).</summary>
    public string InitialFolder { get; set; } = "";
    /// <summary>Segundos sin respuesta antes de rendirse.</summary>
    public int TimeoutSeconds { get; set; } = 20;
    /// <summary>Huella SHA-256 del certificado que el usuario aceptó aunque no fuera de confianza.</summary>
    public string? TrustedCertificate { get; set; }
    /// <summary>Nota libre (como los "Comentarios" de FileZilla).</summary>
    public string Comments { get; set; } = "";

    public int EffectivePort => Port > 0 ? Port : Encryption == RemoteEncryption.Implicit ? 990 : 21;

    public RemoteSite Clone() => (RemoteSite)MemberwiseClone();
}
