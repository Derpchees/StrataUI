using Microsoft.Win32;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Catálogo de optimizaciones de Windows. Las opciones siguen las de Win11Debloat
/// (github.com/Raphire/Win11Debloat), adaptadas a interruptores reversibles.
/// Convención: en los interruptores, la posición "Activado" aplica la optimización.
/// </summary>
public static class TweakCatalog
{
    public const string Privacy = "privacy";
    public const string AI = "ai";
    public const string System = "system";
    public const string Update = "update";
    public const string Appearance = "appearance";
    public const string Explorer = "explorer";
    public const string Multitasking = "multitasking";
    public const string Gaming = "gaming";
    public const string Browsers = "browsers";
    public const string Performance = "performance";
    public const string Hardware = "hardware";

    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";

    /// <summary>El servicio está instalado (los ajustes de fabricante solo se muestran si existe).</summary>
    private static bool ServiceExists(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + name);
            return key != null;
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private const string Desktop = @"Control Panel\Desktop";
    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string RemovableDrivesKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\DelegateFolders\{F5FB2C77-0E2F-4A16-A381-3E560C68BC83}";
    private const string RemovableDrivesKey32 =
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\DelegateFolders\{F5FB2C77-0E2F-4A16-A381-3E560C68BC83}";

    // Interruptor sobre HKCU / HKLM: [valor sin optimizar, valor optimizado].
    private static RegSetting CU(string path, string name, object? off, object? on, object? missing = null,
                                 RegistryValueKind kind = RegistryValueKind.DWord, string? owner = null) =>
        new(Hive.CurrentUser, path, name, [off, on], missing ?? off, kind, owner);

    private static RegSetting LM(string path, string name, object? off, object? on, object? missing = null,
                                 RegistryValueKind kind = RegistryValueKind.DWord, string? owner = null) =>
        new(Hive.LocalMachine, path, name, [off, on], missing ?? off, kind, owner);

    // Política: no existe por defecto; activarla escribe el valor.
    private static RegSetting PolicyCU(string path, string name, object on) => CU(path, name, null, on);
    private static RegSetting PolicyLM(string path, string name, object on) => LM(path, name, null, on);

    public static IReadOnlyList<Tweak> All { get; } =
    [
        // ── Privacidad ─────────────────────────────────────────────────────────
        new("telemetry", Privacy, Loc.T("Desactivar telemetría"),
            Loc.T("Envía solo los datos de diagnóstico mínimos. En Enterprise, Education e IoT queda en nivel Seguridad."),
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)]),
        new("tailored-experiences", Privacy, Loc.T("Desactivar experiencias personalizadas"),
            Loc.T("Microsoft no usa tus datos de diagnóstico para mostrarte consejos y anuncios a medida."),
            [CU(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 1, 0)]),
        new("activity-history", Privacy, Loc.T("Desactivar historial de actividad"),
            Loc.T("No registra ni sube las apps, archivos y sitios que usas."),
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
             PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
             PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0)]),
        new("track-programs", Privacy, Loc.T("Desactivar seguimiento de apps iniciadas"),
            Loc.T("Windows deja de registrar qué apps abres para personalizar Inicio y la búsqueda."),
            [CU(Advanced, "Start_TrackProgs", 1, 0)]),
        new("advertising-id", Privacy, Loc.T("Desactivar ID de publicidad"),
            Loc.T("Las apps no pueden usar un identificador para mostrarte anuncios personalizados."),
            [CU(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, 0)]),
        new("tips", Privacy, Loc.T("Desactivar consejos y sugerencias"),
            Loc.T("Sin notificaciones de consejos, trucos y sugerencias al usar Windows."),
            [CU(ContentDelivery, "SubscribedContent-338389Enabled", 1, 0),
             CU(ContentDelivery, "SoftLandingEnabled", 1, 0)]),
        new("start-suggestions", Privacy, Loc.T("Desactivar sugerencias de apps"),
            Loc.T("Quita las apps sugeridas (anuncios) en Inicio."),
            [CU(ContentDelivery, "SystemPaneSuggestionsEnabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-338388Enabled", 1, 0)]),
        new("settings-suggestions", Privacy, Loc.T("Desactivar sugerencias en Configuración"),
            Loc.T("Quita el contenido sugerido de la app Configuración."),
            [CU(ContentDelivery, "SubscribedContent-338393Enabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-353694Enabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-353696Enabled", 1, 0)]),
        new("lockscreen-ads", Privacy, Loc.T("Desactivar anuncios en la pantalla de bloqueo"),
            Loc.T("Sin datos curiosos, consejos ni anuncios sobre la pantalla de bloqueo."),
            [CU(ContentDelivery, "RotatingLockScreenOverlayEnabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-338387Enabled", 1, 0)]),
        new("silent-apps", Privacy, Loc.T("Evitar instalación automática de apps sugeridas"),
            Loc.T("Windows deja de instalar apps promocionadas (juegos, redes sociales) por su cuenta."),
            [CU(ContentDelivery, "SilentInstalledAppsEnabled", 1, 0),
             CU(ContentDelivery, "PreInstalledAppsEnabled", 1, 0),
             CU(ContentDelivery, "OemPreInstalledAppsEnabled", 1, 0)]),
        new("welcome-experience", Privacy, Loc.T("Desactivar \"Terminar de configurar el dispositivo\""),
            Loc.T("Sin pantallas de bienvenida que ofrecen servicios de Microsoft tras las actualizaciones."),
            [CU(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-310093Enabled", 1, 0)]),
        new("bing-search", Privacy, Loc.T("Desactivar búsqueda web (Bing) en Windows"),
            Loc.T("La búsqueda de Windows solo busca en tu equipo."),
            [PolicyCU(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)],
            Refresh: TweakRefresh.Explorer),
        new("settings-home", Privacy, Loc.T("Ocultar la página Inicio de Configuración"),
            Loc.T("Quita la página con anuncios de Microsoft 365 y Game Pass de la app Configuración."),
            [LM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility",
                null, "hide:home", kind: RegistryValueKind.String)]),
        new("location", Privacy, Loc.T("Desactivar ubicación"),
            Loc.T("Ninguna app puede acceder a tu ubicación. Nota: también impide buscar redes Wi-Fi desde Strata UI."),
            [LM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value",
                "Allow", "Deny", kind: RegistryValueKind.String)]),

        // ── Inteligencia artificial ────────────────────────────────────────────
        new("copilot", AI, Loc.T("Desactivar Copilot"),
            Loc.T("Quita Copilot de Windows."),
            [PolicyCU(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)]),
        new("recall", AI, Loc.T("Desactivar Recall"),
            Loc.T("Windows no guarda capturas de tu actividad para buscarlas después."),
            [PolicyCU(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)]),
        new("click-to-do", AI, Loc.T("Desactivar Click to Do"),
            Loc.T("Quita las acciones de IA sobre el contenido de la pantalla."),
            [PolicyCU(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1)]),
        new("paint-ai", AI, Loc.T("Desactivar IA en Paint"),
            Loc.T("Quita Cocreator, Relleno generativo y Creador de imágenes."),
            [PolicyLM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableCocreator", 1),
             PolicyLM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableGenerativeFill", 1),
             PolicyLM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableImageCreator", 1)]),
        new("notepad-ai", AI, Loc.T("Desactivar IA en el Bloc de notas"),
            Loc.T("Quita Reescribir y las demás funciones de IA del Bloc de notas."),
            [PolicyLM(@"SOFTWARE\Policies\WindowsNotepad", "DisableAIFeatures", 1)]),
        new("edge-sidebar", AI, Loc.T("Ocultar Copilot y la barra lateral de Edge"),
            Loc.T("Quita el botón de Copilot y la barra lateral de Microsoft Edge."),
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0)]),

        // ── Sistema ────────────────────────────────────────────────────────────
        new("classic-context-menu", System, Loc.T("Menú contextual clásico"),
            Loc.T("Muestra el menú completo al hacer clic derecho, sin pasar por \"Mostrar más opciones\"."),
            [CU(ClassicMenuKey + @"\InprocServer32", "", null, "", kind: RegistryValueKind.String, owner: ClassicMenuKey)],
            Refresh: TweakRefresh.Explorer),
        new("mouse-acceleration", System, Loc.T("Desactivar aceleración del mouse"),
            Loc.T("Apaga \"Mejorar la precisión del puntero\": el cursor se mueve igual sin importar la velocidad."),
            [CU(@"Control Panel\Mouse", "MouseSpeed", "1", "0", kind: RegistryValueKind.String),
             CU(@"Control Panel\Mouse", "MouseThreshold1", "6", "0", kind: RegistryValueKind.String),
             CU(@"Control Panel\Mouse", "MouseThreshold2", "10", "0", kind: RegistryValueKind.String)],
            Refresh: TweakRefresh.Mouse),
        new("sticky-keys", System, Loc.T("Desactivar el atajo de Teclas especiales"),
            Loc.T("Pulsar Mayús cinco veces ya no abre el aviso de Teclas especiales."),
            [CU(@"Control Panel\Accessibility\StickyKeys", "Flags", "510", "506", kind: RegistryValueKind.String)],
            Refresh: TweakRefresh.StickyKeys),
        new("storage-sense", System, Loc.T("Desactivar Sensor de almacenamiento"),
            Loc.T("Windows no borra archivos temporales ni la papelera automáticamente."),
            [CU(@"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1, 0, missing: 0)]),
        new("fast-startup", System, Loc.T("Desactivar inicio rápido"),
            Loc.T("Apagar cierra Windows por completo (evita problemas con controladores y arranque dual)."),
            [LM(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, 0)]),
        new("bitlocker-auto", System, Loc.T("Evitar cifrado automático del dispositivo"),
            Loc.T("Windows no activa BitLocker por su cuenta. No descifra unidades ya cifradas."),
            [PolicyLM(@"SYSTEM\CurrentControlSet\Control\BitLocker", "PreventDeviceEncryption", 1)]),

        // ── Windows Update ─────────────────────────────────────────────────────
        new("no-auto-reboot", Update, Loc.T("No reiniciar automáticamente"),
            Loc.T("Windows Update no reinicia el equipo mientras tengas la sesión iniciada."),
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1)]),
        new("delivery-optimization", Update, Loc.T("No compartir actualizaciones con otros equipos"),
            Loc.T("Desactiva la optimización de distribución: las actualizaciones se descargan solo de Microsoft."),
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)]),
        new("continuous-innovation", Update, Loc.T("No recibir novedades en cuanto salgan"),
            Loc.T("Desactiva \"Obtener las actualizaciones más recientes en cuanto estén disponibles\"."),
            [LM(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "IsContinuousInnovationOptedIn", 1, 0, missing: 0)]),
        new("device-apps", Update, Loc.T("No instalar apps complementarias de dispositivos"),
            Loc.T("Evita que se descarguen apps de fabricantes al conectar monitores, mouses, etc."),
            [LM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 0, 1)]),

        // ── Apariencia de Windows ──────────────────────────────────────────────
        new("dark-apps", Appearance, Loc.T("Modo oscuro en aplicaciones"),
            Loc.T("Las apps que lo soportan usan el tema oscuro."),
            [CU(Personalize, "AppsUseLightTheme", 1, 0)], Refresh: TweakRefresh.Theme),
        new("dark-system", Appearance, Loc.T("Modo oscuro en Windows"),
            Loc.T("Menús, Inicio y paneles del sistema en oscuro."),
            [CU(Personalize, "SystemUsesLightTheme", 1, 0)], Refresh: TweakRefresh.Theme),
        new("no-transparency", Appearance, Loc.T("Desactivar transparencia"),
            Loc.T("Fondos sólidos en lugar de efectos translúcidos."),
            [CU(Personalize, "EnableTransparency", 1, 0)], Refresh: TweakRefresh.Theme),
        new("accent-titlebars", Appearance, Loc.T("Color de acento en barras de título"),
            Loc.T("Las barras de título y bordes de las ventanas usan tu color de acento."),
            [CU(@"Software\Microsoft\Windows\DWM", "ColorPrevalence", 0, 1)], Refresh: TweakRefresh.Theme),
        new("spotlight-icon", Appearance, Loc.T("Ocultar \"Más información sobre esta imagen\""),
            Loc.T("Quita el ícono de Windows Spotlight del escritorio."),
            [PolicyCU(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
                      "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}", 1)],
            Refresh: TweakRefresh.Explorer),

        // ── Explorador de archivos ─────────────────────────────────────────────
        new("file-extensions", Explorer, Loc.T("Mostrar extensiones de archivo"),
            Loc.T("Muestra .exe, .pdf, .jpg… en los nombres de archivo."),
            [CU(Advanced, "HideFileExt", 1, 0)]),
        new("hidden-files", Explorer, Loc.T("Mostrar archivos ocultos"),
            Loc.T("Muestra archivos, carpetas y unidades ocultas."),
            [CU(Advanced, "Hidden", 2, 1)]),
        new("system-files", Explorer, Loc.T("Mostrar archivos protegidos del sistema"),
            Loc.T("Muestra archivos del sistema operativo. Úsalo con cuidado."),
            [CU(Advanced, "ShowSuperHidden", 0, 1)]),
        new("launch-to", Explorer, Loc.T("Abrir el Explorador en"),
            Loc.T("Carpeta que se muestra al abrir el Explorador."),
            [new RegSetting(Hive.CurrentUser, Advanced, "LaunchTo", [2, 1, 3], 2)],
            Options: [Loc.T("Inicio del Explorador"), Loc.T("Este equipo"), Loc.T("Descargas")]),
        new("compact-mode", Explorer, Loc.T("Vista compacta"),
            Loc.T("Reduce el espacio entre elementos."),
            [CU(Advanced, "UseCompactMode", 0, 1)]),
        new("item-checkboxes", Explorer, Loc.T("Casillas para seleccionar elementos"),
            Loc.T("Muestra una casilla junto a cada archivo para seleccionar varios."),
            [CU(Advanced, "AutoCheckSelect", 0, 1)]),
        new("hide-home", Explorer, Loc.T("Ocultar \"Inicio\" del panel de navegación"),
            Loc.T("Quita la sección Inicio (archivos recientes y favoritos)."),
            [PolicyCU(@"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}", "System.IsPinnedToNameSpaceTree", 0)
                 with { OwnerKey = @"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}" }],
            Refresh: TweakRefresh.Explorer),
        new("hide-gallery", Explorer, Loc.T("Ocultar \"Galería\" del panel de navegación"),
            Loc.T("Quita la sección Galería de fotos."),
            [PolicyCU(@"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}", "System.IsPinnedToNameSpaceTree", 0)
                 with { OwnerKey = @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}" }],
            Refresh: TweakRefresh.Explorer),
        new("removable-drive-duplicates", Explorer, Loc.T("Quitar unidades extraíbles duplicadas"),
            Loc.T("Las memorias USB aparecen solo dentro de \"Este equipo\", no repetidas en el panel."),
            // Aquí la optimización *borra* la clave, y "no existe" significa "ya optimizado".
            [new RegSetting(Hive.LocalMachine, RemovableDrivesKey, "", ["Removable Drives", null], null,
                            RegistryValueKind.String, RemovableDrivesKey),
             new RegSetting(Hive.LocalMachine, RemovableDrivesKey32, "", ["Removable Drives", null], null,
                            RegistryValueKind.String, RemovableDrivesKey32)],
            Refresh: TweakRefresh.Explorer),

        // ── Multitarea ─────────────────────────────────────────────────────────
        new("snap", Multitasking, Loc.T("Desactivar ajuste de ventanas (Snap)"),
            Loc.T("Arrastrar una ventana al borde ya no la acomoda automáticamente."),
            [CU(@"Control Panel\Desktop", "WindowArrangementActive", "1", "0", kind: RegistryValueKind.String)],
            Refresh: TweakRefresh.Snap),
        new("snap-assist", Multitasking, Loc.T("Desactivar sugerencias al ajustar"),
            Loc.T("Al ajustar una ventana no se ofrecen las demás para llenar el espacio."),
            [CU(Advanced, "SnapAssist", 1, 0)]),
        new("snap-layouts", Multitasking, Loc.T("Desactivar diseños de ajuste"),
            Loc.T("No aparecen los diseños al pasar el mouse sobre el botón Maximizar."),
            [CU(Advanced, "EnableSnapAssistFlyout", 1, 0)]),
        new("alt-tab", Multitasking, Loc.T("Pestañas de Edge en Alt+Tab"),
            Loc.T("Qué muestra Alt+Tab además de las ventanas abiertas."),
            [new RegSetting(Hive.CurrentUser, Advanced, "MultiTaskingAltTabFilter", [3, 0, 1, 2], 3)],
            Options: [Loc.T("Solo ventanas"), Loc.T("20 pestañas"), Loc.T("5 pestañas"), Loc.T("3 pestañas")]),

        // ── Juegos ─────────────────────────────────────────────────────────────
        new("game-dvr", Gaming, Loc.T("Desactivar grabación de juegos (Game DVR)"),
            Loc.T("Xbox Game Bar deja de grabar en segundo plano; puede mejorar el rendimiento."),
            [CU(@"System\GameConfigStore", "GameDVR_Enabled", 1, 0),
             CU(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1, 0)]),
        new("game-bar-button", Gaming, Loc.T("No abrir Game Bar con el botón Xbox"),
            Loc.T("El botón Xbox del control ya no abre Xbox Game Bar."),
            [CU(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 1, 0)]),

        // ── Rendimiento ────────────────────────────────────────────────────────
        // Ajustes de respuesta y prioridad: ninguno quita funciones; los que pesan en algo lo dicen.
        new("menu-delay", Performance, Loc.T("Menús instantáneos"),
            Loc.T("Los submenús se abren sin la espera de 0,4 s de Windows."),
            [CU(Desktop, "MenuShowDelay", "400", "0", kind: RegistryValueKind.String)]),
        new("hover-time", Performance, Loc.T("Respuesta más rápida al pasar el mouse"),
            Loc.T("Las vistas previas y los tooltips aparecen antes (150 ms en lugar de 400)."),
            [CU(@"Control Panel\Mouse", "MouseHoverTime", "400", "150", kind: RegistryValueKind.String)]),
        new("min-animate", Performance, Loc.T("Sin animación al minimizar y maximizar"),
            Loc.T("Las ventanas cambian de tamaño al instante. Se aplica al volver a iniciar sesión."),
            [CU(Desktop + @"\WindowMetrics", "MinAnimate", "1", "0", kind: RegistryValueKind.String)]),
        new("startup-delay", Performance, Loc.T("Sin espera para las apps de inicio"),
            Loc.T("Windows abre las apps de inicio en cuanto inicias sesión, sin retrasarlas unos segundos."),
            [CU(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", null, 0)]),
        new("hung-apps", Performance, Loc.T("Cerrar antes las apps que no responden"),
            Loc.T("Windows da por colgada una app a los 2 s (en lugar de 5) y apagar espera 5 s por cada una (en lugar de 20)."),
            [CU(Desktop, "HungAppTimeout", "5000", "2000", kind: RegistryValueKind.String),
             CU(Desktop, "WaitToKillAppTimeout", "20000", "5000", kind: RegistryValueKind.String)]),
        new("power-throttling", Performance, Loc.T("Desactivar la limitación de energía"),
            Loc.T("Windows deja de frenar las apps en segundo plano para ahorrar energía. En portátiles gasta más batería."),
            [LM(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", null, 1)]),
        new("hags", Performance, Loc.T("Programación de GPU acelerada por hardware"),
            Loc.T("La tarjeta gráfica administra su propia memoria: menos latencia en juegos y video. Se aplica al reiniciar."),
            [LM(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1, 2, missing: 1)]),
        new("games-priority", Performance, Loc.T("Más prioridad para juegos"),
            Loc.T("Los juegos reciben más tiempo de procesador y prioridad de disco que las tareas de fondo."),
            [LM(SystemProfile + @"\Tasks\Games", "Priority", 2, 6),
             LM(SystemProfile + @"\Tasks\Games", "Scheduling Category", "Medium", "High", kind: RegistryValueKind.String),
             LM(SystemProfile + @"\Tasks\Games", "SFIO Priority", "Normal", "High", kind: RegistryValueKind.String)]),
        new("system-responsiveness", Performance, Loc.T("Menos reserva para tareas multimedia de fondo"),
            Loc.T("Windows reserva el 10 % del procesador (en lugar del 20 %) y no limita la red mientras suena audio o video."),
            [LM(SystemProfile, "SystemResponsiveness", 20, 10),
             LM(SystemProfile, "NetworkThrottlingIndex", 10, unchecked((int)0xFFFFFFFF))]),
        new("background-apps", Performance, Loc.T("Impedir que las apps de la Tienda corran en segundo plano"),
            Loc.T("Ahorra memoria y procesador. Ojo: esas apps (WhatsApp de la Tienda, por ejemplo) dejan de avisar cuando están cerradas."),
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2)]),
        new("search-indexing", Performance, Loc.T("Desactivar la indexación de búsqueda"),
            Loc.T("Menos uso de disco en segundo plano. La búsqueda del Explorador se vuelve más lenta (la de Inicio de Strata UI no cambia). Se aplica al reiniciar."),
            [LM(@"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 2, 4)]),

        // ── Hardware: servicios de los fabricantes (solo si están instalados) ──
        // Se desactivan en el registro (como el Administrador de servicios): rigen desde el próximo inicio.
        new("nvidia-telemetry", Hardware, Loc.T("NVIDIA: desactivar la telemetría"),
            Loc.T("El servicio que envía a NVIDIA datos de uso del controlador. Se aplica al reiniciar."),
            [LM(ServicesKey + "NvTelemetryContainer", "Start", 2, 4)],
            Available: () => ServiceExists("NvTelemetryContainer")),
        new("amd-uep", Hardware, Loc.T("AMD: desactivar el Programa de experiencia del usuario"),
            Loc.T("El servicio que envía a AMD datos de uso de AMD Software. Se aplica al reiniciar."),
            [LM(ServicesKey + "AUEPLauncher", "Start", 2, 4)],
            Available: () => ServiceExists("AUEPLauncher")),
        new("intel-cip", Hardware, Loc.T("Intel: desactivar el Programa de mejora del equipo"),
            Loc.T("Los servicios del Intel Computing Improvement Program, que envían datos de uso a Intel. Se aplica al reiniciar."),
            [LM(ServicesKey + "ESRV_SVC_QUEENCREEK", "Start", 2, 4),
             LM(ServicesKey + "SystemUsageReportSvc_QUEENCREEK", "Start", 2, 4)],
            Available: () => ServiceExists("ESRV_SVC_QUEENCREEK") && ServiceExists("SystemUsageReportSvc_QUEENCREEK")),

        // ── Navegadores ────────────────────────────────────────────────────────
        new("brave-bloat", Browsers, Loc.T("Brave: quitar Rewards, Wallet, VPN y Leo"),
            Loc.T("Desactiva por política las funciones extra de Brave (cripto, VPN e IA)."),
            [PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", 1),
             PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveWalletDisabled", 1),
             PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveVPNDisabled", 1),
             PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveAIChatEnabled", 0)]),
    ];
}
