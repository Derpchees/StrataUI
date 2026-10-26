using System.IO;
using System.Runtime.InteropServices;

namespace Strata.Shell.Services.Files;

/// <summary>
/// La interfaz de 7z.dll (7-Zip, LGPL) que usa el proceso de los comprimidos (<see cref="ArchiveHost"/>):
/// los formatos que trae, abrir y listar, extraer y crear. Nunca se carga en el proceso de Strata UI
/// (un comprimido dañado o malicioso que tumbe el código nativo solo tumba ese proceso). Las llamadas
/// van en un hilo MTA: los objetos de 7-Zip no están registrados y no se pueden pasar entre apartamentos.
/// </summary>
internal static class SevenZip
{
    /// <summary>Un formato de 7z.dll: nombre ("7z", "zip", "Rar5"), su clase, extensiones y firmas.</summary>
    internal sealed record Format(int Index, string Name, Guid ClassId, string[] Extensions, byte[][] Signatures, int SignatureOffset, bool CanWrite);

    private static IntPtr _library;
    private static CreateObjectFn? _createObject;
    private static List<Format>? _formats;

    private delegate int CreateObjectFn(ref Guid clsid, ref Guid iid, out IntPtr instance);
    private delegate int GetNumberOfFormatsFn(out uint count);
    private delegate int GetHandlerProperty2Fn(uint index, uint propId, ref PropVariant value);

    /// <summary>La ruta de 7z.dll: junto a StrataUI.exe.</summary>
    internal static string LibraryPath => Path.Combine(AppContext.BaseDirectory, "7z.dll");

    internal static bool Available => File.Exists(LibraryPath);

    /// <summary>Los formatos que sabe leer (y cuáles escribir) la 7z.dll instalada.</summary>
    internal static List<Format> Formats
    {
        get
        {
            if (_formats != null)
                return _formats;
            Load();
            var count = Marshal.GetDelegateForFunctionPointer<GetNumberOfFormatsFn>(NativeLibrary.GetExport(_library, "GetNumberOfFormats"));
            var property = Marshal.GetDelegateForFunctionPointer<GetHandlerProperty2Fn>(NativeLibrary.GetExport(_library, "GetHandlerProperty2"));
            var list = new List<Format>();
            count(out uint total);
            for (uint i = 0; i < total; i++)
            {
                string name = (string?)Read(property, i, HandlerName) ?? "";
                var id = Read(property, i, HandlerClassId, raw: true) as byte[];
                string extensions = (string?)Read(property, i, HandlerExtension) ?? "";
                var signatures = new List<byte[]>();
                if (Read(property, i, HandlerSignature, raw: true) is byte[] { Length: > 0 } single)
                    signatures.Add(single);
                if (Read(property, i, HandlerMultiSignature, raw: true) is byte[] { Length: > 0 } multi)
                {
                    // Varias firmas: cada una con su largo delante.
                    for (int p = 0; p < multi.Length;)
                    {
                        int length = multi[p++];
                        if (length == 0 || p + length > multi.Length)
                            break;
                        signatures.Add(multi[p..(p + length)]);
                        p += length;
                    }
                }
                int offset = Read(property, i, HandlerSignatureOffset) is uint o ? (int)o : 0;
                bool update = Read(property, i, HandlerUpdate) is true;
                if (id is { Length: 16 })
                    list.Add(new Format((int)i, name, new Guid(id), extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                                        signatures.ToArray(), offset, update));
            }
            return _formats = list;
        }
    }

    private static void Load()
    {
        if (_library != IntPtr.Zero)
            return;
        _library = NativeLibrary.Load(LibraryPath);
        _createObject = Marshal.GetDelegateForFunctionPointer<CreateObjectFn>(NativeLibrary.GetExport(_library, "CreateObject"));
    }

    private static object? Read(GetHandlerProperty2Fn property, uint index, uint id, bool raw = false)
    {
        var value = new PropVariant();
        try
        {
            if (property(index, id, ref value) != 0)
                return null;
            // El ID de la clase y las firmas vienen como BSTR con bytes crudos.
            if (raw && value.Type == VtBstr && value.Pointer != IntPtr.Zero)
            {
                int bytes = Marshal.ReadInt32(value.Pointer, -4);
                var data = new byte[bytes];
                Marshal.Copy(value.Pointer, data, 0, bytes);
                return data;
            }
            return value.Value;
        }
        finally
        {
            value.Clear();
        }
    }

    /// <summary>Crea el objeto de un formato con la interfaz pedida (IInArchive o IOutArchive).</summary>
    internal static T Create<T>(Guid classId) where T : class
    {
        Load();
        var iid = typeof(T).GUID;
        int hr = _createObject!(ref classId, ref iid, out var pointer);
        if (hr != 0 || pointer == IntPtr.Zero)
            throw new COMException("7z.dll no creó el objeto", hr);
        try
        {
            return (T)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    // ── Propiedades ─────────────────────────────────────────────────────────

    internal const uint HandlerName = 0, HandlerClassId = 1, HandlerExtension = 2, HandlerUpdate = 4,
                        HandlerSignature = 6, HandlerMultiSignature = 7, HandlerSignatureOffset = 8;

    internal const uint PropPath = 3, PropName = 4, PropIsDir = 6, PropSize = 7, PropPackSize = 8, PropAttrib = 9,
                        PropCTime = 10, PropATime = 11, PropMTime = 12, PropEncrypted = 15, PropIsAnti = 21;

    internal const ushort VtEmpty = 0, VtI4 = 3, VtBstr = 8, VtBool = 11, VtI8 = 20, VtUi8 = 21, VtUi4 = 19, VtUint = 23, VtFiletime = 64;

    /// <summary>Resultado de extraer un elemento.</summary>
    internal const int ResultOk = 0, ResultUnsupported = 1, ResultDataError = 2, ResultCrcError = 3, ResultWrongPassword = 9;

    internal const int AskExtract = 0, AskTest = 1, AskSkip = 2;

    internal const int SOk = 0, SFalse = 1, EAbort = unchecked((int)0x80004004), EFail = unchecked((int)0x80004005);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public long Long;
        [FieldOffset(8)] public uint UInt;
        [FieldOffset(8)] public short Bool;

        public readonly object? Value => Type switch
        {
            VtBstr => Marshal.PtrToStringBSTR(Pointer),
            VtBool => Bool != 0,
            VtUi4 or VtUint => UInt,
            VtI4 => (int)UInt,
            VtUi8 => (ulong)Long,
            VtI8 => Long,
            VtFiletime => Long > 0 ? DateTime.FromFileTimeUtc(Long) : null,
            _ => null,
        };

        public void Clear()
        {
            if (Type != VtEmpty)
                PropVariantClear(ref this);
            Type = VtEmpty;
            Long = 0;
        }

        public static PropVariant From(object? value)
        {
            var v = new PropVariant();
            switch (value)
            {
                case string s:
                    v.Type = VtBstr;
                    v.Pointer = Marshal.StringToBSTR(s);
                    break;
                case bool b:
                    v.Type = VtBool;
                    v.Bool = (short)(b ? -1 : 0);
                    break;
                case uint u:
                    v.Type = VtUi4;
                    v.UInt = u;
                    break;
                case ulong l:
                    v.Type = VtUi8;
                    v.Long = (long)l;
                    break;
                case DateTime t:
                    v.Type = VtFiletime;
                    v.Long = t.ToFileTimeUtc();
                    break;
            }
            return v;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}

// ── Interfaces de 7-Zip (IUnknown; el orden de los métodos es el de la DLL) ────────────────

[ComImport, Guid("23170F69-40C1-278A-0000-000000050000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProgress
{
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(IntPtr completeValue);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300010000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISequentialInStream
{
    [PreserveSig] int Read(IntPtr data, uint size, IntPtr processedSize);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300020000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISequentialOutStream
{
    [PreserveSig] int Write(IntPtr data, uint size, IntPtr processedSize);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300030000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInStream
{
    [PreserveSig] int Read(IntPtr data, uint size, IntPtr processedSize);
    [PreserveSig] int Seek(long offset, uint origin, IntPtr newPosition);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300040000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOutStream
{
    [PreserveSig] int Write(IntPtr data, uint size, IntPtr processedSize);
    [PreserveSig] int Seek(long offset, uint origin, IntPtr newPosition);
    [PreserveSig] int SetSize(ulong newSize);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600100000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveOpenCallback
{
    [PreserveSig] int SetTotal(IntPtr files, IntPtr bytes);
    [PreserveSig] int SetCompleted(IntPtr files, IntPtr bytes);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600300000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveOpenVolumeCallback
{
    [PreserveSig] int GetProperty(uint propId, IntPtr value);
    [PreserveSig] int GetStream([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Interface)] out IInStream? stream);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600200000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveExtractCallback
{
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(IntPtr completeValue);
    [PreserveSig] int GetStream(uint index, [MarshalAs(UnmanagedType.Interface)] out ISequentialOutStream? stream, int askExtractMode);
    [PreserveSig] int PrepareOperation(int askExtractMode);
    [PreserveSig] int SetOperationResult(int result);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000500100000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICryptoGetTextPassword
{
    [PreserveSig] int CryptoGetTextPassword([MarshalAs(UnmanagedType.BStr)] out string password);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000500110000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICryptoGetTextPassword2
{
    [PreserveSig] int CryptoGetTextPassword2(out int passwordIsDefined, [MarshalAs(UnmanagedType.BStr)] out string password);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600600000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInArchive
{
    [PreserveSig] int Open([MarshalAs(UnmanagedType.Interface)] IInStream stream, ref ulong maxCheckStartPosition,
                           [MarshalAs(UnmanagedType.Interface)] IArchiveOpenCallback? callback);
    [PreserveSig] int Close();
    [PreserveSig] int GetNumberOfItems(out uint count);
    [PreserveSig] int GetProperty(uint index, uint propId, ref SevenZip.PropVariant value);
    [PreserveSig] int Extract([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] uint[]? indices, uint count, int testMode,
                              [MarshalAs(UnmanagedType.Interface)] IArchiveExtractCallback callback);
    [PreserveSig] int GetArchiveProperty(uint propId, ref SevenZip.PropVariant value);
    [PreserveSig] int GetNumberOfProperties(out uint count);
    [PreserveSig] int GetPropertyInfo(uint index, [MarshalAs(UnmanagedType.BStr)] out string name, out uint propId, out ushort varType);
    [PreserveSig] int GetNumberOfArchiveProperties(out uint count);
    [PreserveSig] int GetArchivePropertyInfo(uint index, [MarshalAs(UnmanagedType.BStr)] out string name, out uint propId, out ushort varType);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600800000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IArchiveUpdateCallback
{
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(IntPtr completeValue);
    [PreserveSig] int GetUpdateItemInfo(uint index, IntPtr newData, IntPtr newProperties, IntPtr indexInArchive);
    [PreserveSig] int GetProperty(uint index, uint propId, IntPtr value);
    [PreserveSig] int GetStream(uint index, [MarshalAs(UnmanagedType.Interface)] out ISequentialInStream? stream);
    [PreserveSig] int SetOperationResult(int result);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600A00000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOutArchive
{
    [PreserveSig] int UpdateItems([MarshalAs(UnmanagedType.Interface)] ISequentialOutStream outStream, uint count,
                                  [MarshalAs(UnmanagedType.Interface)] IArchiveUpdateCallback callback);
    [PreserveSig] int GetFileTimeType(out uint type);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600030000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISetProperties
{
    [PreserveSig] int SetProperties([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names, IntPtr values, uint count);
}

// ── Flujos sobre archivos de .NET ───────────────────────────────────────────────────────

/// <summary>Un archivo para leer (el comprimido, sus volúmenes o lo que se comprime).</summary>
[ClassInterface(ClassInterfaceType.None)]
internal sealed class InStream(Stream stream) : IInStream, ISequentialInStream, IDisposable
{
    public Stream Stream { get; private set; } = stream;

    /// <summary>
    /// Se cierra el archivo al llegar al final (lo que se comprime: no quedan miles abiertos) y se
    /// vuelve a abrir si 7-Zip lo pide otra vez (el zip relee un archivo que no conviene comprimir).
    /// </summary>
    public Func<Stream>? Reopen { get; init; }
    private bool _closed;

    private void EnsureOpen()
    {
        if (_closed && Reopen != null)
        {
            Stream = Reopen();
            _closed = false;
        }
    }

    public unsafe int Read(IntPtr data, uint size, IntPtr processedSize)
    {
        int read = 0;
        try
        {
            EnsureOpen();
            // Read puede devolver menos de lo pedido: 7-Zip vuelve a pedir lo que falta.
            if (!_closed)
                read = Stream.Read(new Span<byte>((void*)data, (int)size));
            if (read == 0 && size > 0 && Reopen != null && !_closed)
            {
                _closed = true;
                Stream.Dispose();
            }
            if (processedSize != IntPtr.Zero)
                Marshal.WriteInt32(processedSize, read);
            return SevenZip.SOk;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            if (processedSize != IntPtr.Zero)
                Marshal.WriteInt32(processedSize, read);
            return SevenZip.EFail;
        }
    }

    public int Seek(long offset, uint origin, IntPtr newPosition)
    {
        try
        {
            long position;
            if (_closed && Reopen != null && origin == (uint)SeekOrigin.Current && offset == 0)
            {
                position = new FileInfo(((FileStream)Stream).Name).Length; // sigue al final, sin reabrir
            }
            else
            {
                EnsureOpen();
                position = Stream.Seek(offset, (SeekOrigin)origin);
            }
            if (newPosition != IntPtr.Zero)
                Marshal.WriteInt64(newPosition, position);
            return SevenZip.SOk;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ObjectDisposedException or UnauthorizedAccessException)
        {
            return SevenZip.EFail;
        }
    }

    public void Dispose()
    {
        _closed = true;
        Stream.Dispose();
    }
}

/// <summary>Un archivo para escribir (lo que se extrae o el comprimido nuevo); cuenta lo escrito.</summary>
[ClassInterface(ClassInterfaceType.None)]
internal sealed class OutStream(Stream stream, Action<long>? written = null) : IOutStream, ISequentialOutStream, IDisposable
{
    public Stream Stream { get; } = stream;
    public volatile bool Cancelled;

    public unsafe int Write(IntPtr data, uint size, IntPtr processedSize)
    {
        if (Cancelled)
            return SevenZip.EAbort;
        try
        {
            Stream.Write(new ReadOnlySpan<byte>((void*)data, (int)size));
            if (processedSize != IntPtr.Zero)
                Marshal.WriteInt32(processedSize, (int)size);
            written?.Invoke(size);
            return SevenZip.SOk;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (processedSize != IntPtr.Zero)
                Marshal.WriteInt32(processedSize, 0);
            return SevenZip.EFail;
        }
    }

    public int Seek(long offset, uint origin, IntPtr newPosition)
    {
        try
        {
            long position = Stream.Seek(offset, (SeekOrigin)origin);
            if (newPosition != IntPtr.Zero)
                Marshal.WriteInt64(newPosition, position);
            return SevenZip.SOk;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return SevenZip.EFail;
        }
    }

    public int SetSize(ulong newSize)
    {
        try
        {
            Stream.SetLength((long)newSize);
            return SevenZip.SOk;
        }
        catch (IOException)
        {
            return SevenZip.EFail;
        }
    }

    public void Dispose() => Stream.Dispose();
}
