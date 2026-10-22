using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Clic en una notificación de una app de escritorio que registró su propio activador (Thunderbird,
/// Firefox…). Su AppUserModelID no es una app de <c>shell:AppsFolder</c>, así que abrirla por ahí no
/// hacía nada; Windows en cambio crea el objeto COM de <c>CustomActivator</c> y le pasa el "launch"
/// del aviso, y la app abre el correo o la conversación. Aquí se hace lo mismo.
/// </summary>
internal static class ToastActivator
{
    public static bool TryActivate(StrataNotification item)
    {
        if (item.AppId is not { Length: > 0 } appId || Activator(appId) is not { } clsid)
            return false;
        string launch = item.Launch ?? "";
        // En un hilo MTA: el activador puede tardar (arranca la app) y no debe trabar la interfaz.
        Task.Run(() =>
        {
            object? instance = null;
            try
            {
                var iid = typeof(INotificationActivationCallback).GUID;
                int hr = CoCreateInstance(clsid, IntPtr.Zero, ClsctxInprocServer | ClsctxLocalServer, iid, out instance);
                if (hr < 0 || instance is not INotificationActivationCallback callback)
                {
                    Log.Info($"El activador de {appId} no respondió (0x{hr:X8})");
                    return;
                }
                callback.Activate(appId, launch, IntPtr.Zero, 0);
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo pasar el clic de la notificación a {item.AppName}", ex);
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance))
                    Marshal.ReleaseComObject(instance);
            }
        });
        return true;
    }

    /// <summary>El CLSID que la app registró para recibir los clics de sus notificaciones.</summary>
    private static Guid? Activator(string appId)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Classes\AppUserModelId\{appId}");
            if (key?.GetValue("CustomActivator") is string value && Guid.TryParse(value, out var clsid))
                return clsid;
        }
        return null;
    }

    private const uint ClsctxInprocServer = 0x1, ClsctxLocalServer = 0x4;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance([In] in Guid clsid, IntPtr outer, uint context, [In] in Guid iid,
                                               [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationActivationCallback
    {
        void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                      [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
                      IntPtr data, uint count);
    }
}
