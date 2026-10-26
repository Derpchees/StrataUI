using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>Un elemento de un comprimido: su ruta adentro (con "\"), si es carpeta, tamaño y fecha.</summary>
public sealed record ArchiveItem(int Index, string Path, bool IsFolder, long Size, DateTime Modified, bool Encrypted)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Lo que hay en un comprimido, con sus carpetas (también las que solo aparecen en las rutas).</summary>
public sealed class ArchiveListing
{
    internal ArchiveListing(string archive, string format, List<ArchiveItem> items, DateTime stamp, long length)
    {
        Archive = archive;
        Format = format;
        Items = items;
        Stamp = stamp;
        Length = length;
        foreach (var item in items)
        {
            if (item.IsFolder)
                _folders.TryAdd(item.Path, item);
            for (string? parent = Parent(item.Path); parent != null; parent = Parent(parent))
                _folders.TryAdd(parent, null);
        }
        foreach (var item in items.Where(i => !i.IsFolder))
            Add(Parent(item.Path) ?? "", new Child(item.Name, item.Path, false, item));
        foreach (var (folder, item) in _folders)
            Add(Parent(folder) ?? "", new Child(System.IO.Path.GetFileName(folder), folder, true, item));
    }

    /// <summary>Un elemento de una carpeta del comprimido (<see cref="Item"/> null en las carpetas implícitas).</summary>
    public sealed record Child(string Name, string Path, bool IsFolder, ArchiveItem? Item);

    private readonly Dictionary<string, ArchiveItem?> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Child>> _children = new(StringComparer.OrdinalIgnoreCase);

    public string Archive { get; }
    public string Format { get; }
    public IReadOnlyList<ArchiveItem> Items { get; }
    internal DateTime Stamp { get; }
    internal long Length { get; }

    /// <summary>Lo que hay directamente en una carpeta ("" es la raíz).</summary>
    public IReadOnlyList<Child> Children(string folder) =>
        _children.TryGetValue(folder.Trim('\\'), out var list) ? list : [];

    public bool HasFolder(string folder) => folder.Trim('\\').Length == 0 || _folders.ContainsKey(folder.Trim('\\'));

    /// <summary>Todo lo que está debajo de una carpeta (para buscar en las subcarpetas).</summary>
    public IEnumerable<Child> Below(string folder)
    {
        string prefix = folder.Trim('\\') is { Length: > 0 } f ? f + "\\" : "";
        return _children.Where(c => c.Key.Length > 0 && (prefix.Length == 0 || (c.Key + "\\").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(c => c.Value);
    }

    private void Add(string folder, Child child)
    {
        if (!_children.TryGetValue(folder, out var list))
            _children[folder] = list = [];
        list.Add(child);
    }

    private static string? Parent(string path) => path.LastIndexOf('\\') is int i and > 0 ? path[..i] : null;
}

/// <summary>
/// Comprimidos dentro de Archivos, sin otro programa: se navegan como carpetas (<c>C:\fotos.zip\2024</c>),
/// se extraen con su progreso y se crean en 7z (lo que mejor comprime), zip o tar. El trabajo lo hace
/// 7z.dll en un proceso aparte (<see cref="ArchiveHost"/>); aquí se lanza, se le pasa lo elegido y se
/// leen sus respuestas. Las contraseñas se piden con el diálogo de Strata UI y se recuerdan mientras
/// Strata UI sigue abierto.
/// </summary>
public static class Archives
{
    /// <summary>Lo que se navega como carpeta (7-Zip lee más, pero estos son los que se reconocen por su extensión).</summary>
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2", ".xz", ".txz", ".zst", ".tzst", ".lz", ".tlz",
        ".lzma", ".z", ".taz", ".cab", ".iso", ".img", ".wim", ".swm", ".esd", ".arj", ".lzh", ".lha", ".cpio", ".rpm", ".deb",
        ".xar", ".dmg", ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow", ".qcow2", ".squashfs", ".apk", ".xapk", ".apks", ".ipa",
        ".nupkg", ".vsix", ".whl", ".cbz", ".cbr", ".cb7", ".cbt", ".001", ".udf", ".hfs", ".pak",
    };

    /// <summary>Imágenes de disco: con doble clic las monta Windows (como en el Explorador); se exploran desde el menú.</summary>
    private static readonly HashSet<string> DiskImages = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".img", ".vhd", ".vhdx" };

    private static readonly Dictionary<string, ArchiveListing> Listings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Passwords = new(StringComparer.OrdinalIgnoreCase);

    public static bool Available => SevenZip.Available;

    /// <summary>Se puede abrir como carpeta (por su extensión).</summary>
    public static bool IsArchive(string path) => Available && Extensions.Contains(Path.GetExtension(path));

    /// <summary>Con doble clic se entra (las imágenes de disco, en cambio, se montan).</summary>
    public static bool OpensInside(string path) => IsArchive(path) && !DiskImages.Contains(Path.GetExtension(path));

    /// <summary>
    /// Si la ruta está dentro de un comprimido ("C:\a\b.zip\fotos"): el comprimido y la carpeta de
    /// adentro ("" en su raíz). Null si es una carpeta normal. Solo mira los tramos con extensión de
    /// comprimido (no toca el disco por cada carpeta).
    /// </summary>
    public static (string Archive, string Inner)? Locate(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Available || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return null;
        string clean = path.TrimEnd('\\');
        for (int end = clean.Length; end > 3; end = clean.LastIndexOf('\\', end - 1))
        {
            string candidate = clean[..end];
            if (Extensions.Contains(Path.GetExtension(candidate)) && File.Exists(candidate))
                return (candidate, end >= clean.Length ? "" : clean[(end + 1)..]);
            if (clean.LastIndexOf('\\', end - 1) < 0)
                break;
        }
        return null;
    }

    /// <summary>El listado ya leído (sin leer el disco ni preguntar nada); null si no está o el archivo cambió.</summary>
    public static ArchiveListing? Cached(string archive)
    {
        lock (Listings)
        {
            if (!Listings.TryGetValue(archive, out var listing))
                return null;
            var info = new FileInfo(archive);
            return info.Exists && info.LastWriteTimeUtc == listing.Stamp && info.Length == listing.Length ? listing : null;
        }
    }

    /// <summary>
    /// Lee lo que hay en el comprimido (o lo toma de la memoria si no cambió). Si tiene los nombres
    /// cifrados, pide la contraseña; null si se canceló. Lanza <see cref="InvalidDataException"/> si no
    /// se puede abrir.
    /// </summary>
    public static async Task<ArchiveListing?> ListAsync(string archive, IntPtr owner, CancellationToken token = default)
    {
        if (Cached(archive) is { } cached)
            return cached;
        string password = PasswordFor(archive);
        bool retry = false;
        while (true)
        {
            var items = new List<ArchiveItem>();
            string format = "";
            var info = new FileInfo(archive);
            int code = await RunHostAsync(["list", archive], [password], line =>
            {
                if (line[0] == "K" && line.Length > 1)
                    format = line[1];
                else if (line[0] == "I" && line.Length >= 7)
                    items.Add(new ArchiveItem(int.Parse(line[1]), line[2], line[3] == "1", long.Parse(line[4]),
                                              long.Parse(line[5]) is > 0 and var time ? DateTime.FromFileTimeUtc(time).ToLocalTime() : info.LastWriteTime,
                                              line[6] == "1"));
            }, token);
            if (code == ArchiveHost.Ok)
            {
                var listing = new ArchiveListing(archive, format, items, info.LastWriteTimeUtc, info.Length);
                lock (Listings)
                    Listings[archive] = listing;
                if (password.Length > 0)
                    Passwords[archive] = password;
                return listing;
            }
            if (code != ArchiveHost.NeedsPassword)
                throw new InvalidDataException(Loc.T("No se pudo abrir el comprimido: está dañado o su formato no es compatible."));
            if (await AskPasswordAsync(archive, retry, owner) is not { } typed)
                return null;
            password = typed;
            retry = true;
        }
    }

    private static string PasswordFor(string archive) => Passwords.GetValueOrDefault(archive, "");

    /// <summary>Pide la contraseña con el diálogo de Strata UI (en el hilo de la interfaz). Null si se canceló.</summary>
    private static async Task<string?> AskPasswordAsync(string archive, bool wrong, IntPtr owner)
    {
        var ui = Application.Current?.Dispatcher;
        if (ui is null)
            return null;
        return await await ui.InvokeAsync(async () =>
        {
            var box = new PasswordBox();
            string message = wrong ? Loc.T("La contraseña no es correcta. Vuelve a intentarlo.")
                                   : Loc.F("«{0}» está protegido con contraseña.", Path.GetFileName(archive));
            int choice = await Controls.StrataDialog.ShowAsync(FileOperations.OwnerWindow(owner), Loc.T("Contraseña"), message, box, false,
                                                               Loc.T("Aceptar"), Loc.T("Cancelar"));
            return choice == 0 && box.Password.Length > 0 ? box.Password : null;
        });
    }

    // ── Extraer ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Extrae lo elegido (rutas de adentro; vacío = todo lo de <paramref name="baseFolder"/>) en una
    /// carpeta, con su tarjeta de progreso. Lo de arriba queda con el nombre que tenía (o "nombre (2)").
    /// </summary>
    public static FileTransfer Extract(string archive, IReadOnlyList<string> inner, string baseFolder, string destination,
                                       TransferConflict conflict, IntPtr owner)
    {
        var sources = inner.Count > 0 ? inner.Select(i => Path.Combine(archive, i)).ToList() : [archive];
        var transfer = new FileTransfer(TransferKind.Extract, sources, destination, conflict);
        return FileTransfers.Run(transfer, t => ExtractWorkAsync(t, archive, inner, baseFolder, owner));
    }

    private static async Task ExtractWorkAsync(FileTransfer t, string archive, IReadOnlyList<string> inner, string baseFolder, IntPtr owner)
    {
        string password = PasswordFor(archive);
        bool retry = false;
        while (true)
        {
            var results = new List<string>();
            var input = new List<string> { password, t.Conflict.ToString(), baseFolder };
            input.AddRange(inner);
            input.Add(".");
            int code = await RunHostAsync(["extract", archive, t.Folder], input, line =>
            {
                switch (line[0])
                {
                    case "P" when line.Length >= 4:
                        t.TotalBytes = long.Parse(line[2]);
                        Interlocked.Exchange(ref t.DoneBytes, long.Parse(line[1]));
                        t.CurrentName = line[3];
                        t.StateValue = TransferState.Running;
                        break;
                    case "R" when line.Length >= 2:
                        results.Add(line[1]);
                        break;
                    case "E" when line.Length >= 2:
                        t.AddError(line[1], admin: false);
                        break;
                }
            }, t.Cancellation.Token);
            if (code == ArchiveHost.NeedsPassword)
            {
                if (await AskPasswordAsync(archive, retry, owner) is not { } typed)
                {
                    t.StateValue = TransferState.Canceled;
                    return;
                }
                password = typed;
                retry = true;
                continue;
            }
            if (password.Length > 0 && code == ArchiveHost.Ok)
                Passwords[archive] = password;
            t.Result = results.FirstOrDefault();
            t.StateValue = code switch
            {
                ArchiveHost.Ok => TransferState.Done,
                ArchiveHost.Canceled => TransferState.Canceled,
                _ => TransferState.Failed,
            };
            if (code == ArchiveHost.NotArchive)
                t.AddError(Loc.T("No se pudo abrir el comprimido: está dañado o su formato no es compatible."), admin: false);
            else if (code != ArchiveHost.Ok && code != ArchiveHost.Canceled && t.Errors.Count == 0)
                t.AddError(Loc.T("No se pudo extraer."), admin: false);
            return;
        }
    }

    /// <summary>
    /// "Extraer aquí": junto al comprimido. Si todo está en una sola carpeta, esa carpeta (sin otra
    /// alrededor); si no, en una carpeta nueva con el nombre del comprimido. Devuelve la tarea (null si se canceló).
    /// </summary>
    public static async Task<FileTransfer?> ExtractHereAsync(string archive, IntPtr owner)
    {
        var listing = await ListAsync(archive, owner);
        if (listing is null)
            return null;
        string parent = Path.GetDirectoryName(archive)!;
        var top = listing.Children("");
        if (top is [{ IsFolder: true }])
            return Extract(archive, [], "", parent, TransferConflict.KeepBoth, owner);
        string name = Path.GetFileNameWithoutExtension(archive);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);
        string target = Path.Combine(parent, name);
        for (int i = 2; Directory.Exists(target) || File.Exists(target); i++)
            target = Path.Combine(parent, $"{name} ({i})");
        return Extract(archive, [], "", target, TransferConflict.KeepBoth, owner);
    }

    /// <summary>
    /// Saca lo elegido a una carpeta temporal (para abrirlo o copiarlo) y devuelve sus rutas; vacío si
    /// se canceló o falló. Sin tarjeta, salvo que tarde.
    /// </summary>
    public static async Task<List<string>> ExtractToTempAsync(string archive, IReadOnlyList<string> inner, string baseFolder, IntPtr owner)
    {
        CleanTemp();
        string folder = Path.Combine(TempRoot, "open", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        var transfer = Extract(archive, inner, baseFolder, folder, TransferConflict.Replace, owner);
        if (!await transfer.Finished.Task)
            return [];
        return inner.Select(i => Path.Combine(folder, i[(baseFolder.Trim('\\').Length > 0 ? baseFolder.Trim('\\').Length + 1 : 0)..]))
                    .Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    private static string TempRoot => Path.Combine(Path.GetTempPath(), "strataui-archives");

    /// <summary>Lo sacado a la carpeta temporal hace más de un día (ya se usó) se borra.</summary>
    private static void CleanTemp()
    {
        try
        {
            string open = Path.Combine(TempRoot, "open");
            if (!Directory.Exists(open))
                return;
            foreach (var dir in new DirectoryInfo(open).EnumerateDirectories().Where(d => d.CreationTimeUtc < DateTime.UtcNow.AddDays(-1)))
                dir.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ── Comprimir ───────────────────────────────────────────────────────────

    /// <summary>Formatos para crear: 7z comprime más; zip se abre en cualquier lado; tar solo junta.</summary>
    public static readonly string[] CreateFormats = ["7z", "zip", "tar"];

    /// <summary>"Comprimir en «nombre.7z»": 7z con la compresión máxima, junto a lo elegido.</summary>
    public static FileTransfer? CompressQuick(IReadOnlyList<string> sources, IntPtr owner)
    {
        if (sources.Count == 0)
            return null;
        return Compress(sources, FreeArchiveName(sources, "7z"), "7z", 9, "");
    }

    /// <summary>El nombre que tendría el comprimido ("fotos.7z", o el de la carpeta si son varios).</summary>
    public static string SuggestedName(IReadOnlyList<string> sources)
    {
        string first = sources[0].TrimEnd('\\');
        string name = sources.Count == 1
            ? (Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first))
            : Path.GetFileName(Path.GetDirectoryName(first)?.TrimEnd('\\') ?? "");
        return name.Length > 0 ? name : Loc.T("Comprimido");
    }

    private static string FreeArchiveName(IReadOnlyList<string> sources, string extension, string? name = null)
    {
        string folder = Path.GetDirectoryName(sources[0].TrimEnd('\\'))!;
        name ??= SuggestedName(sources);
        string target = Path.Combine(folder, $"{name}.{extension}");
        for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(folder, $"{name} ({i}).{extension}");
        return target;
    }

    /// <summary>Pregunta nombre, formato, nivel y contraseña (opcional) y comprime con su tarjeta de progreso.</summary>
    public static async Task<FileTransfer?> CompressAsync(IReadOnlyList<string> sources, IntPtr owner)
    {
        if (sources.Count == 0)
            return null;
        var name = new TextBox { Text = SuggestedName(sources), Margin = new Thickness(0, 6, 0, 14) };
        var format = new ComboBox { Margin = new Thickness(0, 6, 0, 14) };
        format.Items.Add(Loc.T("7z (comprime más)"));
        format.Items.Add(Loc.T("zip (se abre en cualquier equipo)"));
        format.Items.Add(Loc.T("tar (sin comprimir)"));
        format.SelectedIndex = 0;
        var level = new ComboBox { Margin = new Thickness(0, 6, 0, 14) };
        level.Items.Add(Loc.T("Máxima"));
        level.Items.Add(Loc.T("Normal"));
        level.Items.Add(Loc.T("Rápida"));
        level.SelectedIndex = 0;
        var password = new PasswordBox { Margin = new Thickness(0, 6, 0, 0) };
        format.SelectionChanged += (_, _) => level.IsEnabled = password.IsEnabled = format.SelectedIndex != 2;
        var panel = new StackPanel();
        foreach (var (caption, control) in new (string, FrameworkElement)[]
                 {
                     (Loc.T("Nombre"), name), (Loc.T("Formato"), format), (Loc.T("Compresión"), level),
                     (Loc.T("Contraseña (opcional)"), password),
                 })
        {
            var text = new System.Windows.Controls.TextBlock { Text = caption, FontSize = 13 };
            text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            panel.Children.Add(text);
            panel.Children.Add(control);
        }
        string what = sources.Count == 1 ? Loc.F("«{0}»", Path.GetFileName(sources[0].TrimEnd('\\'))) : Loc.F("{0} elementos", sources.Count);
        if (await Controls.StrataDialog.ShowAsync(FileOperations.OwnerWindow(owner), Loc.T("Comprimir"), Loc.F("Se comprimirá {0}.", what),
                                                  panel, false, Loc.T("Comprimir"), Loc.T("Cancelar")) != 0)
            return null;
        string chosen = CreateFormats[Math.Max(0, format.SelectedIndex)];
        string clean = string.Concat(name.Text.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c))).TrimEnd('.', ' ');
        if (clean.EndsWith("." + chosen, StringComparison.OrdinalIgnoreCase))
            clean = clean[..^(chosen.Length + 1)];
        int compression = level.SelectedIndex switch { 1 => 5, 2 => 1, _ => 9 };
        return Compress(sources, FreeArchiveName(sources, chosen, clean.Length > 0 ? clean : null), chosen, compression,
                        chosen == "tar" ? "" : password.Password);
    }

    private static FileTransfer Compress(IReadOnlyList<string> sources, string target, string format, int level, string password)
    {
        var transfer = new FileTransfer(TransferKind.Compress, sources.ToList(), target, TransferConflict.Replace);
        return FileTransfers.Run(transfer, async t =>
        {
            var input = new List<string> { password };
            input.AddRange(sources);
            input.Add(".");
            int code = await RunHostAsync(["create", target, format, level.ToString()], input, line =>
            {
                switch (line[0])
                {
                    case "P" when line.Length >= 4:
                        t.TotalBytes = long.Parse(line[2]);
                        Interlocked.Exchange(ref t.DoneBytes, long.Parse(line[1]));
                        t.CurrentName = line[3];
                        t.StateValue = TransferState.Running;
                        break;
                    case "R" when line.Length >= 2:
                        t.Result = line[1];
                        break;
                    case "E" when line.Length >= 2:
                        t.AddError(line[1], admin: false);
                        break;
                }
            }, t.Cancellation.Token);
            t.StateValue = code switch
            {
                ArchiveHost.Ok => TransferState.Done,
                ArchiveHost.Canceled => TransferState.Canceled,
                _ => TransferState.Failed,
            };
            if (t.State == TransferState.Failed && t.Errors.Count == 0)
                t.AddError(Loc.T("No se pudo crear el comprimido."), admin: false);
        });
    }

    // ── El proceso aparte ───────────────────────────────────────────────────

    /// <summary>
    /// Abre <c>StrataUI.exe --archive …</c>, le escribe <paramref name="input"/> (una línea cada uno) y
    /// pasa cada línea que contesta a <paramref name="onLine"/>. Cancelar le manda "C" (y si no
    /// termina, se cierra a la fuerza). Devuelve su código de salida.
    /// </summary>
    private static async Task<int> RunHostAsync(string[] args, IReadOnlyList<string> input, Action<string[]> onLine, CancellationToken token)
    {
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(ArchiveHost.Arg);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo abrir el proceso de los comprimidos");
        foreach (var line in input)
            await process.StandardInput.WriteLineAsync(line);
        await process.StandardInput.FlushAsync();
        await using var cancel = token.Register(() =>
        {
            try
            {
                process.StandardInput.WriteLine("C");
                process.StandardInput.Flush();
                _ = Task.Delay(5000).ContinueWith(_ =>
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                });
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
        });
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.Length > 0)
                onLine(line.Split('\t'));
        }
        // Terminó de contestar: se cierra su entrada (deja de esperar un "cancelar").
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
