using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Notepad;

/// <summary>
/// Registra el Bloc de notas de Strata UI en Windows (solo este usuario, HKCU): un tipo de archivo
/// propio, "Abrir con" en las extensiones de texto y la entrada en Aplicaciones predeterminadas para
/// que el usuario lo elija. Windows no deja a ninguna app ponerse de predeterminada sola. Se queda al
/// cerrar Strata UI (sin la app abierta, el editor se abre solo) y se quita al desactivarlo, al quitar
/// el módulo o al desinstalar (--unregister-notepad).
/// </summary>
internal static class NotepadRegistration
{
    public const string ProgId = "StrataUI.TextFile";
    public const string AppName = "Strata UI Notepad";
    public const string UnregisterArg = "--unregister-notepad";
    private const string Capabilities = @"Software\StrataUI\Notepad\Capabilities";

    public static bool Wanted => OptionalModules.Notepad.ReplacesNotepad;

    /// <summary>Aplica el estado según los ajustes (al iniciar y cuando cambian).</summary>
    public static void Update()
    {
        try
        {
            if (Wanted)
                Register();
            else if (IsRegistered)
                Unregister();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Log.Error("No se pudo registrar el Bloc de notas en Windows", ex);
        }
    }

    private static bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
            return key is not null;
        }
    }

    private static string Command => $"\"{Environment.ProcessPath}\" --notepad \"%1\"";

    private static void Register()
    {
        string exe = Environment.ProcessPath ?? "";
        using (var current = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            if (current?.GetValue(null) as string == Command && IsComplete())
                return;

        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue(null, Loc.T("Documento de texto"));
            prog.SetValue("FriendlyTypeName", Loc.T("Documento de texto"));
            using (var icon = prog.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, @"%SystemRoot%\System32\imageres.dll,-102", RegistryValueKind.ExpandString);
            using (var open = prog.CreateSubKey(@"shell\open"))
                open.SetValue("FriendlyAppName", Loc.T("Bloc de notas de Strata UI"));
            using var command = prog.CreateSubKey(@"shell\open\command");
            command.SetValue(null, Command);
        }
        using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{System.IO.Path.GetFileName(exe)}\SupportedTypes"))
            foreach (string ext in TextFile.Extensions)
                app.SetValue(ext, "");
        using (var caps = Registry.CurrentUser.CreateSubKey(Capabilities))
        {
            caps.SetValue("ApplicationName", Loc.T("Bloc de notas de Strata UI"));
            caps.SetValue("ApplicationDescription", Loc.T("Editor de texto con pestañas de Strata UI."));
            caps.SetValue("ApplicationIcon", $"\"{exe}\",0");
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (string ext in TextFile.Extensions)
                assoc.SetValue(ext, ProgId);
        }
        using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue(AppName, Capabilities);
        foreach (string ext in TextFile.Extensions)
        {
            using var with = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
            with.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        NotifyChanged();
        Log.Info("Bloc de notas registrado en Windows (Abrir con y Aplicaciones predeterminadas)");
    }

    private static bool IsComplete()
    {
        using var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
        if (registered?.GetValue(AppName) is null)
            return false;
        using var with = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.txt\OpenWithProgids");
        return with?.GetValueNames().Contains(ProgId) == true;
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\StrataUI\Notepad", false);
        if (Environment.ProcessPath is { } exe)
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{System.IO.Path.GetFileName(exe)}\SupportedTypes", false);
        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            registered?.DeleteValue(AppName, false);
        foreach (string ext in TextFile.Extensions)
        {
            using var with = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
            with?.DeleteValue(ProgId, false);
        }
        NotifyChanged();
        Log.Info("Bloc de notas quitado del registro de Windows");
    }

    /// <summary>Desinstalador: quita todo lo que se registró.</summary>
    public static int RunUnregister()
    {
        try
        {
            Unregister();
            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return 1;
        }
    }

    /// <summary>Aplicaciones predeterminadas de Windows, en la página del Bloc de notas de Strata UI.</summary>
    public static void OpenDefaultAppsSettings()
    {
        Register();
        try
        {
            Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={Uri.EscapeDataString(AppName)}")
                { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("No se pudo abrir Aplicaciones predeterminadas", ex);
        }
    }

    private static void NotifyChanged() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
