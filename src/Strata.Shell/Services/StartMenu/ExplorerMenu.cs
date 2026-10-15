using System.IO;
using Microsoft.Win32;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// "Anclar a Inicio de Strata UI" en el menú contextual del Explorador, de dos formas:
/// <list type="bullet">
/// <item>Con el paquete sparse que registra el instalador (StrataUI.ContextMenu.exe, un
/// IExplorerCommand): sale en el menú moderno de Windows 11, en el clásico y en Nilesoft Shell. Lee
/// su título, si está activado y la ruta de Strata UI de <see cref="CommandKey"/>.</item>
/// <item>Sin el paquete (Strata UI sin instalar), un verbo del usuario (HKCU, sin administrador) que
/// sale en el menú clásico ("Mostrar más opciones") y en Nilesoft Shell. Con el paquete se quita,
/// para no salir dos veces.</item>
/// </list>
/// Las dos terminan en <c>StrataUI.exe --pin-start "ruta"</c> o en el mismo pedido a la instancia abierta.
/// </summary>
public static class ExplorerMenu
{
    public const string PinArg = "--pin-start";

    private const string VerbKey = @"Software\Classes\AllFilesystemObjects\shell\StrataUI.PinToStart";
    private const string CommandKey = @"Software\StrataUI\ContextMenu";
    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private const string PinEventName = @"Local\StrataUI.Shell.PinToStart";

    private static readonly string RequestsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "pin-requests.txt");

    /// <summary>El paquete del menú moderno está registrado para este usuario.</summary>
    public static bool PackageInstalled()
    {
        using var packages = Registry.CurrentUser.OpenSubKey(PackagesKey);
        return packages?.GetSubKeyNames().Any(n => n.StartsWith("StrataUI.ContextMenu_", StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>Pone o quita el verbo según la opción (con la ruta del ejecutable actual).</summary>
    public static void Update()
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            using (var settings = Registry.CurrentUser.CreateSubKey(CommandKey))
            {
                settings.SetValue("Title", Loc.T("Anclar a Inicio de Strata UI"));
                settings.SetValue("Enabled", Settings.Current.StartExplorerMenu ? "1" : "0");
                settings.SetValue("Exe", exe);
            }
            if (!Settings.Current.StartExplorerMenu || PackageInstalled())
            {
                Registry.CurrentUser.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
                return;
            }
            using var key = Registry.CurrentUser.CreateSubKey(VerbKey);
            key.SetValue("MUIVerb", Loc.T("Anclar a Inicio de Strata UI"));
            key.SetValue("Icon", $"\"{exe}\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" {PinArg} \"%1\"");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("No se pudo actualizar el menú contextual del Explorador", ex);
        }
    }

    // ── Petición desde el Explorador ────────────────────────────────────────

    /// <summary>
    /// StrataUI.exe --pin-start ruta: si Strata UI está abierto le deja la ruta y lo avisa; si no, la
    /// ancla en la configuración (se verá al abrirlo).
    /// </summary>
    internal static int RunRequest(string[] args)
    {
        if (args.Length != 2 || !StartMenuService.IsPathId(args[1]) || !(File.Exists(args[1]) || Directory.Exists(args[1])))
            return 2;
        if (EventWaitHandle.TryOpenExisting(PinEventName, out var running))
        {
            using (running)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RequestsPath)!);
                // Con varios archivos elegidos el Explorador abre un proceso por archivo: se reintenta si el archivo está ocupado.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        File.AppendAllLines(RequestsPath, [args[1]]);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(50);
                    }
                }
                running.Set();
            }
            return 0;
        }
        PinLayout.Pin(args[1]);
        return 0;
    }

    private static EventWaitHandle? _event;
    private static RegisteredWaitHandle? _wait;

    /// <summary>La instancia principal atiende las peticiones (y las que quedaron de antes de abrirse).</summary>
    public static void Listen()
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, PinEventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_event,
            (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(HandleRequests), null, -1, executeOnlyOnce: false);
        HandleRequests();
    }

    public static void Stop()
    {
        _wait?.Unregister(null);
        _event?.Dispose();
    }

    private static void HandleRequests()
    {
        if (!File.Exists(RequestsPath))
            return;
        try
        {
            var paths = File.ReadAllLines(RequestsPath).Where(p => p.Length > 0).Distinct().ToList();
            File.Delete(RequestsPath);
            Log.Info($"Anclar a Inicio desde el Explorador: {string.Join(", ", paths)}");
            StartMenuService.PinPaths(paths);
        }
        catch (IOException)
        {
            // Otro proceso está escribiendo: llegará otro aviso.
        }
    }
}
