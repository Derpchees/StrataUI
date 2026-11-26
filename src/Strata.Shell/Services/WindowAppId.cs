using System.Runtime.InteropServices;

namespace Strata.Shell.Services;

/// <summary>
/// Identidad propia de una ventana (AppUserModelID): su botón en la barra no se agrupa con el resto
/// de Strata UI y lleva el ícono de la ventana. La usan las ventanas de los módulos (Archivos, Bloc
/// de notas).
/// </summary>
public static class WindowAppId
{
    public static void Apply(IntPtr hwnd, string appId)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); // IPropertyStore
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) < 0 || store is not IPropertyStore properties)
            return;
        var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
        var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(appId) }; // VT_LPWSTR
        try
        {
            properties.SetValue(ref key, ref value);
            properties.Commit();
        }
        finally
        {
            PropVariantClear(ref value);
            Marshal.ReleaseComObject(properties);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
