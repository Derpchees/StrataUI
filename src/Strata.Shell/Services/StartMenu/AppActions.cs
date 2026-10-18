using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// Acciones del clic derecho sobre una app de Inicio que dependen de dónde está instalada:
/// abrir su ubicación, ejecutarla como administrador y desinstalarla.
/// </summary>
public static class AppActions
{
    /// <summary>App de la Tienda (o empaquetada): su ID es "Familia_editor!App".</summary>
    public static bool IsPackaged(StartApp app) => app.Id.Contains('!') && !app.Id.Contains('\\');

    /// <summary>Ejecutable (o archivo) al que apunta la app; null en apps empaquetadas o direcciones web.</summary>
    public static string? TargetPath(StartApp app)
    {
        if (IsPackaged(app))
            return null;
        // Juego de Steam, Epic o GOG: su ejecutable principal (el del ícono).
        if (app.ExtraData is { } extra)
            return extra.IconSource.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(extra.IconSource)
                ? extra.IconSource
                : null;
        if (app.IsFile && !app.Id.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return File.Exists(app.Id) ? app.Id : null;
        var path = ReadString(app.IsFile ? app.Id : "shell:AppsFolder\\" + app.Id, LinkTargetParsingPath);
        return path != null && Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;
    }

    /// <summary>Acceso directo del menú Inicio con el mismo nombre que la app (como "Abrir ubicación" de Windows).</summary>
    public static string? ShortcutPath(StartApp app)
    {
        if (app.IsFile)
            return app.Id.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(app.Id) ? app.Id : null;
        return Shortcuts().TryGetValue(app.Name, out var path) && File.Exists(path) ? path : null;
    }

    private static Dictionary<string, string>? _shortcuts;
    private static DateTime _shortcutsAt;

    /// <summary>Accesos directos de Inicio por nombre; se vuelven a leer si tienen más de un minuto.</summary>
    private static Dictionary<string, string> Shortcuts()
    {
        if (_shortcuts != null && DateTime.UtcNow - _shortcutsAt < TimeSpan.FromMinutes(1))
            return _shortcuts;
        var map = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var root in StartMenuFolders())
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    map.TryAdd(Path.GetFileNameWithoutExtension(file), file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        _shortcutsAt = DateTime.UtcNow;
        return _shortcuts = map;
    }

    private static IEnumerable<string> StartMenuFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    ];

    /// <summary>Hay un archivo que mostrar (las apps de la Tienda no tienen: como en Windows, no la ofrecen).</summary>
    public static bool CanOpenLocation(StartApp app) => app.IsFile || TargetPath(app) != null || ShortcutPath(app) != null;

    public static bool CanRunAsAdmin(StartApp app) =>
        TargetPath(app) is { } path && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    public static void OpenLocation(StartApp app)
    {
        // Un archivo o carpeta anclado se muestra seleccionado en su carpeta.
        var path = app.IsFile ? app.Id : ShortcutPath(app) ?? TargetPath(app);
        if (path is null)
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Abre la app pidiendo permiso de administrador (UAC). Con el acceso directo se conservan sus
    /// argumentos y carpeta de trabajo.
    /// </summary>
    public static void RunAsAdmin(StartApp app)
    {
        var path = ShortcutPath(app) is { } lnk && TargetPath(app) != null ? lnk : TargetPath(app);
        if (path is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // El usuario canceló el diálogo de UAC.
        }
    }

    /// <summary>
    /// Desinstala: las apps empaquetadas se quitan directamente (tras confirmar); las de escritorio
    /// abren su desinstalador, o la lista de programas de Windows si no se encuentra.
    /// </summary>
    public static async Task UninstallAsync(StartApp app)
    {
        if (IsPackaged(app))
        {
            var confirm = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("Desinstalar"),
                Content = Loc.F("Se desinstalará {0} y sus datos.", app.Name),
                PrimaryButtonText = Loc.T("Desinstalar"),
                CloseButtonText = Loc.T("Cancelar"),
            };
            if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
                return;
            await RemovePackageAsync(app);
            StartMenuService.Refresh();
            return;
        }

        // App web de un navegador (Brave, Chrome, Edge): su ejecutable es el del navegador, así que
        // buscar por carpeta daba con el desinstalador del navegador entero.
        string? command = WebAppId(app) is { } webApp ? WebAppUninstaller(app, webApp) : FindUninstaller(app);
        if (command != null)
        {
            Log.Info($"Desinstalando {app.Name}: {command}");
            var (file, args) = SplitCommand(command);
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
                // Al terminar el desinstalador se vuelve a leer el catálogo (no siempre se nota el cambio solo).
                if (process != null)
                    _ = process.WaitForExitAsync().ContinueWith(_ =>
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(StartMenuService.Refresh));
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
            }
            return;
        }
        Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
    }

    // ── Apps web de navegadores Chromium ────────────────────────────────────

    /// <summary>
    /// ID de la app web (32 letras a–p) si la app es una app web de un navegador Chromium: su
    /// AppUserModelID lleva "_crx_ID" y su acceso directo, "--app-id=ID".
    /// </summary>
    internal static string? WebAppId(StartApp app)
    {
        if (CrxId(app.Id) is { } fromId)
            return fromId;
        if (app.IsExtra || IsPackaged(app))
            return null;
        var lnk = ShortcutPath(app);
        string? arguments = lnk != null ? ReadString(lnk, LinkArguments) : null;
        return AppIdArgument(arguments);
    }

    internal static string? CrxId(string id)
    {
        int at = id.IndexOf("_crx_", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return null;
        var rest = id[(at + 5)..];
        return rest.Length >= 32 && rest[..32].All(c => c is >= 'a' and <= 'p') ? rest[..32] : null;
    }

    internal static string? AppIdArgument(string? arguments)
    {
        if (arguments is null)
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(arguments, @"--app-id=([a-p]{32})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// La entrada de "Programas y características" que el navegador registró para la app web
    /// (UninstallString con "--uninstall-app-id=ID"). Si no hay, se le pide al navegador mismo que
    /// la quite (muestra su confirmación); nunca el desinstalador del navegador.
    /// </summary>
    private static string? WebAppUninstaller(StartApp app, string appId)
    {
        foreach (var (hive, view) in new[] { (RegistryHive.CurrentUser, RegistryView.Default), (RegistryHive.LocalMachine, RegistryView.Registry64) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null)
                continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("UninstallString") is string command
                    && command.Contains("--uninstall-app-id=" + appId, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(SplitCommand(command).File))
                    return command;
            }
        }
        // El navegador del acceso directo (chrome_proxy.exe o brave_proxy.exe está junto al navegador).
        if (TargetPath(app) is not { } target || Path.GetDirectoryName(target) is not { } folder)
            return null;
        var browser = Directory.EnumerateFiles(folder, "*.exe")
            .FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant() is "brave.exe" or "chrome.exe" or "msedge.exe" or "vivaldi.exe" or "opera.exe");
        if (browser is null)
            return null;
        var lnk = ShortcutPath(app);
        string profile = (lnk != null ? ReadString(lnk, LinkArguments) : null) is { } args
                         && System.Text.RegularExpressions.Regex.Match(args, "--profile-directory=(\"[^\"]+\"|\\S+)") is { Success: true } m
            ? m.Groups[1].Value
            : "Default";
        return $"\"{browser}\" --profile-directory={profile} --uninstall-app-id={appId}";
    }

    private static async Task RemovePackageAsync(StartApp app)
    {
        string family = app.Id[..app.Id.IndexOf('!')];
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            var package = manager.FindPackagesForUser("", family).FirstOrDefault();
            if (package is null)
                return;
            Log.Info($"Desinstalando el paquete {package.Id.FullName}");
            await manager.RemovePackageAsync(package.Id.FullName);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo desinstalar {app.Name}", ex);
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("Desinstalar"),
                Content = Loc.F("Windows no permitió desinstalar {0}.", app.Name),
                CloseButtonText = Loc.T("Cerrar"),
            }.ShowDialogAsync();
        }
    }

    /// <summary>
    /// Entrada de "Programas y características" de la app: por nombre, o porque su ejecutable
    /// está dentro de la carpeta de instalación o es su ícono.
    /// </summary>
    internal static string? FindUninstaller(StartApp app)
    {
        string? target = TargetPath(app);
        // Lanzador de apps web de un navegador: la carpeta es la del navegador y su desinstalador
        // quitaría el navegador entero.
        if (target != null && Path.GetFileName(target).EndsWith("_proxy.exe", StringComparison.OrdinalIgnoreCase))
            return null;
        string? targetDir = target != null ? Path.GetDirectoryName(target) : null;
        string? best = null;
        int bestScore = 0;
        foreach (var (hive, view) in new[] { (RegistryHive.CurrentUser, RegistryView.Default), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null)
                continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("UninstallString") is not string command || string.IsNullOrWhiteSpace(command))
                    continue;
                int score = UninstallScore(app.Name, targetDir, target,
                    key.GetValue("DisplayName") as string, key.GetValue("InstallLocation") as string, key.GetValue("DisplayIcon") as string);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = command;
                }
            }
        }
        return best;
    }

    internal static int UninstallScore(string appName, string? targetDir, string? target, string? displayName, string? installLocation, string? displayIcon)
    {
        // DisplayIcon suele ser "C:\ruta\app.exe",0: primero se separa el índice y luego las comillas.
        if (target != null && displayIcon != null && displayIcon.Split(',')[0].Trim().Trim('"').Equals(target, StringComparison.OrdinalIgnoreCase))
            return 4;
        if (targetDir != null && !string.IsNullOrWhiteSpace(installLocation)
            && targetDir.StartsWith(installLocation.Trim('"').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return 3;
        if (displayName is null)
            return 0;
        if (displayName.Equals(appName, StringComparison.CurrentCultureIgnoreCase))
            return 2;
        return displayName.StartsWith(appName + " ", StringComparison.CurrentCultureIgnoreCase) ? 1 : 0;
    }

    /// <summary>Separa "C:\Ruta con espacios\uninst.exe" /S en archivo y argumentos.</summary>
    internal static (string File, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }
        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
            return (command[..(exe + 4)], command[(exe + 4)..].Trim());
        int space = command.IndexOf(' ');
        return space > 0 ? (command[..space], command[(space + 1)..]) : (command, "");
    }

    // ── Propiedades del shell ───────────────────────────────────────────────

    private static readonly PropertyKey LinkTargetParsingPath = new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);
    private static readonly PropertyKey LinkArguments = new(new Guid("436F2667-14E2-4FEB-B30A-146C53B5B674"), 100);

    private static string? ReadString(string parsingName, PropertyKey key)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreFromParsingName(parsingName, IntPtr.Zero, 0, ref iid, out var store) != 0)
            return null;
        try
        {
            if (store.GetValue(ref key, out var value) != 0)
                return null;
            try
            {
                return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; // VT_LPWSTR
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, int flags, ref Guid iid, out IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
