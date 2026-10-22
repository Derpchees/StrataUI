using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media;
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

public static class Places
{
    /// <summary>Carpetas del usuario (las que existen), en el orden del Explorador.</summary>
    public static List<Place> Folders()
    {
        var list = new List<Place>
        {
            new(Loc.T("Escritorio"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), SymbolRegular.Desktop24),
            new(Loc.T("Descargas"), KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")), SymbolRegular.ArrowDownload24),
            new(Loc.T("Documentos"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), SymbolRegular.Document24),
            new(Loc.T("Imágenes"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), SymbolRegular.Image24),
            new(Loc.T("Música"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), SymbolRegular.MusicNote224),
            new("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), SymbolRegular.Video24),
        };
        return list.Where(p => !string.IsNullOrEmpty(p.Path) && Directory.Exists(p.Path)).ToList();
    }

    /// <summary>
    /// Unidades listas, con su nombre de Windows y el espacio usado. Lento con unidades de red o
    /// lectoras: llamar fuera de la interfaz (en el hilo de los íconos).
    /// </summary>
    public static List<Place> Drives()
    {
        var list = new List<Place>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                    continue;
                long total = drive.TotalSize, free = drive.AvailableFreeSpace;
                list.Add(new Place(FileIcons.DisplayName(drive.RootDirectory.FullName), drive.RootDirectory.FullName, drive.DriveType switch
                {
                    DriveType.Removable => SymbolRegular.UsbStick24,
                    DriveType.Network => SymbolRegular.Server24,
                    _ => SymbolRegular.HardDrive20,
                })
                {
                    Icon = FileIcons.IconOfPath(drive.RootDirectory.FullName, folder: true),
                    UsedPercent = total > 0 ? 100.0 * (total - free) / total : 0,
                    Detail = Loc.F("{0} libres de {1}", FileEntry.FormatSize(free), FileEntry.FormatSize(total)),
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Una lectora sin disco o una unidad de red caída: no se muestra.
            }
        }
        return list;
    }

    /// <summary>Nombre para la ruta de navegación y el título: el de Windows en las unidades.</summary>
    public static string NameOf(string path)
    {
        if (Path.GetPathRoot(path) is { } root && string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return FileIcons.DisplayName(root);
        return Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
    }

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
