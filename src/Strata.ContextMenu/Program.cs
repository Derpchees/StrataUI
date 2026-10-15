using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Strata.ContextMenu;

/// <summary>
/// Servidor COM local: el Explorador lo lanza con "-Embedding" cuando arma el menú contextual,
/// pide el comando (<see cref="PinCommand"/>) y lo usa. Se cierra solo tras un rato sin uso; el
/// siguiente clic derecho lo vuelve a lanzar.
/// </summary>
internal static class Program
{
    /// <summary>CLSID del comando; el mismo que declara installer/context-menu/AppxManifest.xml.</summary>
    internal const string Clsid = "C5A1E7F2-3B84-4D29-9E6A-2F7B1C8D4E53";

    private static readonly TimeSpan IdleExit = TimeSpan.FromMinutes(2);
    internal static long LastUse = Environment.TickCount64;

    [MTAThread]
    private static int Main(string[] args)
    {
        if (!args.Any(a => a.Equals("-Embedding", StringComparison.OrdinalIgnoreCase) || a.Equals("/Embedding", StringComparison.OrdinalIgnoreCase)))
            return 1; // solo lo lanza COM

        var clsid = new Guid(Clsid);
        int hr = CoRegisterClassObject(ref clsid, new CommandFactory(), ClsctxLocalServer, RegclsMultipleUse, out uint cookie);
        if (hr < 0)
            return hr;
        try
        {
            // Las llamadas llegan por hilos de RPC (MTA); este solo espera a quedar sin uso.
            while (Environment.TickCount64 - Interlocked.Read(ref LastUse) < IdleExit.TotalMilliseconds)
                Thread.Sleep(5000);
        }
        finally
        {
            CoRevokeClassObject(cookie);
        }
        return 0;
    }

    private const uint ClsctxLocalServer = 4, RegclsMultipleUse = 1;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint cookie);
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, ref Guid riid, out IntPtr instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockIt);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class CommandFactory : IClassFactory
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    public int CreateInstance(IntPtr outer, ref Guid riid, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero)
            return ClassENoAggregation;
        Interlocked.Exchange(ref Program.LastUse, Environment.TickCount64);
        IntPtr unknown = Marshal.GetIUnknownForObject(new PinCommand());
        try
        {
            return Marshal.QueryInterface(unknown, in riid, out instance);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public int LockServer(bool lockIt) => 0;
}

[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray? items, out IntPtr name);
    [PreserveSig] int GetIcon(IShellItemArray? items, out IntPtr icon);
    [PreserveSig] int GetToolTip(IShellItemArray? items, out IntPtr tip);
    [PreserveSig] int GetCanonicalName(out Guid name);
    [PreserveSig] int GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
    [PreserveSig] int Invoke(IShellItemArray? items, IntPtr bindContext);
    [PreserveSig] int GetFlags(out uint flags);
    [PreserveSig] int EnumSubCommands(out IntPtr commands);
}

[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
    void GetPropertyStore(int flags, ref Guid riid, out IntPtr result);
    void GetPropertyDescriptionList(IntPtr key, ref Guid riid, out IntPtr result);
    void GetAttributes(int flags, uint mask, out uint attributes);
    void GetCount(out uint count);
    void GetItemAt(uint index, out IShellItem item);
    void EnumItems(out IntPtr items);
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
    void GetParent(out IShellItem parent);
    [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
}

/// <summary>
/// "Anclar a Inicio de Strata UI". El título (ya traducido), si está activado y dónde está Strata UI
/// los escribe Strata UI en HKCU\Software\StrataUI\ContextMenu (Services/StartMenu/ExplorerMenu).
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None), Guid(Program.Clsid)]
internal sealed class PinCommand : IExplorerCommand
{
    private const string SettingsKey = @"Software\StrataUI\ContextMenu";
    private const int ENotImpl = unchecked((int)0x80004001);
    private const uint EcsEnabled = 0, EcsHidden = 2;
    private const uint SigdnFileSysPath = 0x80058000;
    private const string PinEventName = @"Local\StrataUI.Shell.PinToStart";

    private static readonly string RequestsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "pin-requests.txt");

    private static string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return key?.GetValue(name)?.ToString();
    }

    private static void Touch() => Interlocked.Exchange(ref Program.LastUse, Environment.TickCount64);

    public int GetTitle(IShellItemArray? items, out IntPtr name)
    {
        Touch();
        name = Marshal.StringToCoTaskMemUni(Read("Title") is { Length: > 0 } title ? title : "Pin to Strata UI Start");
        return 0;
    }

    public int GetIcon(IShellItemArray? items, out IntPtr icon)
    {
        string exe = Read("Exe") is { Length: > 0 } path && File.Exists(path) ? path : Environment.ProcessPath ?? "";
        icon = Marshal.StringToCoTaskMemUni($"{exe},0");
        return 0;
    }

    public int GetToolTip(IShellItemArray? items, out IntPtr tip)
    {
        tip = IntPtr.Zero;
        return ENotImpl;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = new Guid(Program.Clsid);
        return 0;
    }

    /// <summary>Oculto si se desactivó en el Hub o si no hay ningún archivo o carpeta con ruta.</summary>
    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        Touch();
        state = Read("Enabled") == "1" && Paths(items).Count > 0 ? EcsEnabled : EcsHidden;
        return 0;
    }

    public int Invoke(IShellItemArray? items, IntPtr bindContext)
    {
        Touch();
        var paths = Paths(items);
        if (paths.Count == 0)
            return 0;
        try
        {
            if (EventWaitHandle.TryOpenExisting(PinEventName, out var running))
            {
                // Strata UI abierto: el mismo pedido que hace "StrataUI.exe --pin-start".
                using (running)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RequestsPath)!);
                    File.AppendAllLines(RequestsPath, paths);
                    running.Set();
                }
            }
            else if (Read("Exe") is { Length: > 0 } exe && File.Exists(exe))
            {
                foreach (var path in paths)
                    Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "--pin-start", path }, UseShellExecute = false })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Marshal.GetHRForException(ex);
        }
        return 0;
    }

    public int GetFlags(out uint flags)
    {
        flags = 0; // ECF_DEFAULT
        return 0;
    }

    public int EnumSubCommands(out IntPtr commands)
    {
        commands = IntPtr.Zero;
        return ENotImpl;
    }

    private static List<string> Paths(IShellItemArray? items)
    {
        var paths = new List<string>();
        if (items is null)
            return paths;
        try
        {
            items.GetCount(out uint count);
            for (uint i = 0; i < count && i < 64; i++)
            {
                items.GetItemAt(i, out var item);
                if (item.GetDisplayName(SigdnFileSysPath, out var raw) >= 0 && raw != IntPtr.Zero)
                {
                    string? path = Marshal.PtrToStringUni(raw);
                    Marshal.FreeCoTaskMem(raw);
                    if (!string.IsNullOrEmpty(path))
                        paths.Add(path);
                }
            }
        }
        catch (COMException)
        {
            // Elementos sin ruta (Papelera, Este equipo…): no se ofrece.
        }
        return paths;
    }
}
