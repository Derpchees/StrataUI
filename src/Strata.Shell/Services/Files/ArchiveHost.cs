using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Strata.Shell.Services.Files;

/// <summary>
/// El proceso de los comprimidos (<c>StrataUI.exe --archive list|extract|create …</c>), con 7z.dll
/// (<see cref="SevenZip"/>). Recibe la contraseña y lo elegido por la entrada estándar y contesta por
/// la salida (UTF-8), una línea por dato: elemento (I), progreso (P), error (E), resultado (R). "C"
/// por la entrada cancela. Código de salida: 0 bien, 1 error, 3 falta la contraseña o es incorrecta,
/// 4 no es un comprimido que se pueda abrir, 5 cancelado.
/// </summary>
public static class ArchiveHost
{
    public const string Arg = "--archive";
    internal const int Ok = 0, Failed = 1, NeedsPassword = 3, NotArchive = 4, Canceled = 5;

    /// <summary>Formatos que solo comprimen un archivo: si adentro hay un .tar, se abre el tar (.tar.gz, .tgz…).</summary>
    private static readonly HashSet<string> StreamFormats = new(StringComparer.OrdinalIgnoreCase) { "gzip", "bzip2", "xz", "zstd", "lzma", "lzip", "Z", "lz4", "brotli" };

    private static TextWriter _out = TextWriter.Null;
    private static volatile bool _cancel;

    internal static int Run(string[] args)
    {
        if (args.Length < 3 || !SevenZip.Available)
            return Failed;
        int code = Failed;
        // Los objetos de 7-Zip no están registrados: todo en un mismo hilo MTA.
        var thread = new Thread(() => code = Dispatch(args)) { IsBackground = false };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return code;
    }

    private static int Dispatch(string[] args)
    {
        var utf8 = new UTF8Encoding(false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8, true);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        _out = TextWriter.Synchronized(output);
        try
        {
            string password = input.ReadLine() ?? "";
            switch (args[1])
            {
                case "list" when args.Length == 3:
                    return List(args[2], password);
                case "extract" when args.Length == 4:
                {
                    var conflict = Enum.TryParse<TransferConflict>(input.ReadLine(), out var c) ? c : TransferConflict.KeepBoth;
                    string baseFolder = input.ReadLine() ?? "";
                    var selection = ReadUntilDot(input);
                    ListenForCancel(input);
                    return Extract(args[2], args[3], password, conflict, baseFolder, selection);
                }
                case "create" when args.Length == 5:
                {
                    var sources = ReadUntilDot(input);
                    ListenForCancel(input);
                    return Create(args[2], args[3], int.TryParse(args[4], out int level) ? Math.Clamp(level, 0, 9) : 9, password, sources);
                }
            }
            return Failed;
        }
        catch (Exception ex)
        {
            Log.Error($"Comprimidos: falló {string.Join(' ', args.Skip(1).Take(1))}", ex);
            Send("E", ex.Message);
            return Failed;
        }
    }

    private static List<string> ReadUntilDot(StreamReader input)
    {
        var lines = new List<string>();
        while (input.ReadLine() is { } line && line != ".")
            lines.Add(line);
        return lines;
    }

    private static void ListenForCancel(StreamReader input) =>
        new Thread(() =>
        {
            try
            {
                while (input.ReadLine() is { } line)
                    if (line == "C")
                        _cancel = true;
            }
            catch (IOException)
            {
            }
            _cancel = true; // se cerró Strata UI
        }) { IsBackground = true }.Start();

    private static void Send(params object[] fields) =>
        _out.WriteLine(string.Join('\t', fields.Select(f => (f?.ToString() ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '))));

    // ── Abrir ───────────────────────────────────────────────────────────────

    /// <summary>Un comprimido abierto: su formato, el objeto de 7-Zip y lo que hay que cerrar.</summary>
    private sealed class Opened : IDisposable
    {
        public required IInArchive Archive { get; init; }
        public required SevenZip.Format Format { get; init; }
        public required OpenCallback Callback { get; init; }
        public required InStream Stream { get; init; }
        public required string Path { get; init; }
        public Opened? Outer { get; init; }

        public void Dispose()
        {
            Archive.Close();
            Marshal.FinalReleaseComObject(Archive);
            Stream.Dispose();
            Callback.Dispose();
            Outer?.Dispose();
        }
    }

    /// <summary>
    /// Prueba los formatos que corresponden a la extensión, después los que reconocen su firma y al
    /// final todos (un .exe autoextraíble, una extensión cambiada). Null si ninguno lo abre;
    /// <paramref name="needsPassword"/> si se pidió una contraseña que no había (o no sirvió).
    /// </summary>
    private static Opened? Open(string path, string password, out bool needsPassword, bool nested = true)
    {
        needsPassword = false;
        var head = new byte[4096];
        int headLength;
        using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            headLength = probe.Read(head);
        string name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        var formats = SevenZip.Formats;
        var byExtension = formats.Where(f => f.Extensions.Any(e => name.EndsWith("." + e.ToLowerInvariant(), StringComparison.Ordinal)));
        var bySignature = formats.Where(f => f.Signatures.Any(s => Matches(head, headLength, f.SignatureOffset, s)));
        foreach (var format in byExtension.Concat(bySignature).Concat(formats).Distinct())
        {
            var stream = new InStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var callback = new OpenCallback(path, password);
            IInArchive? archive = null;
            try
            {
                archive = SevenZip.Create<IInArchive>(format.ClassId);
                ulong maxCheck = 1 << 22;
                if (archive.Open(stream, ref maxCheck, callback) == SevenZip.SOk)
                {
                    var opened = new Opened { Archive = archive, Format = format, Callback = callback, Stream = stream, Path = path };
                    return nested ? OpenNestedTar(opened, password) : opened;
                }
            }
            catch (COMException)
            {
            }
            if (callback.PasswordAsked)
                needsPassword = true;
            if (archive != null)
            {
                archive.Close();
                Marshal.FinalReleaseComObject(archive);
            }
            stream.Dispose();
            callback.Dispose();
            // Pidió contraseña: ese era el formato; no tiene sentido probar los demás.
            if (needsPassword)
                return null;
        }
        return null;
    }

    private static bool Matches(byte[] head, int length, int offset, byte[] signature) =>
        offset + signature.Length <= length && head.AsSpan(offset, signature.Length).SequenceEqual(signature);

    /// <summary>
    /// Un .tar.gz (o .tgz, .tar.xz…) se muestra como el tar que tiene adentro: el tar se extrae una vez
    /// a la carpeta temporal (por ruta, tamaño y fecha) y se abre ese.
    /// </summary>
    private static Opened OpenNestedTar(Opened opened, string password)
    {
        if (!StreamFormats.Contains(opened.Format.Name) || opened.Archive.GetNumberOfItems(out uint count) != 0 || count != 1)
            return opened;
        string inner = ItemPath(opened, 0);
        if (!inner.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            return opened;
        var file = new FileInfo(opened.Path);
        string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            Encoding.UTF8.GetBytes($"{file.FullName.ToLowerInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}")))[..16];
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "strataui-archives", "tar");
        Directory.CreateDirectory(folder);
        string tar = System.IO.Path.Combine(folder, key + ".tar");
        if (!File.Exists(tar))
        {
            string part = tar + ".part";
            var callback = new SingleExtract(part);
            int hr = opened.Archive.Extract([0], 1, 0, callback);
            callback.Close();
            if (hr != SevenZip.SOk || callback.Result != SevenZip.ResultOk)
            {
                TryDelete(part);
                return opened;
            }
            File.Move(part, tar, overwrite: true);
        }
        var tarOpened = Open(tar, password, out _, nested: false);
        if (tarOpened is null)
            return opened;
        return new Opened
        {
            Archive = tarOpened.Archive, Format = tarOpened.Format, Callback = tarOpened.Callback,
            Stream = tarOpened.Stream, Path = tarOpened.Path, Outer = opened,
        };
    }

    /// <summary>La ruta de un elemento, con "\" y sin nada que salga de la carpeta (sin unidad, sin "..").</summary>
    private static string ItemPath(Opened opened, uint index)
    {
        var value = new SevenZip.PropVariant();
        string path = "";
        try
        {
            if (opened.Archive.GetProperty(index, SevenZip.PropPath, ref value) == 0 && value.Value is string p)
                path = p;
        }
        finally
        {
            value.Clear();
        }
        if (path.Length == 0)
        {
            // Un .gz de un solo archivo no guarda el nombre: el del comprimido sin su extensión.
            path = System.IO.Path.GetFileNameWithoutExtension(opened.Outer?.Path ?? opened.Path);
            if (opened.Format.Name.Equals("tar", StringComparison.OrdinalIgnoreCase) || path.Length == 0)
                path = "item" + index;
        }
        return CleanPath(path);
    }

    internal static string CleanPath(string path)
    {
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries)
                        .Where(part => part is not ("." or ".."))
                        .Select(part => new string(part.Select(c => c is ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? '_' : c).ToArray()).TrimEnd('.', ' '))
                        .Where(part => part.Length > 0);
        return string.Join('\\', parts);
    }

    private static object? Property(Opened opened, uint index, uint id)
    {
        var value = new SevenZip.PropVariant();
        try
        {
            return opened.Archive.GetProperty(index, id, ref value) == 0 ? value.Value : null;
        }
        finally
        {
            value.Clear();
        }
    }

    private sealed record Item(uint Index, string Path, bool IsFolder, long Size, DateTime? Modified, bool Encrypted, uint? Attributes);

    private static List<Item> Items(Opened opened)
    {
        var items = new List<Item>();
        if (opened.Archive.GetNumberOfItems(out uint count) != 0)
            return items;
        for (uint i = 0; i < count; i++)
        {
            if (Property(opened, i, SevenZip.PropIsAnti) is true)
                continue;
            string path = ItemPath(opened, i);
            if (path.Length == 0)
                continue;
            items.Add(new Item(i, path, Property(opened, i, SevenZip.PropIsDir) is true,
                               Property(opened, i, SevenZip.PropSize) is ulong size ? (long)size : 0,
                               Property(opened, i, SevenZip.PropMTime) as DateTime?,
                               Property(opened, i, SevenZip.PropEncrypted) is true,
                               Property(opened, i, SevenZip.PropAttrib) as uint?));
        }
        return items;
    }

    // ── Listar ──────────────────────────────────────────────────────────────

    private static int List(string path, string password)
    {
        using var opened = Open(path, password, out bool needsPassword);
        if (opened is null)
            return needsPassword ? NeedsPassword : NotArchive;
        Send("K", opened.Format.Name);
        foreach (var item in Items(opened))
            Send("I", item.Index, item.Path, item.IsFolder ? 1 : 0, item.Size, item.Modified?.ToFileTimeUtc() ?? 0, item.Encrypted ? 1 : 0);
        return Ok;
    }

    // ── Extraer ─────────────────────────────────────────────────────────────

    private static int Extract(string path, string destination, string password, TransferConflict conflict, string baseFolder, List<string> selection)
    {
        using var opened = Open(path, password, out bool needsPassword);
        if (opened is null)
            return needsPassword ? NeedsPassword : NotArchive;
        destination = System.IO.Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);
        baseFolder = CleanPath(baseFolder);
        var chosen = selection.Select(CleanPath).Where(s => s.Length > 0).ToList();
        var items = Items(opened).Where(item => chosen.Count == 0
            ? baseFolder.Length == 0 || item.Path.StartsWith(baseFolder + "\\", StringComparison.OrdinalIgnoreCase)
            : chosen.Any(s => item.Path.Equals(s, StringComparison.OrdinalIgnoreCase) || item.Path.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase))).ToList();

        // Dónde va cada uno: relativo a la carpeta que se veía; con "conservar ambos", lo de arriba que
        // ya existe pasa a "nombre (2)".
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<uint, (string Path, Item Item)>();
        foreach (var item in items)
        {
            string relative = baseFolder.Length > 0 && item.Path.StartsWith(baseFolder + "\\", StringComparison.OrdinalIgnoreCase)
                ? item.Path[(baseFolder.Length + 1)..] : item.Path;
            string top = relative.Split('\\')[0];
            if (!renamed.TryGetValue(top, out var topName))
            {
                topName = top;
                bool topIsFolder = relative.Contains('\\') || item.IsFolder;
                if (conflict == TransferConflict.KeepBoth)
                    topName = System.IO.Path.GetFileName(FreeName(System.IO.Path.Combine(destination, top), topIsFolder));
                renamed[top] = topName;
                Send("R", System.IO.Path.Combine(destination, topName));
            }
            string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(destination, topName + relative[top.Length..]));
            // Nada fuera de la carpeta elegida, aunque el comprimido lo intente.
            if (!target.StartsWith(destination.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                continue;
            targets[item.Index] = (target, item);
        }
        if (targets.Count == 0)
            return Ok;
        var callback = new ExtractCallback(targets, password);
        int hr = opened.Archive.Extract(targets.Keys.Order().ToArray(), (uint)targets.Count, 0, callback);
        callback.Close();
        if (_cancel)
            return Canceled;
        if (callback.WrongPassword || callback.PasswordMissing)
            return NeedsPassword;
        foreach (var error in callback.Errors.Take(20))
            Send("E", error);
        return hr == SevenZip.SOk && callback.Errors.Count == 0 ? Ok : Failed;
    }

    private static string FreeName(string path, bool folder)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;
        string parent = System.IO.Path.GetDirectoryName(path)!;
        string stem = folder ? System.IO.Path.GetFileName(path) : System.IO.Path.GetFileNameWithoutExtension(path);
        string extension = folder ? "" : System.IO.Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = System.IO.Path.Combine(parent, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ── Comprimir ───────────────────────────────────────────────────────────

    private static int Create(string archivePath, string formatName, int level, string password, List<string> sources)
    {
        var format = SevenZip.Formats.FirstOrDefault(f => f.Name.Equals(formatName, StringComparison.OrdinalIgnoreCase) && f.CanWrite);
        if (format is null || sources.Count == 0 || !Path.IsPathFullyQualified(archivePath))
            return Failed;
        var entries = new List<UpdateCallback.Entry>();
        foreach (var source in sources.Select(s => s.TrimEnd('\\')))
        {
            string name = System.IO.Path.GetFileName(source);
            if (Directory.Exists(source))
            {
                entries.Add(new UpdateCallback.Entry(name, new DirectoryInfo(source)));
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var info in new DirectoryInfo(source).EnumerateFileSystemInfos("*", options))
                    entries.Add(new UpdateCallback.Entry(System.IO.Path.Combine(name, System.IO.Path.GetRelativePath(source, info.FullName)), info));
            }
            else if (File.Exists(source))
            {
                entries.Add(new UpdateCallback.Entry(name, new FileInfo(source)));
            }
        }
        string part = archivePath + ".strata-part";
        var archive = SevenZip.Create<IOutArchive>(format.ClassId);
        try
        {
            SetProperties(archive, format.Name, level, password.Length > 0);
            var callback = new UpdateCallback(entries, password);
            int hr;
            using (var output = new OutStream(new FileStream(part, FileMode.Create, FileAccess.ReadWrite, FileShare.None)))
                hr = archive.UpdateItems(output, (uint)entries.Count, callback);
            callback.Close();
            if (_cancel || hr != SevenZip.SOk)
            {
                if (!_cancel)
                {
                    Log.Info($"Comprimidos: 7-Zip no pudo crear {Path.GetFileName(archivePath)} (0x{hr:X8})");
                    Send("E", callback.Errors.FirstOrDefault() ?? $"0x{hr:X8}");
                }
                TryDelete(part);
                return _cancel ? Canceled : Failed;
            }
            File.Move(part, archivePath, overwrite: true);
            foreach (var error in callback.Errors.Take(20))
                Send("E", error);
            Send("R", archivePath);
            return callback.Errors.Count == 0 ? Ok : Failed;
        }
        finally
        {
            Marshal.FinalReleaseComObject(archive);
        }
    }

    /// <summary>Nivel (0–9), varios hilos y, con contraseña, AES-256 (en 7z también los nombres).</summary>
    private static void SetProperties(IOutArchive archive, string format, int level, bool encrypted)
    {
        if (archive is not ISetProperties setter)
            return;
        var properties = new List<(string Name, object Value)>();
        if (!format.Equals("tar", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add(("x", (uint)level));
            properties.Add(("mt", "on"));
        }
        if (encrypted && format.Equals("7z", StringComparison.OrdinalIgnoreCase))
            properties.Add(("he", "on"));
        if (encrypted && format.Equals("zip", StringComparison.OrdinalIgnoreCase))
            properties.Add(("em", "AES256"));
        if (properties.Count == 0)
            return;
        int size = Marshal.SizeOf<SevenZip.PropVariant>();
        IntPtr values = Marshal.AllocHGlobal(size * properties.Count);
        var variants = properties.Select(p => SevenZip.PropVariant.From(p.Value)).ToArray();
        try
        {
            for (int i = 0; i < variants.Length; i++)
                Marshal.StructureToPtr(variants[i], values + i * size, false);
            setter.SetProperties(properties.Select(p => p.Name).ToArray(), values, (uint)properties.Count);
        }
        finally
        {
            foreach (var variant in variants)
                variant.Clear();
            Marshal.FreeHGlobal(values);
        }
    }

    // ── Callbacks ───────────────────────────────────────────────────────────

    /// <summary>Al abrir: la contraseña (comprimidos con los nombres cifrados) y los volúmenes (.001, .part2.rar).</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class OpenCallback(string path, string password) : IArchiveOpenCallback, ICryptoGetTextPassword, IArchiveOpenVolumeCallback, IDisposable
    {
        private readonly List<InStream> _volumes = [];
        public bool PasswordAsked { get; private set; }

        public int SetTotal(IntPtr files, IntPtr bytes) => SevenZip.SOk;
        public int SetCompleted(IntPtr files, IntPtr bytes) => _cancel ? SevenZip.EAbort : SevenZip.SOk;

        public int CryptoGetTextPassword(out string text)
        {
            PasswordAsked = true;
            text = password;
            return password.Length > 0 ? SevenZip.SOk : SevenZip.EAbort;
        }

        public int GetProperty(uint propId, IntPtr value)
        {
            var variant = propId == SevenZip.PropName ? SevenZip.PropVariant.From(System.IO.Path.GetFileName(path)) : new SevenZip.PropVariant();
            Marshal.StructureToPtr(variant, value, false);
            return SevenZip.SOk;
        }

        public int GetStream(string name, out IInStream? stream)
        {
            stream = null;
            // Solo volúmenes de la misma carpeta (un nombre, sin rutas).
            string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileName(name));
            if (!File.Exists(file))
                return SevenZip.SFalse;
            var volume = new InStream(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            _volumes.Add(volume);
            stream = volume;
            return SevenZip.SOk;
        }

        public void Dispose()
        {
            foreach (var volume in _volumes)
                volume.Dispose();
        }
    }

    /// <summary>Extrae un solo elemento a un archivo (el tar de un .tar.gz).</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class SingleExtract(string file) : IArchiveExtractCallback
    {
        private OutStream? _stream;
        public int Result { get; private set; } = -1;

        public int SetTotal(ulong total) => SevenZip.SOk;
        public int SetCompleted(IntPtr completeValue) => _cancel ? SevenZip.EAbort : SevenZip.SOk;

        public int GetStream(uint index, out ISequentialOutStream? stream, int askExtractMode)
        {
            stream = null;
            if (askExtractMode != SevenZip.AskExtract)
                return SevenZip.SOk;
            _stream = new OutStream(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None));
            stream = _stream;
            return SevenZip.SOk;
        }

        public int PrepareOperation(int askExtractMode) => SevenZip.SOk;

        public int SetOperationResult(int result)
        {
            Result = result;
            Close();
            return SevenZip.SOk;
        }

        public void Close()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    /// <summary>Extrae lo elegido a sus rutas, con progreso y la contraseña.</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ExtractCallback(Dictionary<uint, (string Path, Item Item)> targets, string password)
        : IArchiveExtractCallback, ICryptoGetTextPassword
    {
        private OutStream? _stream;
        private (string Path, Item Item)? _current;
        private ulong _total;
        private long _lastReport;
        public List<string> Errors { get; } = [];
        public bool WrongPassword { get; private set; }
        public bool PasswordMissing { get; private set; }

        public int SetTotal(ulong total)
        {
            _total = total;
            return SevenZip.SOk;
        }

        public int SetCompleted(IntPtr completeValue)
        {
            if (_cancel)
                return SevenZip.EAbort;
            long now = Environment.TickCount64;
            if (completeValue != IntPtr.Zero && now - _lastReport > 150)
            {
                _lastReport = now;
                Send("P", Marshal.ReadInt64(completeValue), _total, _current is { } c ? System.IO.Path.GetFileName(c.Path) : "");
            }
            return SevenZip.SOk;
        }

        public int CryptoGetTextPassword(out string text)
        {
            text = password;
            if (password.Length > 0)
                return SevenZip.SOk;
            PasswordMissing = true;
            return SevenZip.EAbort;
        }

        public int GetStream(uint index, out ISequentialOutStream? stream, int askExtractMode)
        {
            stream = null;
            if (askExtractMode != SevenZip.AskExtract || !targets.TryGetValue(index, out var target))
                return SevenZip.SOk;
            _current = target;
            try
            {
                if (target.Item.IsFolder)
                {
                    Directory.CreateDirectory(target.Path);
                    return SevenZip.SOk;
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target.Path)!);
                if (File.Exists(target.Path))
                {
                    var attributes = File.GetAttributes(target.Path);
                    if (attributes.HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(target.Path, attributes & ~FileAttributes.ReadOnly);
                }
                _stream = new OutStream(new FileStream(target.Path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16));
                stream = _stream;
                return SevenZip.SOk;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Errors.Add($"{System.IO.Path.GetFileName(target.Path)}: {ex.Message}");
                _current = null;
                return SevenZip.SOk; // se salta ese y sigue
            }
        }

        public int PrepareOperation(int askExtractMode) => SevenZip.SOk;

        public int SetOperationResult(int result)
        {
            Close();
            if (_current is not { } current)
                return SevenZip.SOk;
            _current = null;
            if (result == SevenZip.ResultOk)
            {
                try
                {
                    if (current.Item.Modified is { } modified)
                    {
                        if (current.Item.IsFolder)
                            Directory.SetLastWriteTimeUtc(current.Path, modified);
                        else
                            File.SetLastWriteTimeUtc(current.Path, modified);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
                return SevenZip.SOk;
            }
            if (!current.Item.IsFolder)
                TryDelete(current.Path);
            // Con cifrado, una contraseña equivocada se ve como datos dañados.
            if (result == SevenZip.ResultWrongPassword || (current.Item.Encrypted && result is SevenZip.ResultDataError or SevenZip.ResultCrcError))
                WrongPassword = true;
            else
                Errors.Add(result switch
                {
                    SevenZip.ResultUnsupported => $"{current.Item.Path}: método de compresión no admitido",
                    SevenZip.ResultCrcError => $"{current.Item.Path}: error de CRC (el comprimido está dañado)",
                    _ => $"{current.Item.Path}: el comprimido está dañado ({result})",
                });
            return SevenZip.SOk;
        }

        public void Close()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    /// <summary>Lo que se comprime: rutas relativas, datos de cada archivo, progreso y contraseña.</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class UpdateCallback(List<UpdateCallback.Entry> entries, string password) : IArchiveUpdateCallback, ICryptoGetTextPassword2
    {
        internal sealed record Entry(string Path, FileSystemInfo Info);

        // En zip, 7-Zip comprime varios archivos a la vez: cada uno se cierra al terminar de leerse.
        private readonly List<InStream> _streams = [];
        private ulong _total;
        private long _lastReport;
        private volatile string _currentName = "";
        public List<string> Errors { get; } = [];

        public int SetTotal(ulong total)
        {
            _total = total;
            return SevenZip.SOk;
        }

        public int SetCompleted(IntPtr completeValue)
        {
            if (_cancel)
                return SevenZip.EAbort;
            long now = Environment.TickCount64;
            if (completeValue != IntPtr.Zero && now - _lastReport > 150)
            {
                _lastReport = now;
                Send("P", Marshal.ReadInt64(completeValue), _total, _currentName);
            }
            return SevenZip.SOk;
        }

        public int GetUpdateItemInfo(uint index, IntPtr newData, IntPtr newProperties, IntPtr indexInArchive)
        {
            if (newData != IntPtr.Zero)
                Marshal.WriteInt32(newData, 1);
            if (newProperties != IntPtr.Zero)
                Marshal.WriteInt32(newProperties, 1);
            if (indexInArchive != IntPtr.Zero)
                Marshal.WriteInt32(indexInArchive, -1);
            return SevenZip.SOk;
        }

        public int GetProperty(uint index, uint propId, IntPtr value)
        {
            var entry = entries[(int)index];
            bool folder = entry.Info is DirectoryInfo;
            object? data = propId switch
            {
                SevenZip.PropPath => entry.Path,
                SevenZip.PropIsDir => folder,
                SevenZip.PropSize => folder ? 0UL : (ulong)((FileInfo)entry.Info).Length,
                SevenZip.PropAttrib => (uint)entry.Info.Attributes,
                SevenZip.PropMTime => entry.Info.LastWriteTimeUtc,
                SevenZip.PropCTime => entry.Info.CreationTimeUtc,
                SevenZip.PropATime => entry.Info.LastAccessTimeUtc,
                SevenZip.PropIsAnti => false,
                _ => null,
            };
            Marshal.StructureToPtr(SevenZip.PropVariant.From(data), value, false);
            return SevenZip.SOk;
        }

        public int GetStream(uint index, out ISequentialInStream? stream)
        {
            stream = null;
            var entry = entries[(int)index];
            if (entry.Info is DirectoryInfo)
                return SevenZip.SOk;
            _currentName = entry.Info.Name;
            try
            {
                Stream OpenFile() => new FileStream(entry.Info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                var input = new InStream(OpenFile()) { Reopen = OpenFile };
                lock (_streams)
                    _streams.Add(input);
                stream = input;
                return SevenZip.SOk;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (Errors)
                    Errors.Add($"{entry.Path}: {ex.Message}");
                return SevenZip.SFalse; // se salta ese archivo
            }
        }

        public int SetOperationResult(int result) => SevenZip.SOk;

        public int CryptoGetTextPassword2(out int passwordIsDefined, out string text)
        {
            passwordIsDefined = password.Length > 0 ? 1 : 0;
            text = password;
            return SevenZip.SOk;
        }

        public void Close()
        {
            lock (_streams)
            {
                foreach (var input in _streams)
                    input.Dispose();
                _streams.Clear();
            }
        }
    }
}
