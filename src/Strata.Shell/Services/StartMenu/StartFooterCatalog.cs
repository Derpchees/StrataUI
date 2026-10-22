using System.Diagnostics;
using Wpf.Ui.Controls;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.StartMenu;

/// <summary>Botón configurable del pie del menú Inicio.</summary>
public sealed record StartFooterButton(string Id, string Label, SymbolRegular Icon, Action? Run)
{
    /// <summary>"Energía" abre un menú con todas las opciones en lugar de ejecutar una acción.</summary>
    public bool IsPowerMenu => Run is null;

    /// <summary>
    /// Punto naranja en el botón de energía cuando hay actualizaciones esperando un reinicio (como en
    /// Windows): su menú tiene "Actualizar y apagar" y "Actualizar y reiniciar". Se mira al armar el menú.
    /// </summary>
    public bool ShowUpdateBadge => IsPowerMenu && StartMenuService.UpdatesPending;
}

/// <summary>Todos los botones que se pueden poner en el pie del menú Inicio (orden de la lista en el Hub).</summary>
public static class StartFooterCatalog
{
    public static IReadOnlyList<StartFooterButton> All { get; } =
    [
        new("Power", Loc.T("Energía (menú con todas las opciones)"), SymbolRegular.Power24, null),
        new("Lock", Loc.T("Bloquear"), SymbolRegular.LockClosed24, StartMenuService.Lock),
        new("Sleep", Loc.T("Suspender"), SymbolRegular.WeatherMoon24, StartMenuService.Sleep),
        new("Restart", Loc.T("Reiniciar"), SymbolRegular.ArrowClockwise24, StartMenuService.Restart),
        new("Shutdown", Loc.T("Apagar"), SymbolRegular.Power24, StartMenuService.PowerOff),
        new("SignOut", Loc.T("Cerrar sesión"), SymbolRegular.SignOut24, StartMenuService.SignOut),
        new("Explorer", Loc.T("Explorador de archivos"), SymbolRegular.Folder24, () => Open("explorer.exe")),
        new("StrataFiles", Loc.T("Archivos de Strata UI"), SymbolRegular.FolderOpen24, () => App.Current.OpenFiles()),
        new("Documents", Loc.T("Documentos"), SymbolRegular.Document24, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyDocuments)),
        new("Downloads", Loc.T("Descargas"), SymbolRegular.ArrowDownload24, StartMenuService.OpenDownloads),
        new("Pictures", Loc.T("Imágenes"), SymbolRegular.Image24, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyPictures)),
        new("Music", Loc.T("Música"), SymbolRegular.MusicNote224, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyMusic)),
        new("Videos", "Videos", SymbolRegular.Video24, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyVideos)),
        new("Settings", Loc.T("Configuración de Windows"), SymbolRegular.Settings24, () => Open("ms-settings:")),
        new("ControlPanel", Loc.T("Panel de control"), SymbolRegular.Toolbox24, () => Open("control.exe")),
        new("TaskManager", Loc.T("Administrador de tareas"), SymbolRegular.DataUsage24, () => Open("taskmgr.exe")),
        new("Terminal", "Terminal", SymbolRegular.WindowConsole20, () => Open("wt.exe")),
        new("StrataSettings", Loc.T("Configuración de Strata UI"), SymbolRegular.PaintBrush24, () => App.Current.OpenHub()),
    ];

    public static IReadOnlyList<StartFooterButton> Selected =>
        Settings.Current.StartFooterButtons
            .Select(id => All.FirstOrDefault(b => b.Id == id))
            .Where(b => b != null)
            .Select(b => b!)
            .ToList();

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {target}", ex);
        }
    }
}
