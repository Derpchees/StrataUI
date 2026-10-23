# Strata UI — guía para trabajar en el proyecto

Shell alternativo para Windows 11 (barra de tareas, bandeja, menú Inicio, paneles, Hub de
configuración y optimización). Corre **encima del Explorador** y oculta los componentes nativos;
al salir los restaura. C# + WPF + [ManagedShell](https://github.com/cairoshell/ManagedShell) +
WPF-UI 4.3 + NAudio sobre .NET 10. Ver [README.md](README.md) para la estructura.

## Idioma

- Responder al usuario **en español**.
- El README es para GitHub y va **en inglés**; el CHANGELOG sigue en español.
- Comentarios del código, registros (`Log`), CHANGELOG y mensajes de commit: en español.
- **Textos de la interfaz**: se escriben en español y se marcan para traducir (estilo gettext, el
  texto español es la clave; ver `Services/Localization/Loc.cs`):
  - XAML: `Text="{l:T 'Mostrar fecha'}"` (con `xmlns:l="clr-namespace:Strata.Shell.Services.Localization"`).
  - C#: `Loc.T("Sin conexión")`; con datos, `Loc.F("Volumen: {0}%", percent)` (nada de `$"..."`).
    Siempre con el texto literal dentro de la llamada: la prueba solo ve `Loc.T("...")`, no
    `Loc.T(variable)` (un ayudante que recibe el texto debe recibirlo ya traducido).
  - Agregar la traducción en `src/Strata.Shell/Localization/en.json` (y `pt`, `fr`, `de`). La prueba
    `LocalizationTests` falla si falta alguna o si no coinciden los huecos `{0}`.
  - Hay `en`, `pt`, `fr` y `de`: un texto nuevo lleva su traducción en los cuatro (la prueba
    revisa cada idioma incluido). Un idioma nuevo es otro `xx.json` en esa carpeta (se descubre
    solo); los del usuario van en `%LOCALAPPDATA%\StrataUI\Languages` y se mezclan encima.

## Conceptos de la arquitectura

- **Monitor principal de Strata UI ≠ monitor principal de Windows.** Se elige en el modo edición
  (`Settings.TaskbarMainScreen`, resuelto por `App.MainScreenDevice`). La tecla Windows,
  Win+X, Win+N y el OSD prefieren sus barras (`Taskbar.IsMainScreen`; `App.AppsTaskbar` /
  `App.StatusTaskbar`); las ventanas de monitores sin apps abiertas van a `App.AppsHostDevice`.
  No usar `Screen.Primary` para eso.
- **Diseño por monitor** (`Settings.Layouts`, `ScreenLayout` → `BarConfig`): de cero a cuatro
  barras, una por borde, cada una con su lista ordenada de `BarItem` y la zona de cada elemento
  (`BarZone` inicio/centro/final; `Controls/BarZonesPanel` las acomoda junto a Inicio y las apps).
  `Services/BarLayout` lo lee y lo cambia (barra = `"\\.\DISPLAY1|Bottom"`). Reglas: cada monitor
  lleva cada elemento una sola vez (`BarLayout.Normalize`); las apps abiertas son un elemento más
  (`BarItem.Apps`) con su zona, y se estiran en el espacio libre (`TaskArea`): lo de su misma zona va
  antes o después de ellas (`AppsBeforePanel`/`AppsAfterPanel`) según el orden. Inicio es un elemento
  con zona como los demás; ya no hay alineación (la de antes se convierte una vez con
  `BarLayout.ZoneStartAndApps`); la bandeja puede ir en varios
  monitores (la misma; `Taskbar.HasTray`, y `IsMain` es la primera, la que se informa a las apps);
  arriba y abajo se crean primero y se quedan las esquinas (`BarLayout.InCreationOrder`). Un monitor
  sin entrada recibe `BarLayout.DefaultLayout` al abrir las barras. Los ajustes de antes de la 2.6
  (posición, segunda barra, monitores sin barra, `BarItems`/`BarItemZones`) solo se leen para
  convertirlos (`Services/LegacyLayouts`, en `Settings.Migrate`). Un elemento nuevo: valor en
  `BarItem`, botón en `Taskbar.xaml`, caso en `Taskbar.Element`, nombre/ícono en `BarLayout` (y en
  `BarLayout.All`), su vista en `EditMode/BarPreview.ItemVisual` y zona en `ContrastZones()`.
- **Modo edición** (`EditMode/`): `EditSession` guarda un borrador del diseño, los widgets, los
  `ScreenSettings` de cada monitor (`ScreenOf`/`ChangeScreen`), el monitor principal, el del
  contador de FPS y el de los avisos, y abre una `EditOverlay` por monitor (ventana encima de todo
  con el fondo de pantalla, las barras del borrador dibujadas por `BarPreview` como las reales, los
  widgets en su lugar y un aviso de ejemplo en su esquina). La vista previa lee siempre el borrador,
  nunca `Settings.Current.For(...)`. Nada se guarda hasta `Apply` (un solo `Settings.Update` +
  `WidgetManager.Sync`; los `ScreenSettings` solo de los monitores que cambiaron); Esc o `Cancel` lo
  descarta. Se arrastra con `EditDrag` (captura en el elemento donde empezó y todo se ubica por la
  posición del puntero en la pantalla, también entre monitores y desde la biblioteca del Hub). El
  Hub abierto queda como ventana de la capa de su monitor (siempre encima). Se abre con clic derecho
  en la barra, desde Hub › Barra de tareas o con `StrataUI.exe --edit` (sirve para probarlo).
- **Lecturas avanzadas** (`Services/Sensors`, `SensorsTask`): temperaturas (LibreHardwareMonitor +
  PawnIO), FPS (ETW) y liberar RAM en un proceso elevado `--sensors-task` lanzado por una tarea
  programada (UAC solo al crearla); la tarea ejecuta una copia en `%ProgramFiles%\StrataUI\Lecturas`,
  nunca el Strata UI de la carpeta del usuario. El uso de CPU/GPU/RAM (`SystemMonitor`, PDH) no la necesita.
- **Ajustes por monitor**: la clave es el nombre `\\.\DISPLAYn`, pero Windows lo reparte cada vez
  (la laptop sola y el monitor externo principal son los dos `DISPLAY1`). `Services/MonitorKeys`
  sigue a cada monitor por su identificador de hardware y, al abrir las barras, pasa sus ajustes a
  su nombre actual o los guarda (`ParkedMonitors`) mientras no está. Un ajuste nuevo por monitor
  va también en `MonitorSnapshot` (`Take`/`Clear`/`Put`), con su caso en `MonitorKeysTests`.
- **Las barras se recrean, no se transforman**, cuando cambia qué bordes tiene un monitor, qué
  barras llevan la bandeja o las apps abiertas, o el principal (`App.BarLayoutKey`,
  `BarLayout.StructureKey`). Todo lo demás (elementos, orden, zonas) se aplica en vivo en
  `Taskbar.ApplySettings`, que corre con `Settings.Changed`.
- **Lecturas avanzadas que se iniciaron solas** (por un medidor, `Sensors.StartQuietly`) se
  detienen cuando ninguna barra ni widget muestra CPU, GPU o FPS (`Sensors.StopIfUnused`); las
  pedidas a mano siguen hasta que se detienen en el Hub.
- **Opciones "Igual que Windows"** (apps por monitor): leer siempre el valor efectivo con
  `WindowsTaskbarPrefs.AppsMode(s)`, nunca el ajuste crudo. La alineación solo se lee para convertirla en zonas.
  Se releen cuando Windows avisa (`SystemEvents.UserPreferenceChanged`).
- **Paneles** (sonido, red, Bluetooth, menús Inicio): heredan de `Flyouts/FlyoutWindow`, que los
  coloca junto a la barra en cualquier borde (`BarPosition`), aplica el tema y la región, reserva
  lo que sobresale (`TopOverflow`) y puede seguir abierto sin foco (`StayOpenWhenDeactivated`).
  Se abren con `FlyoutManager.Toggle<T>(botón)`.
- **Opción nueva**: propiedad con valor por defecto en `StrataSettings` (o `ScreenSettings` si es por
  monitor) → control → reacción en `Settings.Changed`. **Dónde va el control**: si se ve en la
  pantalla (una barra, un elemento, un widget, dónde sale algo), en el modo edición: clic derecho de
  la barra (`BarPreview.BarMenu`) o del elemento (`BarPreview.ItemOptions`), sobre el borrador y con
  la vista previa al día; el Hub no la repite. Si es general (tema, menú Inicio, paneles,
  comportamiento, permisos), en su página del Hub con `Settings.Update(...)`. Los ajustes viven en
  `%LOCALAPPDATA%\StrataUI\settings.json`; una propiedad que falta en el archivo toma su valor por defecto.
- **Controlador de NVIDIA** (`Services/Vendors/NvidiaSettings`): NVAPI DRS en un proceso aparte
  (`--nvidia get|set`, escribir pide UAC); IDs de funciones y de ajustes verificados con
  `NvAPI_DRS_GetSettingNameFromId`. Los ajustes de fabricante del catálogo usan `Tweak.Available`.
- **Widgets** (`Services/Widgets/WidgetManager`, `Widgets/`): una `WidgetWindow` por widget, con la
  ventana del escritorio como dueña (así sigue a la vista con Win+D) y siempre al fondo; fondo con
  alfa 1 para que todo el rectángulo reciba el mouse. Posición como fracción del área de trabajo de
  su monitor. El visualizador usa `AudioSpectrum` (captura de bucle de WASAPI + FFT).
- **Menú contextual del Explorador** (`Services/StartMenu/ExplorerMenu`): con el paquete sparse
  (`installer/context-menu/AppxManifest.xml`, armado y firmado por `scripts/build-context-menu.ps1`
  con el certificado autofirmado "CN=Strata UI" de `Cert:\CurrentUser\My`; makeappx/signtool salen
  de NuGet) el comando es `src/Strata.ContextMenu` (servidor COM `IExplorerCommand` fuera de
  proceso, lee título/activado/ruta de `HKCU\Software\StrataUI\ContextMenu`); sin paquete, verbo
  clásico en HKCU. El texto de ese .exe lo escribe Strata UI ya traducido. Publisher, nombre del
  paquete y CLSID deben coincidir entre el manifiesto del paquete, `app.manifest` y `Program.Clsid`.
  Registrar el paquete en esta PC pide confiar en el certificado (UAC).
- **Catálogo del menú Inicio**: `shell:AppsFolder` (lo mismo que "Todas las apps" de Windows) más
  `Services/StartMenu/ExtraApps` (juegos de Steam, Epic y GOG y accesos directos del escritorio a
  programas o juegos), leído en segundo plano; solo se agrega lo que no tiene ya una app con ese
  nombre. Una fuente nueva va en `ExtraApps.Scan` y su carpeta en `WatchedFolders`.
- **Diálogos de abrir archivo o carpeta**: siempre con `Services/FilePicker` (proceso aparte
  `--pick`); el diálogo de Windows dentro del proceso de Strata UI lo tumbó (corrupción del montón).
- **Inicio automático y el Hub**: la tarea y la clave Run abren con `--autostart` (`AutoStart.Arg`);
  así no se abre el Hub salvo con `HubAtStartup`. Una tarea creada elevada no se puede actualizar
  (sigue sin el argumento): por eso, un pedido de abrir el Hub de otra copia en el primer minuto se
  ignora (`App.OnOpenHubRequested`). Strata UI tiene su ícono en la bandeja (`Services/StrataTrayIcon`,
  `NotifyIcon` de WinForms; se ancla la primera vez en `TrayPromotion`).
- **Clic en avisos de sitios** (`Services/Notifications/BrowserActivation`): su app web instalada (por
  nombre) o el `launch` del aviso al navegador con `--notification-launch-id` (lo que hace el
  activador de Chromium); así la página recibe el clic. Abrir el navegador por su AUMID solo abre una ventana.
- **Emojis a color**: WPF los dibuja en blanco y negro; `Services/Emoji/EmojiRenderer` los dibuja con
  DirectWrite (Vortice) y `Controls/RichText.Text` (en vez de `Text`) pone links y emojis en un TextBlock.
- **Vidrio en ventanas pequeñas** (`Services/Themes/GlassSurface`): el desenfoque de DWM cubre toda la
  ventana (una región no lo recorta) y en una ventana en capas (`AllowsTransparency`) además no hay
  región. Por eso los avisos y el OSD son ventanas normales que, con vidrio, miden lo que su tarjeta y
  DWM redondea las esquinas; los menús contextuales y los Popup no llevan vidrio.
- **Cuadros de diálogo**: `DialogPlacement` centra todo `MessageBox` de WPF-UI en el monitor del
  puntero; el diálogo de abrir archivo usa `DialogPlacement.PointerAnchor()` de dueña.
- **Desinstalar desde Inicio** (`AppActions.UninstallAsync`): una app web de Chromium (`_crx_` en el ID o
  `--app-id=` en el acceso directo) usa `--uninstall-app-id`, nunca el desinstalador del navegador.
- **Tareas de administrador**: un argumento `--algo` atendido al principio de `App.OnStartup` y
  lanzado con `Elevation.RunSelfAsync(...)` (UAC). El proceso elevado valida sus argumentos y
  solo hace lo previsto (ver `AudioDeviceOptions.RunElevated`, `TweakEngine.RunElevatedCommand`).
- **Notificaciones**: `NotificationService` lee las de Windows con `UserNotificationListener`
  consultando cada 2 s (sin identidad de paquete no llega el evento de cambios). Los avisos los
  muestra `Notifications/NotificationBannerWindow` (una ventana sin foco, en el monitor y la
  esquina del Hub); el centro es `Flyouts/NotificationFlyout`. Los avisos propios de Strata UI (prueba,
  actualización) llevan `IsLocal` y opcionalmente `OnClick`. En la PC del usuario Windows no
  muestra sus propios avisos emergentes, así que no hay duplicados.
- **Avisos de Windows**: con el centro de Strata UI activo, `WindowsBanners` pone `ShowBanner=0` en
  cada app de `HKCU\...\Notifications\Settings` (respaldo en `Settings.WindowsBannerBackup`) y lo
  devuelve al salir, al desactivar el centro, con `--restore-taskbar` o si Strata UI muere.
- **OSD** (`Notifications/OsdWindow`, clase `Osd`): volumen, brillo y teclas de bloqueo. El OSD
  nativo de volumen lo dibuja `ShellHost.exe` (no se puede ocultar): por eso, con el OSD activo, el
  gancho de teclado se queda con las teclas de volumen y Strata UI cambia el volumen.
- **Temas = estilo + esquema** (`Services/Themes/ThemeCatalog`, `Settings.Theme` + `Settings.ColorScheme`,
  unidos por `ThemeCatalog.Combine`): el estilo pone el material y las formas (Mica = `StrataTheme.System`,
  Translúcido = `Transparent`, Material, Transparente total = `Clear`); el esquema, el claro/oscuro
  (`Light`/`Dark`, con `Mode`: los colores del estilo) o los colores (Nord…). Sin esquema, claro u
  oscuro según Windows. `LiquidGlass`/`LiquidGlassDark` ya no existen (solo se leen para migrarlos en
  `Settings.SplitMode`). Ojo: con "Aplicar el tema a Windows" activado, elegir un esquema claro u
  oscuro cambia el modo de Windows.
- **Animaciones**: `Services/Themes/Motion` (`In`/`Out`) según `PanelAnimation` y `AnimationSpeed`
  (o la importada, `AnimationModule`); usarlo en cualquier ventana emergente nueva.
- **Módulos importables** (`Services/Modules`): temas, animaciones, widgets y aspectos del menú
  Inicio en JSON declarativo (nada de código ni XAML ajeno), en `%LOCALAPPDATA%\StrataUI\Modules`.
  La sección del Hub es `Hub/ModulesSection`; el formato está en `docs/modules/README.md` (en
  inglés) con ejemplos que la prueba `ModuleTests` valida. Un campo nuevo de un módulo va también
  en esa documentación. Los temas se comparan por `ThemeInfo.Key`, no por `Kind`.
- **Barra transparente** (`Taskbar.Transparency.cs`): sin acento de DWM y con el marco extendido
  después de quitar el acento; cambiarla recrea las barras (pasar en vivo de Acrylic a transparente
  la dejaba negra; el "gradiente transparente" pinta el acento de Windows o esconde todo). Cada zona toma `StrataForegroundBrush` claro u oscuro según
  lo que hay detrás, leído con la barra excluida de la captura un momento. Una zona nueva de la
  barra va en `ContrastZones()` y su texto debe usar `StrataForegroundBrush`.
- **Anclados de la barra** (`Services/TaskbarPins`, `Taskbar.Pins.cs`): IDs como los de Inicio en
  `Settings.TaskbarPins`; cada barra con ventanas tiene su lista `PinnedList` de `PinSlot` y el
  `TaskButton` de un anclado usa su primera ventana abierta (`Leader`). Una ventana es del anclado
  por su AppUserModelID o, si no tiene, por el ejecutable; esas ventanas no van en `TaskList`.
- **Ventanas de la barra**: el orden es `TaskOrder` (vista con `CustomSort`); la vista previa es
  `Controls/TaskPreviewWindow` (miniaturas de DWM: ventana sin `AllowsTransparency`). Agrupar por
  app (`Taskbar.Groups.cs`): solo la primera ventana de cada grupo pasa el filtro de la vista;
  `TaskGroups.Key` decide el grupo (sitio web, AppUserModelID o ejecutable).
- **Apps web** (`Services/WebApps/WebSites`): el sitio se reconoce por el título de la pestaña
  (catálogo `Known`, con pruebas en `WebSitesTests`). En notificaciones Windows no deja leer la
  atribución de Chromium (el dominio): solo las apps web instaladas (`Brave._crx_…`) llegan con su
  identidad.
- **Multimedia**: `Services/Media/MediaService` (System Media Transport Controls: lo mismo que
  usa Windows, sirve para cualquier app que se anuncie). Para probar sin molestar al usuario, un
  reproductor de prueba con `MediaPlayer` y volumen 0 (los datos van en `MediaPlaybackItem`, no en
  `DisplayUpdater`).
- **Página nueva del Hub**: `Hub/Pages/XxxPage.xaml` con `<hub:PageHeader Icon=… Title=… Subtitle=…/>`
  al principio, entrada en `HubWindow.xaml` (grupo Personalización o Sistema) y, si conviene, un
  mosaico en `HomePage`. `--hub Xxx` la abre.
- **Módulo de optimización** (`src/Strata.Optimization` → `StrataUI.Optimization.dll`): las páginas
  del grupo Optimización no van en el instalador. `Services/OptimizationModule` lo descarga de la
  publicación `vX.Y.Z` a `%LOCALAPPDATA%\StrataUI\Optimization` y solo lo acepta si su versión
  completa (con el commit) es la de la app; `HubWindow.FillOptimization` agrega sus páginas
  (`IHubModule`) o la entrada "Instalar". El motor (`Tweaks/`, limpieza, NVIDIA) sigue en la app y
  los procesos elevados nunca cargan el módulo. Una página nueva de optimización va en el módulo y
  en `OptimizationPages`; sus textos se traducen en los `.json` de la app. En desarrollo, compilar
  la solución copia el módulo junto a `StrataUI.exe` (así se carga sin descargar; para ver la
  entrada "Instalar", sacar esa copia).
- **Archivos** (`Files/FilesWindow`, `Services/Files/`): explorador propio. Lee con System.IO,
  íconos con `SHGetFileInfo` en un hilo STA aparte (`FileIcons`) y miniaturas decodificadas por WPF;
  nunca extensiones del Shell dentro del proceso: el menú de Windows (`ShellMenuSession`,
  `--shell-menu`: devuelve sus opciones en JSON y ejecuta la elegida) y las carátulas
  (`ThumbnailHost`, `--thumbnails`, UTF-8 por la entrada y la salida estándar) van en otro proceso.
  Cada pestaña tiene uno o dos `FolderPane` (vista doble); la ventana solo tiene la barra lateral,
  las pestañas y los comandos del panel activo. La Papelera se lee de `$Recycle.Bin` (`RecycleBin`,
  fichas `$I`) y los comprimidos se abren con el compresor instalado (`Archivers`). Copiar/mover/Papelera con `SHFileOperation` (`FileOperations`).
  Nube por atributos (`FileEntry.CloudOf`): un archivo "solo en línea" no se lee (ni miniatura ni
  ícono propio) porque leerlo lo descarga. Red: `\\servidor` lista sus recursos (`Places.Shares`).
  Las carpetas que abre Strata UI pasan por `StartMenuService.OpenPath` (Archivos o el Explorador
  según `FilesReplaceExplorer`). Visor de medios: `Files/MediaViewerWindow`.
- **Escritorio** (`Services/Desktop/DesktopManager`, `Desktop/DesktopWindow`): una capa por monitor
  como los widgets (dueña: la ventana del escritorio; siempre al fondo) con los íconos en el monitor
  principal y el menú contextual (el de Windows del escritorio entra con `ShellMenuSession`, modo
  `desktop`). Los widgets usan la capa de su monitor como dueña (quedan encima de los íconos;
  `WidgetManager.Reattach` al cambiar las capas, antes de cerrarlas). Mientras está activo, los
  íconos de Windows se ocultan y vuelven al salir (`DesktopRestoreWindowsIcons`, también en
  `--restore-taskbar`). El área de trabajo se lee con `GetMonitorInfo`: `Screen.WorkingArea` puede
  quedar vieja.
- **Visor**: video y audio con el `MediaPlayer` de WinRT (`FramePlayer`, fotogramas por Direct3D a un
  `WriteableBitmap`): el `MediaElement` de WPF no abre webm, ogg ni opus aunque estén los códecs.
- **Clic en notificaciones**: `BrowserActivation` (sitios), `ToastActivator` (activador COM del
  registro o del AppxManifest de apps empaquetadas, solo `com:ExeServer`) y, si no, la ventana o la
  app. Después de un activador, `AppWakeUp` muestra la app si quedó oculta en la bandeja (Thunderbird
  toma el foco con una ventana invisible).
- **Sonar también en** (`Services/Audio/AudioMirror`): captura de bucle de la salida principal →
  otra salida; `Settings.AudioMirrorDevice`, se reaplica con `AudioService.DevicesChanged`.
- **Consumo**: nada de temporizadores por barra; usar eventos (`SystemStatus.Changed`, el reloj
  compartido de `Taskbar`) y `MemoryTrim.Schedule(...)` después de crear muchos objetos de golpe.

## Compilar, ejecutar y probar

```powershell
.\scripts\run.ps1                          # cierra Strata UI, compila y lo vuelve a abrir
dotnet test tests\Strata.Tests -c Release    # pruebas (xUnit)
.\scripts\build-installer.ps1              # artifacts\StrataUI-Setup-X.Y.Z.exe (Inno Setup 6, ya instalado)
```

- Argumentos útiles: `StrataUI.exe --hub Taskbar` abre el Hub en una página, `--exit` cierra la
  instancia en ejecución, `--restore-taskbar` devuelve la barra de Windows si Strata UI murió mal.

- Compilar mientras Strata UI corre falla porque el .exe está en uso: primero `StrataUI.exe --exit`
  (o `run.ps1`). Para solo comprobar que compila sin cerrarlo:
  `dotnet build src\Strata.Shell\Strata.Shell.csproj -o $env:TEMP\strataui-check`.
- Registro: `%LOCALAPPDATA%\StrataUI\logs\strataui.log` (las excepciones de la UI se registran ahí y no
  cierran la app: revisarlo después de cada prueba).
- Las pruebas que tocan la configuración usan `Settings.SuppressSave = true`.

## Control de versiones (no olvidar)

Cada tanda de cambios visibles para el usuario termina **publicada**, no en "Sin publicar":

1. Subir la versión en [Directory.Build.props](Directory.Build.props) (único lugar). Semver; en
   `0.x`, funciones nuevas suben la menor (0.7.0 → 0.8.0) y solo correcciones, el parche.
2. En [CHANGELOG.md](CHANGELOG.md), renombrar `## [Sin publicar]` a `## [X.Y.Z] - AAAA-MM-DD`
   (secciones Añadido / Cambiado / Corregido) y dejar un `## [Sin publicar]` vacío arriba.
3. Actualizar el README (en inglés) si cambió lo que hace un componente: la cuadrícula "Highlights" y
   su sección en "Features" (cada sección lleva su ícono de `docs/icons`).
4. Commit `feat(área): resumen (vX.Y.Z)` y **etiqueta anotada**:
   `git tag -a vX.Y.Z -m "Strata UI vX.Y.Z: resumen"` (no etiquetas simples).
   Generar también el instalador (`build-installer.ps1`). Las actualizaciones del Hub buscan
   publicaciones de GitHub en `Derpchees/StrataUI` (`UpdateService.Repository`) con la etiqueta
   `vX.Y.Z` y el `.exe` adjunto; publicarlas solo si el usuario lo pide (es hacer push).
   **Notas de la publicación en inglés** (traducir las del CHANGELOG; Added / Changed / Fixed) y
   en una sola lista con todo lo cambiado desde la última versión publicada, aunque haya versiones
   intermedias sin publicación: nada de "también incluye lo de la X.Y.Z".
   Adjuntar también `artifacts\StrataUI.Optimization.dll` (lo deja `build-installer.ps1`, del
   mismo commit): sin él, el botón "Instalar" de Optimización falla en esa versión.
5. Commits intermedios durante el trabajo están bien; la versión se cierra al terminar la tanda,
   antes de pasar a otra cosa.

- Identidad de git: `Derpchees <57698279+Derpchees@users.noreply.github.com>`. Nunca el correo
  del trabajo.
- Mensajes de commit en PowerShell 5.1: escribir el texto en un archivo y usar `git commit -F archivo`
  (`-F -` con here-string no funciona). Revisar que el archivo no tenga un mensaje viejo.
- No hacer push ni reescribir commits ya etiquetados sin que el usuario lo pida.

## Probar en la PC del usuario

El usuario usa la instancia de Strata UI que relanzo y cambia opciones por su cuenta. Quiere que
**yo haga todas las pruebas** antes de pedirle nada (abrir paneles, clics, arrastres, capturas,
instalar y desinstalar en una carpeta temporal); a él solo lo que no se puede automatizar (UAC,
tecla Windows, Win+N).

- Antes de pruebas que cambian opciones, respaldar `%LOCALAPPDATA%\StrataUI\settings.json` y
  restaurarlo después (respetando lo que el usuario haya elegido mientras tanto).
- No probar en el sistema real nada que cambie Windows: tema en Windows, optimizaciones, conectar
  Wi-Fi o Bluetooth, cambiar el dispositivo de audio, quitar apps, escribir propiedades de audio.
- Lo que pide UAC no se puede probar automáticamente (el diálogo está en el escritorio seguro):
  probar la validación sin elevar y pedir al usuario que pruebe el resto.
- Pruebas con mouse simulado: comprobar antes que el panel esté abierto; si no, los clics caen en
  las ventanas del usuario.
- Su PC: Windows en inglés (en-US), así que Strata UI se ve en inglés salvo que elija Español en el
  Hub; los títulos de ventana también están traducidos ("Volume", "Start"). Dos monitores de
  2560×1440: el principal de Windows es `\\.\DISPLAY1` (X = 0, 100 %) y el otro está a su
  izquierda (X negativa; a veces al 125 %). Cambia seguido su diseño de barras (bordes, segunda
  barra, verticales): antes de probar, leer `Layouts` en su `settings.json` en vez de suponerlo.
  Sus monitores aceptan brillo por DDC/CI.
  Es una laptop con batería: a veces está sin los monitores externos (solo la pantalla de
  1920×1080, 100 %, con la barra arriba). El fondo es de Wallpaper Engine (animado; se detiene con
  una ventana maximizada). Puede tener abierto un Strata UI compilado desde otra copia
  (`Downloads\WINX-main`, donde todavía es `WINX.exe`): antes de relanzar, comprobar la ruta con
  `Get-Process StrataUI, WINX` y cerrarlo
  con su propio `--exit`.
- Usa la PC mientras pruebo: antes de cada clic simulado, capturar la pantalla y comprobar qué
  hay en ese punto; preferir pasar el puntero o las teclas a hacer clic.
- En el monitor de 125 % las coordenadas de una captura pueden no coincidir con las de Strata UI: para
  ubicar un control, usar el `BoundingRectangle` de UIA o registrar `PointToScreen` desde Strata UI.
- UIA: buscar por nombre devuelve también la tarjeta y el texto; quedarse con el elemento que
  tenga `InvokePattern`. Los elementos de la navegación del Hub no lo tienen (hacer clic).
- Automatización (UIA): la barra y el Hub se llaman los dos "Strata UI"; distinguirlos por tamaño.
  Invocar el botón de un panel lo abre o lo cierra: comprobar antes si ya está abierto.
- El gancho de teclado ignora las teclas simuladas (`SendInput`): la tecla Windows y Win+X no se
  pueden probar automáticamente.
- Capturas de pantalla desde PowerShell: llamar a `SetProcessDPIAware()` primero, o las
  coordenadas salen escaladas.

## Trampas conocidas

- **DWM**: el Acrylic y el desenfoque por política de acento cubren todo el rectángulo de la
  ventana aunque tenga región (`SetWindowRgn`); para algo que "sobresalga" usar una ventana aparte
  transparente (ver `StartMenu/ProfileAvatarWindow`).
- **WPF-UI**: el estilo de `Button` lo alinea a la izquierda (poner `HorizontalAlignment="Stretch"`
  en mosaicos); `TextBlock` choca con `Wpf.Ui.Controls.TextBlock` (calificar el nombre); cambiar
  `WindowBackdropType` después de crear la ventana hace que WPF-UI deshaga el efecto.
- **Plantillas**: las transformaciones declaradas en un `DataTemplate` quedan congeladas;
  `ProgressBar.Value`/`Slider.Value` enlazan TwoWay por defecto (usar `Mode=OneWay` con
  propiedades de solo lectura); `Click` y `LostMouseCapture` suben por el árbol.
- **Audio**: `IPolicyConfig.SetPropertyValue` devuelve S_OK pero no guarda sin administrador; las
  propiedades de los dispositivos se escriben elevando (`Services/Audio/AudioDeviceOptions`).
- **Arrastrar en paneles**: DragDrop de OLE cancela en silencio ante cualquier excepción en un
  manejador; el menú Inicio arrastra con captura del mouse.
- **Traducciones**: la clave es el texto en español, así que una palabra con dos sentidos comparte
  traducción ("Inicio" del menú y la página "Inicio" del Explorador). Si el sentido cambia, usar
  textos distintos ("Inicio del Explorador").
- **Íconos de WPF-UI** (`SymbolRegular`): no todos los nombres de Fluent existen (`Leaf24` no,
  `LeafOne24` sí) y un nombre inválido rompe el panel al abrirlo (solo se ve en el registro).
  Comprobar con `[Enum]::GetNames` cargando `lib\net472\Wpf.Ui.dll` en PowerShell.
- **Glifos de Segoe Fluent** en C#: escribirlos con la secuencia de escape (barra invertida, u y el código: E701), no el
  carácter; al reescribir un archivo copiando el texto, los caracteres de uso privado se pierden y
  queda `''`. La herramienta Write convierte cualquier `\uXXXX` en el carácter (también `\u200b`,
  invisible): después de crear un archivo, buscar caracteres U+E000–U+F8FF y U+200B–U+200F y
  volver a escribirlos como secuencia.
- **Directivas en HKCU**: `HKCU\Software\Policies` solo la escriben administradores (los ajustes
  ahí piden UAC: `RegSetting.NeedsAdminFor`).
- **DDC/CI**: el identificador de un monitor físico puede valer 0; no usarlo como "no hay".
- **Bandeja**: el clic izquierdo se envía a la app al soltar (para distinguirlo de un arrastre).
  Las apps mandan sus íconos a la primera `Shell_TrayWnd` que encuentra `FindWindow`; en Windows
  26300 la del Explorador puede quedar en una capa del sistema que Strata UI no puede pasar (no sale en
  `EnumWindows` ni se mueve con `SetWindowPos`) y entonces la bandeja de Strata UI queda vacía. La
  barra clásica `ToolbarWindow32` del Explorador ya no existe, así que no hay de dónde importarlos.
  `Services/TrayOwnership` vigila cuál es la primera y, cuando pasa a ser la de Strata UI, difunde
  "TaskbarCreated" para que las apps vuelvan a registrar sus íconos. Muchas apps no lo atienden
  (Spotify, ControlCenter4): `Services/TrayMemory` guarda los íconos en `tray-icons.json` y al
  iniciar se los manda a sí mismo (WM_COPYDATA con las estructuras de ManagedShell) si la ventana de
  la app sigue viva. Lo que una app así registró en el Explorador no se puede traer (ni la barra
  clásica ni `ITrayNotify` responden en 26300): solo vuelve si la app se reinicia.
  Diagnóstico: con la variable `STRATAUI_SHELL_LOG=ruta`, ManagedShell escribe su registro detallado.
- **Inicio automático** (`Services/AutoStart`): tarea programada "StrataUI (usuario)" al iniciar sesión,
  sin elevar (elevado, UIPI bloquea los íconos de bandeja de las apps normales). Si no se puede
  crear, clave Run. El instalador escribe la clave Run y Strata UI la migra al abrirse.
- **Claves de íconos de la bandeja**: `NotifyIcon.Identifier` lleva el texto emergente si el
  ícono no tiene GUID (cambia con el tiempo); para guardar usar `TrayPromotion.Key`/`Matches`.
- **Barras verticales**: cualquier cosa nueva en la barra debe funcionar en los cuatro bordes
  (orientación en `Taskbar.ApplyOrientation`, indicador en `TaskButton.FitTo`, posición de
  ventanas emergentes según `Position`).
