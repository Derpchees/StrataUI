using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Services;

/// <summary>Alineación de Inicio y las apps; "Windows" sigue la de la barra de Windows.</summary>
public enum TaskbarAlignment { Center, Left, Windows }

/// <summary>En qué barras aparece cada ventana abierta (las mismas opciones que Windows).</summary>
public enum TaskbarAppsMode { Windows, AllTaskbars, MainAndOwn, OwnOnly }

/// <summary>Borde de la pantalla donde va la barra.</summary>
public enum TaskbarPosition { Bottom, Top, Left, Right }

public enum WinxTheme { System, Light, Dark, Transparent, LiquidGlass, LiquidGlassDark, Material }

public enum StartMenuStyle { Windows11, List, Fullscreen, Compact, Spotlight, Icons, Profile }

public enum StartIconSize { Small, Medium, Large }

/// <summary>Sección de apps ancladas con nombre.</summary>
public sealed class PinSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Loc.T("Anclado");
    public List<PinEntry> Items { get; set; } = new();
}

/// <summary>Elemento anclado: una app (<see cref="AppId"/>) o una carpeta con varias.</summary>
public sealed class PinEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? AppId { get; set; }
    public string? FolderName { get; set; }
    public List<string>? FolderApps { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFolder => FolderApps != null;
}

public enum StartIconKind { Winx, Windows, WindowsMono, Circle, Dot, Grid, Search }

/// <summary>Preferencias de WINX, guardadas en %LOCALAPPDATA%\WINX\settings.json.</summary>
public sealed class WinxSettings
{
    // Barra de tareas
    public TaskbarAlignment TaskbarAlignment { get; set; } = TaskbarAlignment.Windows;
    /// <summary>Las apps abiertas quedan al centro de la barra aunque Inicio esté a la izquierda.</summary>
    public bool TaskbarCenterApps { get; set; }
    /// <summary>Monitor principal de WINX (bandeja, tecla Windows); null = el principal de Windows.</summary>
    public string? TaskbarMainScreen { get; set; }
    /// <summary>Red, Bluetooth, sonido, batería y reloj en todas las barras (false: solo en la principal).</summary>
    public bool StatusOnAllTaskbars { get; set; } = true;
    public TaskbarAppsMode TaskbarAppsMode { get; set; } = TaskbarAppsMode.Windows;
    /// <summary>Posición de la barra en cada monitor (clave: nombre del dispositivo, "\\.\DISPLAY1"); abajo si no está.</summary>
    public Dictionary<string, TaskbarPosition> TaskbarPositions { get; set; } = new();
    /// <summary>Monitores sin barra de WINX (nunca el principal: ahí va la bandeja).</summary>
    public List<string> TaskbarHiddenScreens { get; set; } = new();

    public TaskbarPosition PositionOf(string screen) =>
        TaskbarPositions.TryGetValue(screen, out var position) ? position : TaskbarPosition.Bottom;
    public int TaskbarHeight { get; set; } = 48;
    public bool ShowSeconds { get; set; }
    public bool ShowDate { get; set; } = true;

    // Menú Inicio
    public StartMenuStyle StartStyle { get; set; } = StartMenuStyle.Windows11;
    public bool StartShowRecent { get; set; } = true;
    public bool StartShowPinned { get; set; } = true;
    public bool StartShowAllApps { get; set; } = true;
    public bool StartShowUser { get; set; } = true;
    public bool StartShowUserPicture { get; set; } = true;
    public StartIconKind StartIcon { get; set; } = StartIconKind.Winx;
    /// <summary>Botones del pie del menú, en orden (IDs de StartFooterCatalog).</summary>
    public List<string> StartFooterButtons { get; set; } = ["Explorer", "WinxSettings", "Power"];
    public bool StartUseWinKey { get; set; } = true;
    /// <summary>Formato anterior de anclados (lista plana); se migra a <see cref="StartLayout"/>.</summary>
    public List<string>? PinnedStartApps { get; set; }
    /// <summary>Anclados organizados en secciones y carpetas; null = primera vez.</summary>
    public List<PinSection>? StartLayout { get; set; }
    public StartIconSize StartIconSize { get; set; } = StartIconSize.Medium;
    /// <summary>Nombres debajo de las apps ancladas (sin ellos, el nombre queda en el tooltip).</summary>
    public bool StartShowPinnedLabels { get; set; } = true;
    /// <summary>Tamaño elegido por el usuario para cada estilo de menú: [ancho, alto] en DIP.</summary>
    public Dictionary<string, double[]> StartMenuSizes { get; set; } = new();

    // Módulos del área de estado
    public bool ShowNetworkModule { get; set; } = true;
    public bool ShowBluetoothModule { get; set; } = true;
    public bool ShowVolumeModule { get; set; } = true;
    public bool ShowBatteryModule { get; set; } = true;

    /// <summary>Idioma de la interfaz: "auto" (el de Windows), "es", "en"…</summary>
    public string Language { get; set; } = "auto";

    // Apariencia
    public WinxTheme Theme { get; set; } = WinxTheme.System;
    /// <summary>Color "#RRGGBB"; null = usar el acento de Windows.</summary>
    public string? AccentColor { get; set; }
    /// <summary>Liquid Glass con refracción real (captura del fondo + shader). Oculta la barra y
    /// los paneles de las capturas de pantalla; desactivado usa el desenfoque de Windows.</summary>
    public bool LiveGlass { get; set; } = true;
    /// <summary>Llevar el tema a Windows: modo claro/oscuro y fondo de las ventanas del Explorador.</summary>
    public bool ApplyThemeToWindows { get; set; }
    /// <summary>Fondo que había antes de que WINX lo cambiara por primera vez.</summary>
    public string? OriginalWallpaper { get; set; }

    // Bandeja: null = primera ejecución (se importan los íconos promovidos en Windows).
    public string[]? PinnedTrayIcons { get; set; }
    public List<string> KnownTrayIcons { get; set; } = new();
}

public static class Settings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINX", "settings.json");

    public static WinxSettings Current { get; private set; } = Load();

    /// <summary>Se dispara tras cada <see cref="Update"/>; la UI se sincroniza escuchándolo.</summary>
    public static event Action? Changed;

    public static void Update(Action<WinxSettings> change)
    {
        change(Current);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Solo pruebas: evita escribir el settings.json real del usuario.</summary>
    internal static bool SuppressSave { get; set; }

    /// <summary>Guarda sin notificar (para datos internos como los íconos conocidos).</summary>
    public static void Save()
    {
        if (SuppressSave)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Error("No se pudo guardar la configuración", ex);
        }
    }

    private static WinxSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WinxSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("Configuración inválida; se usan valores por defecto", ex);
        }
        return new();
    }
}
