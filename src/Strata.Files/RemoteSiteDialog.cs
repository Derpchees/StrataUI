using System.Net.Security;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Files.Remote;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Strata.Shell.Files;

/// <summary>
/// El administrador de sitios de Archivos (como el de FileZilla): agregar o editar un servidor FTP,
/// probar la conexión, pedir la contraseña y decidir sobre un certificado que no es de confianza.
/// </summary>
internal static class RemoteSiteDialog
{
    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 4),
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextFillColorSecondaryBrush"),
    };

    private static ComboBox Choices(IEnumerable<string> items, int selected) => new()
    {
        ItemsSource = items.ToList(),
        SelectedIndex = selected,
        Margin = new Thickness(0, 0, 0, 12),
    };

    private static readonly string?[] Encodings = [null, "utf-8", "windows-1252", "iso-8859-1"];

    /// <summary>Agrega (<paramref name="site"/> null) o edita un sitio; devuelve el guardado, o null si se canceló.</summary>
    public static async Task<RemoteSite?> EditAsync(Window? owner, RemoteSite? site)
    {
        var edited = site?.Clone() ?? new RemoteSite();
        bool hasPassword = site != null && RemoteCredentials.Read(site) is { Length: > 0 };

        var name = new TextBox { Text = edited.Name, Margin = new Thickness(0, 0, 0, 12) };
        var host = new TextBox { Text = edited.Host, Margin = new Thickness(0, 0, 8, 12) };
        var port = new Wpf.Ui.Controls.TextBox { Text = edited.Port > 0 ? edited.Port.ToString() : "", Width = 80, Margin = new Thickness(0, 0, 0, 12) };
        var encryption = Choices([
            Loc.T("Usar FTP sobre TLS explícito si está disponible"),
            Loc.T("Requerir FTP sobre TLS explícito"),
            Loc.T("Requerir FTP sobre TLS implícito"),
            Loc.T("Usar FTP sin cifrar (inseguro)"),
        ], (int)edited.Encryption);
        var warning = new TextBlock
        {
            Text = Loc.T("Sin cifrar, la contraseña y los archivos viajan a la vista de cualquiera en la red."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, -6, 0, 12),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("SystemFillColorCautionBrush"),
        };
        var logon = Choices([Loc.T("Normal"), Loc.T("Anónimo"), Loc.T("Pedir la contraseña al conectar")], (int)edited.Logon);
        var user = new TextBox { Text = edited.User, Margin = new Thickness(0, 0, 0, 12) };
        var password = new RevealPasswordBox { Margin = new Thickness(0, 0, 0, 4) };
        var passwordHint = new TextBlock
        {
            Text = hasPassword ? Loc.T("Hay una contraseña guardada; déjala vacía para conservarla.") : "",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextFillColorTertiaryBrush"),
        };
        var userPanel = new StackPanel { Children = { Caption(Loc.T("Usuario")), user } };
        var passwordPanel = new StackPanel { Children = { Caption(Loc.T("Contraseña")), password, passwordHint } };

        var folder = new TextBox { Text = edited.InitialFolder, Margin = new Thickness(0, 0, 0, 12) };
        var mode = Choices([Loc.T("Predeterminado (pasivo)"), Loc.T("Pasivo"), Loc.T("Activo")], (int)edited.TransferMode);
        var encoding = Choices([Loc.T("Automática"), "UTF-8", "Windows-1252", "ISO-8859-1"],
                               Math.Max(0, Array.FindIndex(Encodings, e => string.Equals(e, edited.Encoding, StringComparison.OrdinalIgnoreCase))));
        var timeout = new TextBox { Text = edited.TimeoutSeconds.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        var comments = new TextBox { Text = edited.Comments, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, Margin = new Thickness(0, 0, 0, 4) };
        var advanced = new Expander
        {
            Header = Loc.T("Opciones avanzadas"),
            Margin = new Thickness(0, 0, 0, 12),
            Content = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    Caption(Loc.T("Carpeta inicial en el servidor")), folder,
                    Caption(Loc.T("Modo de transferencia")), mode,
                    Caption(Loc.T("Codificación de los nombres")), encoding,
                    Caption(Loc.T("Tiempo de espera (segundos)")), timeout,
                    Caption(Loc.T("Comentarios")), comments,
                },
            },
        };

        var testResult = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var test = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("Probar conexión"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.PlugConnected24 },
        };
        var testRow = new DockPanel { Children = { test, testResult } };
        DockPanel.SetDock(test, Dock.Left);

        var hostRow = new DockPanel();
        var portPanel = new StackPanel { Children = { Caption(Loc.T("Puerto")), port } };
        DockPanel.SetDock(portPanel, Dock.Right);
        hostRow.Children.Add(portPanel);
        hostRow.Children.Add(new StackPanel { Children = { Caption(Loc.T("Servidor")), host } });

        var form = new StackPanel
        {
            Children =
            {
                hostRow,
                Caption(Loc.T("Cifrado")), encryption, warning,
                Caption(Loc.T("Tipo de inicio de sesión")), logon,
                userPanel, passwordPanel,
                Caption(Loc.T("Nombre del sitio")), name,
                advanced, testRow,
            },
        };
        var content = new ScrollViewer
        {
            Content = form,
            MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 320),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 6, 0),
        };

        void Sync()
        {
            warning.Visibility = encryption.SelectedIndex == (int)RemoteEncryption.None ? Visibility.Visible : Visibility.Collapsed;
            userPanel.Visibility = logon.SelectedIndex == (int)RemoteLogon.Anonymous ? Visibility.Collapsed : Visibility.Visible;
            passwordPanel.Visibility = logon.SelectedIndex == (int)RemoteLogon.Normal ? Visibility.Visible : Visibility.Collapsed;
            port.PlaceholderText = encryption.SelectedIndex == (int)RemoteEncryption.Implicit ? "990" : "21";
        }
        encryption.SelectionChanged += (_, _) => Sync();
        logon.SelectionChanged += (_, _) => Sync();
        Sync();

        // Lo escrito, como un sitio (sin guardar todavía).
        RemoteSite Read()
        {
            var result = edited.Clone();
            result.Host = Clean(host.Text);
            result.Port = int.TryParse(port.Text.Trim(), out int p) && p is > 0 and < 65536 ? p : 0;
            result.Encryption = (RemoteEncryption)Math.Max(0, encryption.SelectedIndex);
            result.Logon = (RemoteLogon)Math.Max(0, logon.SelectedIndex);
            result.User = user.Text.Trim();
            result.InitialFolder = folder.Text.Trim();
            result.TransferMode = (RemoteTransferMode)Math.Max(0, mode.SelectedIndex);
            result.Encoding = Encodings[Math.Max(0, encoding.SelectedIndex)];
            result.TimeoutSeconds = int.TryParse(timeout.Text.Trim(), out int t) ? Math.Clamp(t, 5, 600) : 20;
            result.Comments = comments.Text;
            result.Name = UniqueName(string.IsNullOrWhiteSpace(name.Text) ? result.Host : name.Text.Trim().Replace('/', '-').Replace('\\', '-'), edited.Id);
            // Otro servidor: el certificado aceptado era del de antes.
            if (site != null && !string.Equals(site.Host, result.Host, StringComparison.OrdinalIgnoreCase))
                result.TrustedCertificate = null;
            return result;
        }
        string? PasswordFor(RemoteSite candidate) => candidate.Logon == RemoteLogon.Normal
            ? password.Password.Length > 0 ? password.Password : site != null ? RemoteCredentials.Read(site) ?? "" : ""
            : password.Password;

        test.Click += async (_, _) =>
        {
            var candidate = Read();
            if (candidate.Host.Length == 0)
            {
                testResult.Text = Loc.T("Escribe la dirección del servidor.");
                return;
            }
            if (candidate.Logon == RemoteLogon.Ask && password.Password.Length == 0)
            {
                testResult.Text = Loc.T("Escribe la contraseña para conectarte.");
                return;
            }
            test.IsEnabled = false;
            testResult.Text = Loc.F("Conectando con {0}…", candidate.Host);
            var error = await RemoteFiles.TestAsync(candidate, PasswordFor(candidate));
            if (error is RemoteCertificateException untrusted
                && await AskCertificateAsync(Window.GetWindow(form), candidate, untrusted.Certificate, save: false))
            {
                edited.TrustedCertificate = candidate.TrustedCertificate = untrusted.Certificate.Thumbprint;
                error = await RemoteFiles.TestAsync(candidate, PasswordFor(candidate));
            }
            test.IsEnabled = true;
            testResult.Text = error is null ? Loc.T("Conexión correcta.") : error.Message;
        };

        if (await StrataDialog.ShowAsync(owner, site is null ? Loc.T("Agregar servidor FTP") : Loc.F("Editar {0}", site.Name),
                                         Loc.T("Se guarda en este equipo; la contraseña queda cifrada en el Administrador de credenciales de Windows."),
                                         content, false, Loc.T("Guardar"), Loc.T("Cancelar")) != 0)
            return null;
        var saved = Read();
        if (saved.Host.Length == 0)
        {
            await StrataDialog.ShowAsync(owner, Loc.T("Falta el servidor"), Loc.T("Escribe la dirección del servidor."), Loc.T("Aceptar"));
            return null;
        }
        if (saved.Logon == RemoteLogon.Normal && password.Password.Length > 0)
            RemoteCredentials.Write(saved, password.Password);
        else if (saved.Logon != RemoteLogon.Normal)
            RemoteCredentials.Delete(saved);
        Settings.Update(s =>
        {
            int index = s.FilesRemoteSites.FindIndex(x => x.Id == saved.Id);
            if (index >= 0)
                s.FilesRemoteSites[index] = saved;
            else
                s.FilesRemoteSites.Add(saved);
        });
        if (site != null)
            RemoteFiles.Forget(site);
        Log.Info($"Archivos: sitio FTP guardado {saved.Host}:{saved.EffectivePort} ({saved.Encryption})");
        return saved;
    }

    /// <summary>"ftp://servidor:21/x" o "servidor " → "servidor".</summary>
    private static string Clean(string host)
    {
        host = host.Trim();
        foreach (var scheme in new[] { "ftp://", "ftps://", "ftpes://" })
            if (host.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                host = host[scheme.Length..];
        int slash = host.IndexOf('/');
        if (slash >= 0)
            host = host[..slash];
        int colon = host.LastIndexOf(':');
        return colon > 0 && host.IndexOf(':') == colon ? host[..colon] : host;
    }

    private static string UniqueName(string name, string id)
    {
        bool Taken(string candidate) => RemoteFiles.Sites.Any(s => s.Id != id && s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name))
            return name;
        for (int i = 2; ; i++)
            if (!Taken($"{name} ({i})"))
                return $"{name} ({i})";
    }

    /// <summary>Quita un sitio (y su contraseña) después de preguntar.</summary>
    public static async Task<bool> RemoveAsync(Window? owner, RemoteSite site)
    {
        if (!await StrataDialog.ConfirmAsync(owner, Loc.F("¿Quitar {0}?", site.Name),
                Loc.T("Se borra del administrador de sitios junto con su contraseña guardada. Los archivos del servidor no se tocan."),
                Loc.T("Quitar")))
            return false;
        RemoteFiles.Forget(site);
        RemoteCredentials.Delete(site);
        Settings.Update(s => s.FilesRemoteSites.RemoveAll(x => x.Id == site.Id));
        return true;
    }

    /// <summary>Pide la contraseña de un sitio (con la opción de recordarla); true si se escribió una.</summary>
    public static async Task<bool> AskPasswordAsync(Window? owner, RemoteSite site, bool rejected)
    {
        var password = new RevealPasswordBox { Margin = new Thickness(0, 0, 0, 12) };
        var remember = new CheckBox { Content = Loc.T("Recordar la contraseña"), IsChecked = site.Logon == RemoteLogon.Normal };
        var panel = new StackPanel { Children = { Caption(Loc.F("Contraseña de {0}", site.User)), password, remember } };
        string message = rejected ? Loc.F("{0} rechazó el usuario o la contraseña.", site.Host)
                                  : Loc.F("Escribe la contraseña para conectarte a {0}.", site.Host);
        if (await StrataDialog.ShowAsync(owner, site.Name, message, panel, false, Loc.T("Conectar"), Loc.T("Cancelar")) != 0
            || password.Password.Length == 0)
            return false;
        RemoteFiles.SetPassword(site, password.Password, save: remember.IsChecked == true);
        return true;
    }

    /// <summary>
    /// Un certificado que no es de confianza (vencido, de otro nombre o firmado por sí mismo): se
    /// muestran sus datos y se decide. Aceptado, se recuerda con el sitio (<paramref name="save"/>).
    /// </summary>
    public static async Task<bool> AskCertificateAsync(Window? owner, RemoteSite site, RemoteCertificate certificate, bool save = true)
    {
        var reasons = new List<string>();
        if (certificate.Errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            reasons.Add(Loc.F("Es de otro nombre, no de {0}.", site.Host));
        if (certificate.Errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            reasons.Add(certificate.ValidTo < DateTime.Now ? Loc.T("Venció.") : Loc.T("No lo firmó una entidad de confianza."));
        if (certificate.Errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            reasons.Add(Loc.T("El servidor no mostró ningún certificado."));
        var details = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Text = string.Join(Environment.NewLine,
                Loc.F("Emitido para: {0}", certificate.Subject),
                Loc.F("Emitido por: {0}", certificate.Issuer),
                Loc.F("Válido: del {0:d} al {1:d}", certificate.ValidFrom, certificate.ValidTo),
                Loc.F("Huella SHA-256: {0}", certificate.Thumbprint)),
        };
        string message = Loc.F("No se puede comprobar que el servidor sea {0}. {1} Si confías en él, la conexión sigue cifrada.",
                               site.Host, string.Join(" ", reasons));
        if (await StrataDialog.ShowAsync(owner, Loc.T("Certificado desconocido"), message, details, false,
                                         Loc.T("Confiar siempre en este certificado"), Loc.T("Cancelar")) != 0)
            return false;
        if (save)
            RemoteFiles.TrustCertificate(site, certificate);
        else
            site.TrustedCertificate = certificate.Thumbprint;
        return true;
    }
}
