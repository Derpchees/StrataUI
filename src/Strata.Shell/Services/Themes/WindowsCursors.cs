using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Punteros del mouse del tema importado: el módulo nombra un esquema instalado en Windows
/// (Panel de control › Mouse, p. ej. "Windows Black") o uno propio ("Strata White", "Strata Black")
/// y se aplica a todo Windows.
/// </summary>
/// <remarks>
/// Los punteros de antes se guardan en la configuración antes de tocarlos y se devuelven al quitar
/// el tema, al salir de Strata UI o con --restore-taskbar (como <see cref="WindowsAnimations"/>).
/// </remarks>
public static class WindowsCursors
{
    private const string CursorsKey = @"Control Panel\Cursors";
    private const string SchemesKey = @"Control Panel\Cursors\Schemes";
    private const string SystemSchemesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";

    // Orden de los archivos en la lista de un esquema (el de main.cpl).
    private static readonly string[] Roles =
        ["Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE",
         "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"];

    // Archivos de los esquemas propios ("Strata White", "Strata Black"; Assets\Cursors, de scripts\build-cursors.ps1), en el orden de Roles.
    private static readonly string[] OwnFiles =
        ["arrow.cur", "help.cur", "working.ani", "busy.ani", "cross.cur", "text.cur", "pen.cur", "unavailable.cur", "vert.cur",
         "horz.cur", "dgn1.cur", "dgn2.cur", "move.cur", "alternate.cur", "link.cur", "pin.cur", "person.cur"];

    private const uint SetCursors = 0x0057, UpdateIniFile = 0x01, SendChange = 0x02;

    /// <summary>Pone los punteros del tema elegido (al abrir y cuando cambia).</summary>
    public static void Update()
    {
        var wanted = ModuleStore.Get<ThemeModule>(ModuleKind.Theme, Settings.Current.ThemeModule)?.Cursors;
        if (wanted is null)
        {
            Restore();
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(CursorsKey);
            if (Settings.Current.WindowsCursorBackup != null && (key.GetValue("") as string) == wanted)
                return;
            if (SchemeFiles(wanted) is not { } files)
            {
                Log.Info($"Punteros: no está instalado el esquema \"{wanted}\"");
                return;
            }
            if (Settings.Current.WindowsCursorBackup is null)
            {
                Settings.Current.WindowsCursorBackup = Roles.Prepend("").Prepend("Scheme Source")
                    .ToDictionary(r => r, r => key.GetValue(r, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString());
                Settings.Save();
            }
            key.SetValue("", wanted);
            for (int i = 0; i < Roles.Length; i++)
                key.SetValue(Roles[i], i < files.Length ? files[i] : "", RegistryValueKind.ExpandString);
            Apply();
            Log.Info($"Punteros: esquema \"{wanted}\"");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron cambiar los punteros", ex);
        }
    }

    /// <summary>Devuelve los punteros como estaban antes de Strata UI.</summary>
    public static void Restore()
    {
        if (Settings.Current.WindowsCursorBackup is not { } backup)
            return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(CursorsKey);
            foreach (var (name, value) in backup)
            {
                if (value is null)
                    key.DeleteValue(name, false);
                else if (name == "Scheme Source")
                    key.SetValue(name, int.TryParse(value, out var n) ? n : 0, RegistryValueKind.DWord);
                else
                    key.SetValue(name, value, name == "" ? RegistryValueKind.String : RegistryValueKind.ExpandString);
            }
            Apply();
            Log.Info("Punteros devueltos");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron devolver los punteros", ex);
        }
        Settings.Current.WindowsCursorBackup = null;
        Settings.Save();
    }

    /// <summary>Archivos del esquema (del usuario o de Windows), en el orden de <see cref="Roles"/>; null si no existe.</summary>
    private static string[]? SchemeFiles(string name)
    {
        if (name is "Strata White" or "Strata Black")
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Cursors", name[7..]);
            return Directory.Exists(dir) ? OwnFiles.Select(f => Path.Combine(dir, f)).ToArray() : null;
        }
        using var user = Registry.CurrentUser.OpenSubKey(SchemesKey);
        using var system = Registry.LocalMachine.OpenSubKey(SystemSchemesKey);
        var list = (user?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                    ?? system?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) as string;
        // La lista puede terminar con el nombre traducible del esquema ("@main.cpl,-1020"): no es un archivo.
        return list?.Split(',').Select(f => f.Trim()).Where(f => !f.StartsWith('@') && !f.StartsWith('-')).ToArray();
    }

    private static void Apply() => SystemParametersInfo(SetCursors, 0, IntPtr.Zero, UpdateIniFile | SendChange);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);
}
