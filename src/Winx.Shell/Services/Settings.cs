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

/// <summary>
/// Segunda barra de un monitor, en el borde opuesto, y qué lleva. Con "Apps" o "Status" las dos
/// barras se reparten el contenido; con "Full" las dos llevan todo.
/// </summary>
public enum SecondBarContent { None, Apps, Status, Full }

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

    /// <summary>Espacio vacío que ocupa una celda (sin app ni carpeta).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGap => FolderApps is null && AppId is null;
}

public enum StartIconKind { Winx, Windows, WindowsMono, Circle, Dot, Grid, Search }

/// <summary>
/// Lo que puede llevar una barra además de las ventanas abiertas. Cada barra tiene su lista, en
/// orden (Hub › Elementos de la barra); Inicio va siempre delante de las ventanas.
/// </summary>
public enum BarItem { Start, Media, Tray, Network, Bluetooth, Brightness, Volume, Battery, Cpu, Gpu, Ram, Fps, FreeRam, Clock }

/// <summary>Esquina de la pantalla donde aparecen los avisos.</summary>
public enum NotificationCorner { BottomRight, BottomLeft, TopRight, TopLeft }

/// <summary>Cómo aparecen y se cierran los paneles, el menú Inicio y los avisos.</summary>
public enum PanelAnimation { Slide, Fade, Zoom, Pop, Rise, None }

/// <summary>Duración de las animaciones.</summary>
public enum AnimationSpeed { Fast, Normal, Slow }

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

    /// <summary>Segunda barra de cada monitor (clave: nombre del dispositivo); ninguna si no está.</summary>
    public Dictionary<string, SecondBarContent> SecondTaskbars { get; set; } = new();

    public SecondBarContent SecondBarOf(string screen) =>
        SecondTaskbars.TryGetValue(screen, out var content) ? content : SecondBarContent.None;

    /// <summary>Borde opuesto (donde va la segunda barra).</summary>
    public static TaskbarPosition Opposite(TaskbarPosition position) => position switch
    {
        TaskbarPosition.Top => TaskbarPosition.Bottom,
        TaskbarPosition.Left => TaskbarPosition.Right,
        TaskbarPosition.Right => TaskbarPosition.Left,
        _ => TaskbarPosition.Top,
    };
    /// <summary>
    /// Elementos de cada barra en orden (clave: <see cref="BarLayout.Id"/>, "\\.\DISPLAY1|1"). Una
    /// barra sin entrada usa los de siempre (<see cref="BarLayout.Defaults"/>).
    /// </summary>
    public Dictionary<string, List<BarItem>> BarItems { get; set; } = new();

    public int TaskbarHeight { get; set; } = 48;
    /// <summary>Un solo botón por app con todas sus ventanas (como "Combinar botones" de Windows).</summary>
    public bool CombineTaskWindows { get; set; }
    /// <summary>Vista previa en vivo de la ventana al pasar el mouse por su botón.</summary>
    public bool ShowTaskPreviews { get; set; } = true;
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
    /// <summary>Solo aparece si algún monitor permite cambiar el brillo (DDC/CI o pantalla integrada).</summary>
    public bool ShowBrightnessModule { get; set; } = true;
    /// <summary>Lo que suena ahora (carátula, título y artista); solo aparece si hay algo reproduciéndose.</summary>
    public bool ShowMediaModule { get; set; } = true;
    /// <summary>Aviso en pantalla al cambiar volumen o brillo y al pulsar Bloq Mayús, Bloq Num o Bloq Despl.</summary>
    public bool ShowOsd { get; set; } = true;
    /// <summary>Porcentaje dentro del ícono de la batería.</summary>
    public bool ShowBatteryPercent { get; set; }
    /// <summary>Botones anterior, reproducir/pausar y siguiente junto a la canción.</summary>
    public bool ShowMediaControls { get; set; } = true;
    /// <summary>Temperatura junto al uso de la CPU y de la GPU (necesita las lecturas avanzadas).</summary>
    public bool ShowTemperatures { get; set; } = true;
    /// <summary>Monitor cuyo juego o app se mide en el contador de FPS (nombre de dispositivo); null = el principal de WINX.</summary>
    public string? FpsScreen { get; set; }
    /// <summary>
    /// Lecturas avanzadas (temperaturas y FPS, con administrador) al abrir WINX; si no, empiezan al
    /// hacer clic en el contador de FPS.
    /// </summary>
    public bool SensorsAtStartup { get; set; }

    // Notificaciones
    /// <summary>Centro de notificaciones de WINX (desactivado: el reloj abre el de Windows).</summary>
    public bool NotificationsEnabled { get; set; } = true;
    /// <summary>Avisos emergentes cuando llega una notificación.</summary>
    public bool NotificationBanners { get; set; } = true;
    /// <summary>Monitor de los avisos (nombre de dispositivo); null = el principal de WINX.</summary>
    public string? NotificationScreen { get; set; }
    public NotificationCorner NotificationCorner { get; set; } = NotificationCorner.BottomRight;
    /// <summary>Segundos que se ve cada aviso.</summary>
    public int NotificationSeconds { get; set; } = 6;
    /// <summary>No molestar: las notificaciones llegan al centro, pero sin avisos.</summary>
    public bool NotificationDoNotDisturb { get; set; }
    /// <summary>
    /// Valor original de "ShowBanner" de cada app en Windows (null: no existía) antes de que WINX
    /// apagara sus avisos emergentes; se devuelve al salir o al desactivar el centro de WINX.
    /// </summary>
    public Dictionary<string, int?> WindowsBannerBackup { get; set; } = new();

    // Actualizaciones
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>Idioma de la interfaz: "auto" (el de Windows), "es", "en"…</summary>
    public string Language { get; set; } = "auto";

    // Apariencia
    public WinxTheme Theme { get; set; } = WinxTheme.System;
    /// <summary>Color "#RRGGBB"; null = usar el acento de Windows.</summary>
    public string? AccentColor { get; set; }
    /// <summary>Acento sacado del fondo de pantalla (tiene prioridad sobre <see cref="AccentColor"/>).</summary>
    public bool AccentFromWallpaper { get; set; }
    /// <summary>
    /// Acento de Windows antes de que WINX le pusiera el suyo ("Aplicar el tema a Windows"):
    /// [color de Inicio, color de acento] en ABGR; se devuelve al desactivarlo. Null: no lo cambió.
    /// </summary>
    public uint[]? WindowsAccentBackup { get; set; }
    /// <summary>Liquid Glass con refracción real (captura del fondo + shader). Oculta la barra y
    /// los paneles de las capturas de pantalla; desactivado usa el desenfoque de Windows.</summary>
    public bool LiveGlass { get; set; } = true;
    /// <summary>Llevar el tema a Windows: modo claro/oscuro y fondo de las ventanas del Explorador.</summary>
    public bool ApplyThemeToWindows { get; set; }
    /// <summary>Barra sin fondo, borde ni desenfoque: solo se ven los íconos y el texto.</summary>
    public bool TaskbarFullyTransparent { get; set; }
    public PanelAnimation PanelAnimation { get; set; } = PanelAnimation.Slide;
    public AnimationSpeed AnimationSpeed { get; set; } = AnimationSpeed.Normal;
    /// <summary>Fondo que había antes de que WINX lo cambiara por primera vez.</summary>
    public string? OriginalWallpaper { get; set; }

    // Bandeja: null = primera ejecución (se importan los íconos promovidos en Windows).
    public string[]? PinnedTrayIcons { get; set; }
    public List<string> KnownTrayIcons { get; set; } = new();
    /// <summary>Orden de los íconos ocultos (identificadores); los que no están van al final.</summary>
    public List<string> HiddenTrayOrder { get; set; } = new();
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
