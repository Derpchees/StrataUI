namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// Herramientas de Windows que el catálogo de apps trae sueltas en algunos equipos (Servicios,
/// Desfragmentar y optimizar unidades, Visor de eventos…): no van en "Todas las apps" (como en el
/// Inicio de Windows 11, que las agrupa en "Herramientas de Windows"), pero se encuentran al buscar.
/// </summary>
public static class SystemTools
{
    // Carpetas conocidas con que el catálogo identifica los programas de Windows (System32, SysWOW64, Windows).
    private static readonly string[] SystemFolderIds =
    [
        "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", "{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}", "{F38BF404-1D43-42F2-9305-67DE0B28FC23}",
    ];

    // Lo de Windows que sí es una app de todos los días.
    private static readonly HashSet<string> Everyday = new(StringComparer.OrdinalIgnoreCase)
    {
        "notepad.exe", "mspaint.exe", "calc.exe", "SnippingTool.exe", "explorer.exe", "write.exe", "wordpad.exe",
    };

    /// <summary>Si la entrada del catálogo (su AppUserModelID o ruta) es una herramienta de Windows.</summary>
    public static bool IsTool(string id)
    {
        if (id.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase))
            return true;
        string? file = null;
        foreach (var folder in SystemFolderIds)
            if (id.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
                file = id[(folder.Length + 1)..];
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (file is null && windows.Length > 0 && id.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
            file = id[(windows.Length + 1)..];
        return file != null && !Everyday.Contains(System.IO.Path.GetFileName(file));
    }

    /// <summary>
    /// Los accesos directos de "Herramientas de Windows" (Administrative Tools y System Tools del menú
    /// Inicio: Administrador de tareas, Editor del Registro, Símbolo del sistema…), para la búsqueda.
    /// El nombre lo pone el Shell (traducido).
    /// </summary>
    public static List<ExtraApp> Shortcuts()
    {
        var found = new List<ExtraApp>();
        foreach (var programs in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
            foreach (var folder in new[] { "Administrative Tools", "System Tools" })
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(programs), folder);
                try
                {
                    if (System.IO.Directory.Exists(dir))
                        foreach (var link in System.IO.Directory.EnumerateFiles(dir, "*.lnk"))
                            found.Add(new ExtraApp(link, "", link, link, null, Tool: true));
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    Log.Error($"No se pudo leer {dir}", ex);
                }
            }
        return found;
    }
}
