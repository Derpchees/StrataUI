using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services;

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

public enum StrataTheme
{
    System, Light, Dark, Transparent, LiquidGlass, LiquidGlassDark, Material,
    // Esquemas de color (ver Themes/ThemeCatalog)
    Midnight, Nord, Dracula, CatppuccinMocha, CatppuccinLatte, TokyoNight, RosePine, Gruvbox,
}

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

public enum StartIconKind { Strata, Windows, WindowsMono, Circle, Dot, Grid, Search }

/// <summary>
/// Lo que puede llevar una barra además de las ventanas abiertas. Cada barra tiene su lista, en
/// orden (Hub › Elementos de la barra); Inicio va siempre delante de las ventanas.
/// </summary>
public enum BarItem { Start, Media, Tray, Network, Bluetooth, Brightness, Volume, Battery, Cpu, Gpu, Ram, Fps, FreeRam, Clock, Notifications }

/// <summary>Widgets de escritorio (ver Services/Widgets).</summary>
public enum WidgetKind { Clock, Visualizer, Custom, Cpu, Gpu, Fps }

/// <summary>
/// Un widget del escritorio: su monitor y su posición como fracción del espacio libre del área de
/// trabajo (0 = arriba/izquierda, 1 = abajo/derecha), así sigue en su lugar si cambia la resolución.
/// </summary>
public sealed class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WidgetKind Kind { get; set; }
    public string? Device { get; set; }
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.25;
    /// <summary>Tamaño: 0,75 chico, 1 mediano, 1,5 grande.</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Reloj: la fecha debajo de la hora.</summary>
    public bool ShowDate { get; set; } = true;
    /// <summary>No se mueve al arrastrarlo.</summary>
    public bool Locked { get; set; }
    /// <summary>Widget importado (<see cref="WidgetKind.Custom"/>): el ID de su módulo.</summary>
    public string? Module { get; set; }
}

/// <summary>
/// Dónde va un elemento a lo largo de su barra: al final (derecha o abajo, lo de siempre), al
/// principio (izquierda o arriba) o en el centro.
/// </summary>
public enum BarZone { End, Start, Center }

/// <summary>Esquina de la pantalla donde aparecen los avisos.</summary>
public enum NotificationCorner { BottomRight, BottomLeft, TopRight, TopLeft }

/// <summary>Cómo aparecen y se cierran los paneles, el menú Inicio y los avisos.</summary>
public enum PanelAnimation { Slide, Fade, Zoom, Pop, Rise, None }

/// <summary>Duración de las animaciones.</summary>
public enum AnimationSpeed { Fast, Normal, Slow }

/// <summary>Preferencias de Strata UI, guardadas en %LOCALAPPDATA%\StrataUI\settings.json.</summary>
public sealed class StrataSettings
{
    // Barra de tareas
    public TaskbarAlignment TaskbarAlignment { get; set; } = TaskbarAlignment.Windows;
    /// <summary>Las apps abiertas quedan al centro de la barra aunque Inicio esté a la izquierda.</summary>
    public bool TaskbarCenterApps { get; set; }
    /// <summary>Monitor principal de Strata UI (bandeja, tecla Windows); null = el principal de Windows.</summary>
    public string? TaskbarMainScreen { get; set; }
    /// <summary>Red, Bluetooth, sonido, batería y reloj en todas las barras (false: solo en la principal).</summary>
    public bool StatusOnAllTaskbars { get; set; } = true;
    public TaskbarAppsMode TaskbarAppsMode { get; set; } = TaskbarAppsMode.Windows;
    /// <summary>Posición de la barra en cada monitor (clave: nombre del dispositivo, "\\.\DISPLAY1"); abajo si no está.</summary>
    public Dictionary<string, TaskbarPosition> TaskbarPositions { get; set; } = new();
    /// <summary>Monitores sin barra de Strata UI (nunca el principal: ahí va la bandeja).</summary>
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

    /// <summary>Ya se agregó la campana de notificaciones junto al reloj de las barras de antes (una sola vez).</summary>
    public bool NotificationBellAdded { get; set; }

    /// <summary>
    /// Zona de la barra de cada elemento (misma clave que <see cref="BarItems"/>); sin entrada, al
    /// final como siempre. Inicio va siempre con las ventanas.
    /// </summary>
    public Dictionary<string, Dictionary<BarItem, BarZone>> BarItemZones { get; set; } = new();

    /// <summary>
    /// Valores del perfil global de NVIDIA antes de cambiarlos desde Strata UI (id → valor; null = no
    /// estaba cambiado). Ver Services/Vendors/NvidiaSettings.
    /// </summary>
    public Dictionary<uint, uint?> NvidiaOriginals { get; set; } = new();

    /// <summary>Categorías marcadas en Optimización › Limpieza (null: las que vienen marcadas).</summary>
    public List<string>? CleanupSelection { get; set; }

    /// <summary>
    /// Monitores por identificador de hardware: el último nombre ("\\.\DISPLAYn") de cada uno, los que
    /// estaban conectados y lo guardado de los que no están (ver <see cref="MonitorKeys"/>).
    /// </summary>
    public Dictionary<string, string> MonitorNames { get; set; } = new();
    public List<string> MonitorsPresent { get; set; } = new();
    public Dictionary<string, MonitorSnapshot> ParkedMonitors { get; set; } = new();

    /// <summary>Widgets del escritorio (reloj, visualizador de música).</summary>
    public List<WidgetConfig> Widgets { get; set; } = new();

    /// <summary>Apps ancladas a la barra, en orden (IDs del menú Inicio o rutas; ver Services/TaskbarPins).</summary>
    public List<string> TaskbarPins { get; set; } = new();

    /// <summary>Ya se trajeron los anclados de la barra de Windows (solo la primera vez).</summary>
    public bool TaskbarPinsImported { get; set; }

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
    public StartIconKind StartIcon { get; set; } = StartIconKind.Strata;
    /// <summary>Botones del pie del menú, en orden (IDs de StartFooterCatalog).</summary>
    public List<string> StartFooterButtons { get; set; } = ["Explorer", "StrataSettings", "Power"];
    public bool StartUseWinKey { get; set; } = true;
    /// <summary>La tecla Windows abre Inicio en el monitor donde está el mouse (si ahí hay una barra con Inicio).</summary>
    public bool StartOnMouseMonitor { get; set; } = true;
    /// <summary>"Anclar a Inicio de Strata UI" en el menú contextual del Explorador (Services/StartMenu/ExplorerMenu).</summary>
    public bool StartExplorerMenu { get; set; } = true;
    /// <summary>Formato anterior de anclados (lista plana); se migra a <see cref="StartLayout"/>.</summary>
    public List<string>? PinnedStartApps { get; set; }
    /// <summary>Anclados organizados en secciones y carpetas; null = primera vez.</summary>
    public List<PinSection>? StartLayout { get; set; }
    /// <summary>Íconos elegidos para apps de Inicio: ID de la app → imagen (Services/StartMenu/CustomIcons).</summary>
    public Dictionary<string, string> StartCustomIcons { get; set; } = new();
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
    /// <summary>Monitor cuyo juego o app se mide en el contador de FPS (nombre de dispositivo); null = el principal de Strata UI.</summary>
    public string? FpsScreen { get; set; }
    /// <summary>
    /// Lecturas avanzadas (temperaturas y FPS, con administrador) al abrir Strata UI; si no, empiezan al
    /// hacer clic en el contador de FPS.
    /// </summary>
    public bool SensorsAtStartup { get; set; }

    // Notificaciones
    /// <summary>Centro de notificaciones de Strata UI (desactivado: el reloj abre el de Windows).</summary>
    public bool NotificationsEnabled { get; set; } = true;
    /// <summary>Avisos emergentes cuando llega una notificación.</summary>
    public bool NotificationBanners { get; set; } = true;
    /// <summary>Sonido de notificación de Windows con cada aviso emergente.</summary>
    public bool NotificationSounds { get; set; } = true;
    /// <summary>Monitor de los avisos (nombre de dispositivo); null = el principal de Strata UI.</summary>
    public string? NotificationScreen { get; set; }
    public NotificationCorner NotificationCorner { get; set; } = NotificationCorner.BottomRight;
    /// <summary>Segundos que se ve cada aviso.</summary>
    public int NotificationSeconds { get; set; } = 6;
    /// <summary>No molestar: las notificaciones llegan al centro, pero sin avisos.</summary>
    public bool NotificationDoNotDisturb { get; set; }
    /// <summary>
    /// Valor original de "ShowBanner" de cada app en Windows (null: no existía) antes de que Strata UI
    /// apagara sus avisos emergentes; se devuelve al salir o al desactivar el centro de Strata UI.
    /// </summary>
    public Dictionary<string, int?> WindowsBannerBackup { get; set; } = new();

    // Actualizaciones
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>Idioma de la interfaz: "auto" (el de Windows), "es", "en"…</summary>
    public string Language { get; set; } = "auto";

    // Apariencia
    public StrataTheme Theme { get; set; } = StrataTheme.System;
    /// <summary>Tema importado (ID del módulo); tiene prioridad sobre <see cref="Theme"/>.</summary>
    public string? ThemeModule { get; set; }
    /// <summary>Color "#RRGGBB"; null = usar el acento de Windows.</summary>
    public string? AccentColor { get; set; }
    /// <summary>Acento sacado del fondo de pantalla (tiene prioridad sobre <see cref="AccentColor"/>).</summary>
    public bool AccentFromWallpaper { get; set; }
    /// <summary>Color propio de la barra con su opacidad ("#AARRGGBB"); null = el del tema.</summary>
    public string? CustomBarColor { get; set; }
    /// <summary>Color propio de los paneles y el menú Inicio ("#AARRGGBB"); null = el del tema.</summary>
    public string? CustomPanelColor { get; set; }
    /// <summary>
    /// Acento de Windows antes de que Strata UI le pusiera el suyo ("Aplicar el tema a Windows"):
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
    /// <summary>Animación importada (ID del módulo); tiene prioridad sobre <see cref="PanelAnimation"/>.</summary>
    public string? AnimationModule { get; set; }
    /// <summary>Fondo que había antes de que Strata UI lo cambiara por primera vez.</summary>
    public string? OriginalWallpaper { get; set; }

    // Bandeja: null = primera ejecución (se importan los íconos promovidos en Windows).
    public string[]? PinnedTrayIcons { get; set; }
    public List<string> KnownTrayIcons { get; set; } = new();
    /// <summary>Orden de los íconos ocultos (identificadores); los que no están van al final.</summary>
    public List<string> HiddenTrayOrder { get; set; } = new();

    // Por monitor
    /// <summary>
    /// Ajustes de cada monitor (clave: nombre del dispositivo). Un monitor sin entrada usa los
    /// generales de arriba (<see cref="DefaultScreen"/>), que también son la plantilla de los nuevos.
    /// </summary>
    public Dictionary<string, ScreenSettings> Screens { get; set; } = new();

    /// <summary>Ajustes efectivos de un monitor (no los guarda: para cambiarlos, <see cref="Settings.UpdateScreen"/>).</summary>
    public ScreenSettings For(string? device) =>
        device != null && Screens.TryGetValue(device, out var screen) ? screen : DefaultScreen();

    /// <summary>Los ajustes generales como ajustes de un monitor (lo que tenía Strata UI antes de poder elegir por monitor).</summary>
    public ScreenSettings DefaultScreen() => new()
    {
        TaskbarAlignment = TaskbarAlignment,
        TaskbarCenterApps = TaskbarCenterApps,
        ShowTaskPreviews = ShowTaskPreviews,
        CombineTaskWindows = CombineTaskWindows,
        TaskbarHeight = TaskbarHeight,
        TaskbarFullyTransparent = TaskbarFullyTransparent,
        Theme = Theme,
        ThemeModule = ThemeModule,
        ShowSeconds = ShowSeconds,
        ShowDate = ShowDate,
        ShowTemperatures = ShowTemperatures,
        ShowBatteryPercent = ShowBatteryPercent,
        ShowMediaControls = ShowMediaControls,
        OsdVolume = ShowOsd,
        OsdMute = ShowOsd,
        OsdBrightness = ShowOsd,
        OsdCapsLock = ShowOsd,
        OsdNumLock = ShowOsd,
        OsdScrollLock = ShowOsd,
        NotificationCorner = NotificationCorner,
    };
}

/// <summary>
/// Lo que se elige por monitor en Hub › Personalización por monitor: su barra, su apariencia, sus
/// avisos, multimedia y avisos en pantalla. El menú Inicio, el acento, el fondo y las animaciones
/// son de todos los monitores.
/// </summary>
public sealed class ScreenSettings
{
    // Barra
    public TaskbarAlignment TaskbarAlignment { get; set; } = TaskbarAlignment.Windows;
    public bool TaskbarCenterApps { get; set; }
    public bool ShowTaskPreviews { get; set; } = true;
    public bool CombineTaskWindows { get; set; }
    public int TaskbarHeight { get; set; } = 48;
    public bool TaskbarFullyTransparent { get; set; }
    public bool ShowSeconds { get; set; }
    public bool ShowDate { get; set; } = true;
    public bool ShowTemperatures { get; set; } = true;
    public bool ShowBatteryPercent { get; set; }

    // Apariencia de la barra y de los paneles que se abren desde ella
    public StrataTheme Theme { get; set; } = StrataTheme.System;
    /// <summary>Tema importado (ID del módulo); tiene prioridad sobre <see cref="Theme"/>.</summary>
    public string? ThemeModule { get; set; }

    // Multimedia
    public bool ShowMediaControls { get; set; } = true;

    // Avisos en pantalla (OSD) junto a la barra de este monitor
    public bool OsdVolume { get; set; } = true;
    public bool OsdMute { get; set; } = true;
    public bool OsdBrightness { get; set; } = true;
    public bool OsdCapsLock { get; set; } = true;
    public bool OsdNumLock { get; set; } = true;
    public bool OsdScrollLock { get; set; } = true;

    /// <summary>Algún aviso en pantalla activo (el gancho de teclado se queda con las teclas de volumen).</summary>
    public bool AnyOsd => OsdVolume || OsdMute || OsdBrightness || OsdCapsLock || OsdNumLock || OsdScrollLock;

    // Notificaciones
    public NotificationCorner NotificationCorner { get; set; } = NotificationCorner.BottomRight;

    public ScreenSettings Clone() => (ScreenSettings)MemberwiseClone();
}

public static class Settings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "settings.json");

    public static StrataSettings Current { get; private set; } = Load();

    /// <summary>Se dispara tras cada <see cref="Update"/>; la UI se sincroniza escuchándolo.</summary>
    public static event Action? Changed;

    public static void Update(Action<StrataSettings> change)
    {
        change(Current);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Cambia los ajustes de un monitor (los crea a partir de los generales la primera vez).</summary>
    public static void UpdateScreen(string device, Action<ScreenSettings> change) => Update(s =>
    {
        if (!s.Screens.TryGetValue(device, out var screen))
            s.Screens[device] = screen = s.DefaultScreen();
        change(screen);
    });

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

    private static StrataSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StrataSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("Configuración inválida; se usan valores por defecto", ex);
        }
        return new();
    }
}
