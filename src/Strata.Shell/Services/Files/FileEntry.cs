using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>Un archivo o carpeta del explorador de Strata UI. El ícono y la miniatura llegan después.</summary>
public sealed class FileEntry : INotifyPropertyChanged
{
    public FileEntry(FileSystemInfo info, string typeName)
    {
        Name = info.Name;
        FullPath = info.FullName;
        IsFolder = info is DirectoryInfo;
        Size = info is FileInfo file ? file.Length : -1;
        Modified = info.LastWriteTime;
        Attributes = info.Attributes;
        TypeName = typeName;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFolder { get; }
    /// <summary>Tamaño en bytes; -1 en las carpetas.</summary>
    public long Size { get; }
    public DateTime Modified { get; }
    public FileAttributes Attributes { get; }
    public string TypeName { get; }
    public string Extension => IsFolder ? "" : Path.GetExtension(Name);
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;

    public string SizeText => IsFolder ? "" : FormatSize(Size);
    public string ModifiedText => Modified.ToString("g", CultureInfo.CurrentCulture);

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; Changed(); Changed(nameof(Preview)); }
    }

    private ImageSource? _thumbnail;
    /// <summary>Miniatura de una imagen (solo en la cuadrícula).</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; Changed(); Changed(nameof(Preview)); Changed(nameof(HasThumbnail)); }
    }

    public bool HasThumbnail => _thumbnail != null;
    public ImageSource? Preview => _thumbnail ?? _icon;

    private bool _isCut;
    /// <summary>Está en el portapapeles para moverse: se ve tenue, como en el Explorador.</summary>
    public bool IsCut
    {
        get => _isCut;
        set { _isCut = value; Changed(); }
    }

    private bool _isRenaming;
    public bool IsRenaming
    {
        get => _isRenaming;
        set { _isRenaming = value; Changed(); }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 40 => Loc.F("{0:0.0} TB", bytes / (double)(1L << 40)),
        >= 1L << 30 => Loc.F("{0:0.0} GB", bytes / (double)(1L << 30)),
        >= 1L << 20 => Loc.F("{0:0} MB", bytes / (double)(1L << 20)),
        // Como el Explorador: un archivo pequeño dice 1 KB, no 0.
        _ => Loc.F("{0:0} KB", Math.Ceiling(bytes / 1024.0)),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
