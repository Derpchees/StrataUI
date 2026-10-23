using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Un lugar de la barra lateral o de la portada del explorador: carpeta del usuario o unidad.
/// <see cref="Path"/> null es la portada (Este equipo).
/// </summary>
public sealed class Place : INotifyPropertyChanged
{
    public Place(string name, string? path, SymbolRegular symbol)
    {
        Name = name;
        Path = path;
        Symbol = symbol;
    }

    public string Name { get; }
    public string? Path { get; }
    public SymbolRegular Symbol { get; }

    /// <summary>Ícono de Windows (unidades); sin él se usa <see cref="Symbol"/>.</summary>
    public ImageSource? Icon { get; init; }
    public bool HasIcon => Icon != null;

    /// <summary>Carpeta sincronizada con la nube (OneDrive, Google Drive…): sus archivos tienen estado.</summary>
    public bool IsCloud { get; init; }
    /// <summary>Ubicación de red recordada: se puede quitar de la lista.</summary>
    public bool CanForget { get; init; }

    /// <summary>Espacio usado de una unidad (0–100); -1 si no es una unidad.</summary>
    public double UsedPercent { get; init; } = -1;
    public bool IsDrive => UsedPercent >= 0;
    /// <summary>La unidad está casi llena: la barra se pinta de rojo, como en el Explorador.</summary>
    public bool IsAlmostFull => UsedPercent >= 90;
    public string Detail { get; init; } = "";

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
                return;
            _isCurrent = value;
            Changed();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Lo que muestra la barra lateral y la portada, leído fuera de la interfaz (ver <see cref="Places.Storage"/>).</summary>
public sealed record StorageView(List<Place> Drives, List<Place> Cloud, List<Place> Network);

public static class Places
{
    public static string? Downloads => KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"));

    /// <summary>Carpetas del usuario (las que existen), en el orden del Explorador.</summary>
    public static List<Place> Folders()
    {
        var list = new List<Place>
        {
            new(Loc.T("Escritorio"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), SymbolRegular.Desktop24),
            new(Loc.T("Descargas"), Downloads, SymbolRegular.ArrowDownload24),
            new(Loc.T("Documentos"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), SymbolRegular.Document24),
            new(Loc.T("Imágenes"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), SymbolRegular.Image24),
            new(Loc.T("Música"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), SymbolRegular.MusicNote224),
            new("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), SymbolRegular.Video24),
        };
        return list.Where(p => !string.IsNullOrEmpty(p.Path) && Directory.Exists(p.Path)).ToList();
    }

    /// <summary>
    /// Unidades, nube y red. Lento con unidades de red o lectoras: llamar fuera de la interfaz (en el
    /// hilo de los íconos). Las ubicaciones de red recordadas no se comprueban (un servidor apagado
    /// tardaría en contestar); si no responde, se ve al abrirla.
    /// </summary>
    public static StorageView Storage(IEnumerable<string> rememberedNetwork)
    {
        var drives = new List<Place>();
        var network = new List<Place>();
        var cloud = CloudFolders();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;
                string root = drive.RootDirectory.FullName;
                long total = drive.TotalSize, free = drive.AvailableFreeSpace;
                bool googleDrive = drive.VolumeLabel.Equals("Google Drive", StringComparison.OrdinalIgnoreCase);
                var place = new Place(googleDrive ? "Google Drive" : FileIcons.DisplayName(root), root, drive.DriveType switch
                {
                    _ when googleDrive => SymbolRegular.Cloud24,
                    DriveType.Removable => SymbolRegular.UsbStick24,
                    DriveType.Network => SymbolRegular.Server24,
                    _ => SymbolRegular.HardDrive20,
                })
                {
                    Icon = FileIcons.IconOfPath(root, folder: true),
                    UsedPercent = total > 0 ? 100.0 * (total - free) / total : 0,
                    Detail = Loc.F("{0} libres de {1}", FileEntry.FormatSize(free), FileEntry.FormatSize(total)),
                    IsCloud = googleDrive,
                };
                (googleDrive ? cloud : drive.DriveType == DriveType.Network ? network : drives).Add(place);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Una lectora sin disco o una unidad de red caída: no se muestra.
            }
        }
        var connected = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network)
                                 .Select(d => d.Name[..1].ToUpperInvariant()).ToHashSet();
        try
        {
            foreach (var saved in SavedNetwork(connected))
            {
                if (!network.Any(p => string.Equals(p.Path, saved.Path, StringComparison.OrdinalIgnoreCase)))
                    network.Add(saved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Info($"Archivos: no se pudieron leer las ubicaciones de red ({ex.Message.Trim()})");
        }
        foreach (var unc in rememberedNetwork)
        {
            if (network.Any(p => string.Equals(p.Path, unc, StringComparison.OrdinalIgnoreCase)))
                continue;
            network.Add(new Place(NameOf(unc), unc, SymbolRegular.Server24) { Detail = unc, CanForget = true });
        }
        return new StorageView(drives, cloud, network);
    }

    /// <summary>
    /// Carpetas sincronizadas con la nube: las que un proveedor instalado (OneDrive, Dropbox, iCloud,
    /// Box…) registró como raíz de sincronización de Windows. Si se desinstala, su carpeta queda en
    /// el disco pero sale del registro, y ya no se muestra como nube. Google Drive monta su propia
    /// unidad (ver <see cref="Storage"/>).
    /// </summary>
    public static List<Place> CloudFolders()
    {
        var found = new List<string>();
        try
        {
            string? user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            using var manager = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager");
            foreach (var name in manager?.GetSubKeyNames() ?? [])
            {
                using var roots = manager!.OpenSubKey(name + @"\UserSyncRoots");
                if (roots is null)
                    continue;
                foreach (var value in roots.GetValueNames())
                {
                    if ((user is null || value.Equals(user, StringComparison.OrdinalIgnoreCase))
                        && roots.GetValue(value) is string folder && folder.Length > 0)
                        found.Add(folder);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }
        return found.Where(Directory.Exists)
                    .DistinctBy(f => f.TrimEnd('\\').ToLowerInvariant())
                    .Select(f => new Place(CloudName(f), f.TrimEnd('\\'), SymbolRegular.Cloud24) { IsCloud = true })
                    .ToList();
    }

    /// <summary>"OneDrive", "OneDrive - Empresa", "Dropbox"…: el nombre de su carpeta, salvo los que lo abrevian.</summary>
    internal static string CloudName(string folder) => Path.GetFileName(folder.TrimEnd('\\')) switch
    {
        "iCloudDrive" => "iCloud Drive",
        var name => name,
    };

    /// <summary>
    /// Ubicaciones de red de Este equipo (accesos directos de "Network Shortcuts") y unidades de red
    /// que Windows recuerda pero no están conectadas. No se comprueban: se ve al abrirlas.
    /// </summary>
    private static IEnumerable<Place> SavedNetwork(IReadOnlyCollection<string> connectedLetters)
    {
        string shortcuts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                        "Microsoft", "Windows", "Network Shortcuts");
        if (Directory.Exists(shortcuts))
        {
            foreach (var folder in Directory.EnumerateDirectories(shortcuts))
            {
                string link = Path.Combine(folder, "target.lnk");
                if (File.Exists(link) && StartMenu.ExtraApps.ShortcutTarget(link) is { Length: > 2 } target && target.StartsWith(@"\\"))
                    yield return new Place(Path.GetFileName(folder), target, SymbolRegular.Globe24) { Detail = target };
            }
        }
        using var mapped = Registry.CurrentUser.OpenSubKey("Network");
        foreach (var letter in mapped?.GetSubKeyNames() ?? [])
        {
            if (connectedLetters.Contains(letter.ToUpperInvariant()))
                continue;
            using var drive = mapped!.OpenSubKey(letter);
            if (drive?.GetValue("RemotePath") is string remote && remote.StartsWith(@"\\"))
                yield return new Place($"{NameOf(remote)} ({letter.ToUpperInvariant()}:)", remote, SymbolRegular.Server24)
                {
                    Detail = Loc.T("Desconectada"),
                };
        }
    }

    /// <summary>Ruta de red "\\servidor" (sin recurso): se listan sus carpetas compartidas.</summary>
    public static bool IsServer(string? path) =>
        path is { Length: > 2 } && path.StartsWith(@"\\") && path.IndexOf('\\', 2) < 0;

    /// <summary>La raíz "\\servidor\recurso" de una ruta de red (null si no es de red).</summary>
    public static string? ShareRoot(string path) =>
        path.StartsWith(@"\\") && !IsServer(path) ? Path.GetPathRoot(path)?.TrimEnd('\\') : null;

    /// <summary>Nombre para la ruta de navegación y el título: el de Windows en las unidades.</summary>
    public static string NameOf(string path)
    {
        if (IsServer(path))
            return path[2..];
        if (Path.GetPathRoot(path) is { } root && string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return root.StartsWith(@"\\") ? Path.GetFileName(root.TrimEnd('\\')) : FileIcons.DisplayName(root);
        return Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
    }

    /// <summary>Carpetas compartidas de un servidor (sin las administrativas, que terminan en $).</summary>
    public static List<string> Shares(string server)
    {
        var shares = new List<string>();
        int result = NetShareEnum(server, 1, out var buffer, -1, out int read, out _, IntPtr.Zero);
        if (result != 0)
            throw new IOException(new System.ComponentModel.Win32Exception(result).Message);
        try
        {
            int size = Marshal.SizeOf<ShareInfo1>();
            for (int i = 0; i < read; i++)
            {
                var info = Marshal.PtrToStructure<ShareInfo1>(buffer + i * size);
                // Solo carpetas (tipo 0), no impresoras ni las administrativas.
                if ((info.Type & 0xFF) == 0 && !info.Name.EndsWith('$'))
                    shares.Add(info.Name);
            }
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
        return shares;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo1
    {
        public string Name;
        public uint Type;
        public string Remark;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string server, int level, out IntPtr buffer, int maxLength, out int read, out int total, IntPtr resume);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer) != 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
