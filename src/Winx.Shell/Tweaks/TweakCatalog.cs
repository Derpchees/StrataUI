using Microsoft.Win32;

namespace Winx.Shell.Tweaks;

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
        new("telemetry", Privacy, "Desactivar telemetría",
            "Envía solo los datos de diagnóstico mínimos. En Enterprise, Education e IoT queda en nivel Seguridad.",
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)]),
        new("tailored-experiences", Privacy, "Desactivar experiencias personalizadas",
            "Microsoft no usa tus datos de diagnóstico para mostrarte consejos y anuncios a medida.",
            [CU(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 1, 0)]),
        new("activity-history", Privacy, "Desactivar historial de actividad",
            "No registra ni sube las apps, archivos y sitios que usas.",
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
             PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
             PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0)]),
        new("track-programs", Privacy, "Desactivar seguimiento de apps iniciadas",
            "Windows deja de registrar qué apps abres para personalizar Inicio y la búsqueda.",
            [CU(Advanced, "Start_TrackProgs", 1, 0)]),
        new("advertising-id", Privacy, "Desactivar ID de publicidad",
            "Las apps no pueden usar un identificador para mostrarte anuncios personalizados.",
            [CU(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, 0)]),
        new("tips", Privacy, "Desactivar consejos y sugerencias",
            "Sin notificaciones de consejos, trucos y sugerencias al usar Windows.",
            [CU(ContentDelivery, "SubscribedContent-338389Enabled", 1, 0),
             CU(ContentDelivery, "SoftLandingEnabled", 1, 0)]),
        new("start-suggestions", Privacy, "Desactivar sugerencias de apps",
            "Quita las apps sugeridas (anuncios) en Inicio.",
            [CU(ContentDelivery, "SystemPaneSuggestionsEnabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-338388Enabled", 1, 0)]),
        new("settings-suggestions", Privacy, "Desactivar sugerencias en Configuración",
            "Quita el contenido sugerido de la app Configuración.",
            [CU(ContentDelivery, "SubscribedContent-338393Enabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-353694Enabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-353696Enabled", 1, 0)]),
        new("lockscreen-ads", Privacy, "Desactivar anuncios en la pantalla de bloqueo",
            "Sin datos curiosos, consejos ni anuncios sobre la pantalla de bloqueo.",
            [CU(ContentDelivery, "RotatingLockScreenOverlayEnabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-338387Enabled", 1, 0)]),
        new("silent-apps", Privacy, "Evitar instalación automática de apps sugeridas",
            "Windows deja de instalar apps promocionadas (juegos, redes sociales) por su cuenta.",
            [CU(ContentDelivery, "SilentInstalledAppsEnabled", 1, 0),
             CU(ContentDelivery, "PreInstalledAppsEnabled", 1, 0),
             CU(ContentDelivery, "OemPreInstalledAppsEnabled", 1, 0)]),
        new("welcome-experience", Privacy, "Desactivar \"Terminar de configurar el dispositivo\"",
            "Sin pantallas de bienvenida que ofrecen servicios de Microsoft tras las actualizaciones.",
            [CU(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 1, 0),
             CU(ContentDelivery, "SubscribedContent-310093Enabled", 1, 0)]),
        new("bing-search", Privacy, "Desactivar búsqueda web (Bing) en Windows",
            "La búsqueda de Windows solo busca en tu equipo.",
            [PolicyCU(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)],
            Refresh: TweakRefresh.Explorer),
        new("settings-home", Privacy, "Ocultar la página Inicio de Configuración",
            "Quita la página con anuncios de Microsoft 365 y Game Pass de la app Configuración.",
            [LM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "SettingsPageVisibility",
                null, "hide:home", kind: RegistryValueKind.String)]),
        new("location", Privacy, "Desactivar ubicación",
            "Ninguna app puede acceder a tu ubicación. Nota: también impide buscar redes Wi-Fi desde WINX.",
            [LM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value",
                "Allow", "Deny", kind: RegistryValueKind.String)]),

        // ── Inteligencia artificial ────────────────────────────────────────────
        new("copilot", AI, "Desactivar Copilot",
            "Quita Copilot de Windows.",
            [PolicyCU(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)]),
        new("recall", AI, "Desactivar Recall",
            "Windows no guarda capturas de tu actividad para buscarlas después.",
            [PolicyCU(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)]),
        new("click-to-do", AI, "Desactivar Click to Do",
            "Quita las acciones de IA sobre el contenido de la pantalla.",
            [PolicyCU(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1)]),
        new("paint-ai", AI, "Desactivar IA en Paint",
            "Quita Cocreator, Relleno generativo y Creador de imágenes.",
            [PolicyLM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableCocreator", 1),
             PolicyLM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableGenerativeFill", 1),
             PolicyLM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableImageCreator", 1)]),
        new("notepad-ai", AI, "Desactivar IA en el Bloc de notas",
            "Quita Reescribir y las demás funciones de IA del Bloc de notas.",
            [PolicyLM(@"SOFTWARE\Policies\WindowsNotepad", "DisableAIFeatures", 1)]),
        new("edge-sidebar", AI, "Ocultar Copilot y la barra lateral de Edge",
            "Quita el botón de Copilot y la barra lateral de Microsoft Edge.",
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0)]),

        // ── Sistema ────────────────────────────────────────────────────────────
        new("classic-context-menu", System, "Menú contextual clásico",
            "Muestra el menú completo al hacer clic derecho, sin pasar por \"Mostrar más opciones\".",
            [CU(ClassicMenuKey + @"\InprocServer32", "", null, "", kind: RegistryValueKind.String, owner: ClassicMenuKey)],
            Refresh: TweakRefresh.Explorer),
        new("mouse-acceleration", System, "Desactivar aceleración del mouse",
            "Apaga \"Mejorar la precisión del puntero\": el cursor se mueve igual sin importar la velocidad.",
            [CU(@"Control Panel\Mouse", "MouseSpeed", "1", "0", kind: RegistryValueKind.String),
             CU(@"Control Panel\Mouse", "MouseThreshold1", "6", "0", kind: RegistryValueKind.String),
             CU(@"Control Panel\Mouse", "MouseThreshold2", "10", "0", kind: RegistryValueKind.String)],
            Refresh: TweakRefresh.Mouse),
        new("sticky-keys", System, "Desactivar el atajo de Teclas especiales",
            "Pulsar Mayús cinco veces ya no abre el aviso de Teclas especiales.",
            [CU(@"Control Panel\Accessibility\StickyKeys", "Flags", "510", "506", kind: RegistryValueKind.String)],
            Refresh: TweakRefresh.StickyKeys),
        new("storage-sense", System, "Desactivar Sensor de almacenamiento",
            "Windows no borra archivos temporales ni la papelera automáticamente.",
            [CU(@"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1, 0, missing: 0)]),
        new("fast-startup", System, "Desactivar inicio rápido",
            "Apagar cierra Windows por completo (evita problemas con controladores y arranque dual).",
            [LM(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, 0)]),
        new("bitlocker-auto", System, "Evitar cifrado automático del dispositivo",
            "Windows no activa BitLocker por su cuenta. No descifra unidades ya cifradas.",
            [PolicyLM(@"SYSTEM\CurrentControlSet\Control\BitLocker", "PreventDeviceEncryption", 1)]),

        // ── Windows Update ─────────────────────────────────────────────────────
        new("no-auto-reboot", Update, "No reiniciar automáticamente",
            "Windows Update no reinicia el equipo mientras tengas la sesión iniciada.",
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1)]),
        new("delivery-optimization", Update, "No compartir actualizaciones con otros equipos",
            "Desactiva la optimización de distribución: las actualizaciones se descargan solo de Microsoft.",
            [PolicyLM(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)]),
        new("continuous-innovation", Update, "No recibir novedades en cuanto salgan",
            "Desactiva \"Obtener las actualizaciones más recientes en cuanto estén disponibles\".",
            [LM(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "IsContinuousInnovationOptedIn", 1, 0, missing: 0)]),
        new("device-apps", Update, "No instalar apps complementarias de dispositivos",
            "Evita que se descarguen apps de fabricantes al conectar monitores, mouses, etc.",
            [LM(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 0, 1)]),

        // ── Apariencia de Windows ──────────────────────────────────────────────
        new("dark-apps", Appearance, "Modo oscuro en aplicaciones",
            "Las apps que lo soportan usan el tema oscuro.",
            [CU(Personalize, "AppsUseLightTheme", 1, 0)], Refresh: TweakRefresh.Theme),
        new("dark-system", Appearance, "Modo oscuro en Windows",
            "Menús, Inicio y paneles del sistema en oscuro.",
            [CU(Personalize, "SystemUsesLightTheme", 1, 0)], Refresh: TweakRefresh.Theme),
        new("no-transparency", Appearance, "Desactivar transparencia",
            "Fondos sólidos en lugar de efectos translúcidos.",
            [CU(Personalize, "EnableTransparency", 1, 0)], Refresh: TweakRefresh.Theme),
        new("accent-titlebars", Appearance, "Color de acento en barras de título",
            "Las barras de título y bordes de las ventanas usan tu color de acento.",
            [CU(@"Software\Microsoft\Windows\DWM", "ColorPrevalence", 0, 1)], Refresh: TweakRefresh.Theme),
        new("spotlight-icon", Appearance, "Ocultar \"Más información sobre esta imagen\"",
            "Quita el ícono de Windows Spotlight del escritorio.",
            [PolicyCU(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
                      "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}", 1)],
            Refresh: TweakRefresh.Explorer),

        // ── Explorador de archivos ─────────────────────────────────────────────
        new("file-extensions", Explorer, "Mostrar extensiones de archivo",
            "Muestra .exe, .pdf, .jpg… en los nombres de archivo.",
            [CU(Advanced, "HideFileExt", 1, 0)]),
        new("hidden-files", Explorer, "Mostrar archivos ocultos",
            "Muestra archivos, carpetas y unidades ocultas.",
            [CU(Advanced, "Hidden", 2, 1)]),
        new("system-files", Explorer, "Mostrar archivos protegidos del sistema",
            "Muestra archivos del sistema operativo. Úsalo con cuidado.",
            [CU(Advanced, "ShowSuperHidden", 0, 1)]),
        new("launch-to", Explorer, "Abrir el Explorador en",
            "Carpeta que se muestra al abrir el Explorador.",
            [new RegSetting(Hive.CurrentUser, Advanced, "LaunchTo", [2, 1, 3], 2)],
            Options: ["Inicio", "Este equipo", "Descargas"]),
        new("compact-mode", Explorer, "Vista compacta",
            "Reduce el espacio entre elementos.",
            [CU(Advanced, "UseCompactMode", 0, 1)]),
        new("item-checkboxes", Explorer, "Casillas para seleccionar elementos",
            "Muestra una casilla junto a cada archivo para seleccionar varios.",
            [CU(Advanced, "AutoCheckSelect", 0, 1)]),
        new("hide-home", Explorer, "Ocultar \"Inicio\" del panel de navegación",
            "Quita la sección Inicio (archivos recientes y favoritos).",
            [PolicyCU(@"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}", "System.IsPinnedToNameSpaceTree", 0)
                 with { OwnerKey = @"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}" }],
            Refresh: TweakRefresh.Explorer),
        new("hide-gallery", Explorer, "Ocultar \"Galería\" del panel de navegación",
            "Quita la sección Galería de fotos.",
            [PolicyCU(@"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}", "System.IsPinnedToNameSpaceTree", 0)
                 with { OwnerKey = @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}" }],
            Refresh: TweakRefresh.Explorer),
        new("removable-drive-duplicates", Explorer, "Quitar unidades extraíbles duplicadas",
            "Las memorias USB aparecen solo dentro de \"Este equipo\", no repetidas en el panel.",
            // Aquí la optimización *borra* la clave, y "no existe" significa "ya optimizado".
            [new RegSetting(Hive.LocalMachine, RemovableDrivesKey, "", ["Removable Drives", null], null,
                            RegistryValueKind.String, RemovableDrivesKey),
             new RegSetting(Hive.LocalMachine, RemovableDrivesKey32, "", ["Removable Drives", null], null,
                            RegistryValueKind.String, RemovableDrivesKey32)],
            Refresh: TweakRefresh.Explorer),

        // ── Multitarea ─────────────────────────────────────────────────────────
        new("snap", Multitasking, "Desactivar ajuste de ventanas (Snap)",
            "Arrastrar una ventana al borde ya no la acomoda automáticamente.",
            [CU(@"Control Panel\Desktop", "WindowArrangementActive", "1", "0", kind: RegistryValueKind.String)],
            Refresh: TweakRefresh.Snap),
        new("snap-assist", Multitasking, "Desactivar sugerencias al ajustar",
            "Al ajustar una ventana no se ofrecen las demás para llenar el espacio.",
            [CU(Advanced, "SnapAssist", 1, 0)]),
        new("snap-layouts", Multitasking, "Desactivar diseños de ajuste",
            "No aparecen los diseños al pasar el mouse sobre el botón Maximizar.",
            [CU(Advanced, "EnableSnapAssistFlyout", 1, 0)]),
        new("alt-tab", Multitasking, "Pestañas de Edge en Alt+Tab",
            "Qué muestra Alt+Tab además de las ventanas abiertas.",
            [new RegSetting(Hive.CurrentUser, Advanced, "MultiTaskingAltTabFilter", [3, 0, 1, 2], 3)],
            Options: ["Solo ventanas", "20 pestañas", "5 pestañas", "3 pestañas"]),

        // ── Juegos ─────────────────────────────────────────────────────────────
        new("game-dvr", Gaming, "Desactivar grabación de juegos (Game DVR)",
            "Xbox Game Bar deja de grabar en segundo plano; puede mejorar el rendimiento.",
            [CU(@"System\GameConfigStore", "GameDVR_Enabled", 1, 0),
             CU(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1, 0)]),
        new("game-bar-button", Gaming, "No abrir Game Bar con el botón Xbox",
            "El botón Xbox del control ya no abre Xbox Game Bar.",
            [CU(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 1, 0)]),

        // ── Navegadores ────────────────────────────────────────────────────────
        new("brave-bloat", Browsers, "Brave: quitar Rewards, Wallet, VPN y Leo",
            "Desactiva por política las funciones extra de Brave (cripto, VPN e IA).",
            [PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", 1),
             PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveWalletDisabled", 1),
             PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveVPNDisabled", 1),
             PolicyLM(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveAIChatEnabled", 0)]),
    ];
}
