using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Clic en una notificación de una app con su propio activador, como hace Windows: crea el objeto
/// COM del activador y le pasa el "launch" del aviso, y la app abre el chat, el correo o la canción.
/// Abrir la app por su AppUserModelID solo la mostraba (o no hacía nada).
/// <list type="bullet">
/// <item>Apps de escritorio (Thunderbird…): <c>CustomActivator</c> en el registro.</item>
/// <item>Apps empaquetadas (WhatsApp, Spotify…): <c>ToastActivatorCLSID</c> en su AppxManifest.xml;
/// solo como servidor aparte (un activador en DLL, como el de Firefox, no se carga en Strata UI).</item>
/// </list>
/// </summary>
internal static class ToastActivator
{
    private static readonly Dictionary<string, Guid?> PackagedCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True si el activador recibió el clic; false para que quien llama abra la app de otra forma.</summary>
    public static async Task<bool> TryActivateAsync(StrataNotification item)
    {
        if (item.AppId is not { Length: > 0 } appId)
            return false;
        uint context;
        Guid clsid;
        if (Registered(appId) is { } registered)
        {
            (clsid, context) = (registered, ClsctxInprocServer | ClsctxLocalServer);
        }
        else if (await Task.Run(() => Packaged(appId)) is { } packaged)
        {
            (clsid, context) = (packaged, ClsctxLocalServer);
        }
        else
        {
            return false;
        }

        string launch = item.Launch ?? "";
        // En un hilo MTA: el activador puede tardar (arranca la app) y no debe trabar la interfaz.
        var activation = Task.Run(() =>
        {
            object? instance = null;
            try
            {
                var iid = typeof(INotificationActivationCallback).GUID;
                int hr = CoCreateInstance(clsid, IntPtr.Zero, context, iid, out instance);
                if (hr < 0 || instance is not INotificationActivationCallback callback)
                {
                    Log.Info($"El activador de {appId} no respondió (0x{hr:X8})");
                    return false;
                }
                // La app recibe el permiso de pasar al frente (si no, Windows solo hace parpadear su botón).
                CoAllowSetForegroundWindow(instance, IntPtr.Zero);
                callback.Activate(appId, launch, IntPtr.Zero, 0);
                Log.Info($"Notificación de {item.AppName}: el clic se pasó a su activador");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo pasar el clic de la notificación a {item.AppName}", ex);
                return false;
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance))
                    Marshal.ReleaseComObject(instance);
            }
        });
        // Una app que tarda en arrancar ya recibió el pedido: no se abre otra vez por otro camino.
        return await Task.WhenAny(activation, Task.Delay(TimeSpan.FromSeconds(8))) != activation || activation.Result;
    }

    /// <summary>El CLSID que una app de escritorio registró para recibir los clics de sus notificaciones.</summary>
    private static Guid? Registered(string appId)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Classes\AppUserModelId\{appId}");
            if (key?.GetValue("CustomActivator") is string value && Guid.TryParse(value, out var clsid))
                return clsid;
        }
        return null;
    }

    /// <summary>
    /// El activador que declara el manifiesto de una app empaquetada ("Familia_editor!App"), si es un
    /// servidor aparte (com:ExeServer) y no una DLL.
    /// </summary>
    private static Guid? Packaged(string appId)
    {
        lock (PackagedCache)
        {
            if (PackagedCache.TryGetValue(appId, out var cached))
                return cached;
        }
        Guid? result = null;
        int bang = appId.IndexOf('!');
        if (bang > 0)
        {
            try
            {
                var package = new Windows.Management.Deployment.PackageManager()
                    .FindPackagesForUser("", appId[..bang]).FirstOrDefault();
                if (package != null)
                    result = FromManifest(Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml"), appId[(bang + 1)..]);
            }
            catch (Exception ex)
            {
                Log.Info($"No se pudo leer el paquete de {appId} ({ex.Message.Trim()})");
            }
        }
        lock (PackagedCache)
            PackagedCache[appId] = result;
        return result;
    }

    internal static Guid? FromManifest(string manifestPath, string applicationId)
    {
        var doc = XDocument.Load(manifestPath);
        var application = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application"
                                                                && (string?)e.Attribute("Id") == applicationId);
        var declared = (application ?? doc.Root!).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ToastNotificationActivation")?.Attribute("ToastActivatorCLSID")?.Value;
        if (!Guid.TryParse(declared, out var clsid))
            return null;
        // Solo si ese CLSID es de un ejecutable (com:ExeServer › com:Class).
        bool exeServer = doc.Descendants().Any(e => e.Name.LocalName == "Class"
                                                    && Guid.TryParse((string?)e.Attribute("Id"), out var id) && id == clsid
                                                    && e.Parent?.Name.LocalName == "ExeServer");
        return exeServer ? clsid : null;
    }

    private const uint ClsctxInprocServer = 0x1, ClsctxLocalServer = 0x4;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance([In] in Guid clsid, IntPtr outer, uint context, [In] in Guid iid,
                                               [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    [DllImport("ole32.dll")]
    private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object instance, IntPtr reserved);

    [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationActivationCallback
    {
        void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                      [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
                      IntPtr data, uint count);
    }
}
