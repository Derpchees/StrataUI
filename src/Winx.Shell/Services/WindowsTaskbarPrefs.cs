using Microsoft.Win32;

namespace Winx.Shell.Services;

/// <summary>
/// Preferencias de la barra de Windows que WINX puede seguir: alineación (Configuración ›
/// Personalización › Barra de tareas) y en qué barras aparecen las apps con varios monitores.
/// </summary>
public static class WindowsTaskbarPrefs
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>Alineación efectiva: la elegida en WINX o, con "Windows", la de Windows.</summary>
    public static TaskbarAlignment Alignment(WinxSettings s) =>
        s.TaskbarAlignment == TaskbarAlignment.Windows ? AlignmentFrom(Read("TaskbarAl")) : s.TaskbarAlignment;

    /// <summary>Modo efectivo de las apps por monitor.</summary>
    public static TaskbarAppsMode AppsMode(WinxSettings s) =>
        s.TaskbarAppsMode == TaskbarAppsMode.Windows ? AppsModeFrom(Read("MMTaskbarMode")) : s.TaskbarAppsMode;

    /// <summary>TaskbarAl: 0 = izquierda; 1 o sin valor (lo normal en Windows 11) = centro.</summary>
    internal static TaskbarAlignment AlignmentFrom(int? taskbarAl) =>
        taskbarAl == 0 ? TaskbarAlignment.Left : TaskbarAlignment.Center;

    /// <summary>MMTaskbarMode: 0 o sin valor = todas, 1 = principal y la del monitor, 2 = solo la del monitor.</summary>
    internal static TaskbarAppsMode AppsModeFrom(int? mmTaskbarMode) => mmTaskbarMode switch
    {
        1 => TaskbarAppsMode.MainAndOwn,
        2 => TaskbarAppsMode.OwnOnly,
        _ => TaskbarAppsMode.AllTaskbars,
    };

    private static int? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Advanced);
        return key?.GetValue(name) is int value ? value : null;
    }
}
