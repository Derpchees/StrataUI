using System.Diagnostics;
using Wpf.Ui.Controls;

namespace Winx.Shell.Services.StartMenu;

/// <summary>Botón configurable del pie del menú Inicio.</summary>
public sealed record StartFooterButton(string Id, string Label, SymbolRegular Icon, Action? Run)
{
    /// <summary>"Energía" abre un menú con todas las opciones en lugar de ejecutar una acción.</summary>
    public bool IsPowerMenu => Run is null;
}

/// <summary>Todos los botones que se pueden poner en el pie del menú Inicio (orden de la lista en el Hub).</summary>
public static class StartFooterCatalog
{
    public static IReadOnlyList<StartFooterButton> All { get; } =
    [
        new("Power", "Energía (menú con todas las opciones)", SymbolRegular.Power24, null),
        new("Lock", "Bloquear", SymbolRegular.LockClosed24, StartMenuService.Lock),
        new("Sleep", "Suspender", SymbolRegular.WeatherMoon24, StartMenuService.Sleep),
        new("Restart", "Reiniciar", SymbolRegular.ArrowClockwise24, StartMenuService.Restart),
        new("Shutdown", "Apagar", SymbolRegular.Power24, StartMenuService.PowerOff),
        new("SignOut", "Cerrar sesión", SymbolRegular.SignOut24, StartMenuService.SignOut),
        new("Explorer", "Explorador de archivos", SymbolRegular.Folder24, () => Open("explorer.exe")),
        new("Documents", "Documentos", SymbolRegular.Document24, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyDocuments)),
        new("Downloads", "Descargas", SymbolRegular.ArrowDownload24, StartMenuService.OpenDownloads),
        new("Pictures", "Imágenes", SymbolRegular.Image24, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyPictures)),
        new("Music", "Música", SymbolRegular.MusicNote224, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyMusic)),
        new("Videos", "Videos", SymbolRegular.Video24, () => StartMenuService.OpenFolder(Environment.SpecialFolder.MyVideos)),
        new("Settings", "Configuración de Windows", SymbolRegular.Settings24, () => Open("ms-settings:")),
        new("ControlPanel", "Panel de control", SymbolRegular.Toolbox24, () => Open("control.exe")),
        new("TaskManager", "Administrador de tareas", SymbolRegular.DataUsage24, () => Open("taskmgr.exe")),
        new("Terminal", "Terminal", SymbolRegular.WindowConsole20, () => Open("wt.exe")),
        new("WinxSettings", "Configuración de WINX", SymbolRegular.PaintBrush24, () => App.Current.OpenHub()),
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
