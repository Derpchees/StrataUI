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
  - Agregar la traducción en `src/Strata.Shell/Localization/en.json`. La prueba
    `LocalizationTests` falla si falta alguna o si no coinciden los huecos `{0}`.
  - Un idioma nuevo es otro `xx.json` más su código en `Loc.Translated`.

## Conceptos de la arquitectura

- **Barra principal de Strata UI ≠ monitor principal de Windows.** Se elige en el Hub
  (`Settings.TaskbarMainScreen`, resuelto por `App.MainScreenDevice`). La bandeja, la tecla
  Windows, Win+X y las ventanas de monitores sin barra van a la barra con `Taskbar.IsMain`;
  no usar `Screen.Primary` para eso.
- **Dos barras por monitor**: la segunda va en el borde opuesto (`Settings.SecondTaskbars`) y
  `App.BarsFor` reparte qué lleva cada una (`BarRole`: apps, área de estado, bandeja). La bandeja,
  la tecla Windows y Win+N usan la barra del monitor principal que tenga eso (`IsMain` = la que
  lleva la bandeja; `App.AppsTaskbar` / `App.StatusTaskbar`).
- **Elementos de cada barra** (`Services/BarLayout`, `Settings.BarItems`, clave `"\\.\DISPLAY1|1"`):
  lista ordenada de `BarItem` por barra; sin entrada, `BarLayout.Defaults` (lo de antes). La bandeja
  va en la primera barra que la lleve (`IsMain`; moverla recrea las barras). Se editan en el Hub
  (`Hub/BarItemsEditor`) o arrastrando en la barra (`Taskbar.ItemDrag.cs`); al guardar se escriben
  las listas de todas las barras. Un elemento nuevo: valor en `BarItem`, botón en `Taskbar.xaml`,
  caso en `Taskbar.Element`, nombre/ícono en `BarLayout` y zona en `ContrastZones()`.
  Cada elemento va en una zona de la barra (`Settings.BarItemZones`, `BarZone` inicio/centro/final;
  `Controls/BarZonesPanel` las acomoda a lo largo de la barra junto con Inicio y las ventanas).
- **Lecturas avanzadas** (`Services/Sensors`, `SensorsTask`): temperaturas (LibreHardwareMonitor +
  PawnIO), FPS (ETW) y liberar RAM en un proceso elevado `--sensors-task` lanzado por una tarea
  programada (UAC solo al crearla); la tarea ejecuta una copia en `%ProgramFiles%\StrataUI\Lecturas`,
  nunca el Strata UI de la carpeta del usuario. El uso de CPU/GPU/RAM (`SystemMonitor`, PDH) no la necesita.
- **Las barras se recrean, no se transforman**, cuando cambia la posición o la segunda barra de un
  monitor, los monitores sin barra o el principal (`App.BarLayoutKey`). Todo lo demás se aplica en vivo en
  `Taskbar.ApplySettings`, que corre con `Settings.Changed`.
- **Opciones "Igual que Windows"** (alineación, apps por monitor): leer siempre el valor efectivo
  con `WindowsTaskbarPrefs.Alignment(s)` / `WindowsTaskbarPrefs.AppsMode(s)`, nunca el ajuste crudo.
  Se releen cuando Windows avisa (`SystemEvents.UserPreferenceChanged`).
- **Paneles** (sonido, red, Bluetooth, menús Inicio): heredan de `Flyouts/FlyoutWindow`, que los
  coloca junto a la barra en cualquier borde (`BarPosition`), aplica el tema y la región, reserva
  lo que sobresale (`TopOverflow`) y puede seguir abierto sin foco (`StayOpenWhenDeactivated`).
  Se abren con `FlyoutManager.Toggle<T>(botón)`.
- **Opción nueva**: propiedad con valor por defecto en `StrataSettings` → control en la página del
  Hub que guarda con `Settings.Update(...)` → reacción en `Settings.Changed`. Los ajustes viven en
  `%LOCALAPPDATA%\StrataUI\settings.json`; una propiedad que falta en el archivo toma su valor por defecto.
- **Controlador de NVIDIA** (`Services/Vendors/NvidiaSettings`): NVAPI DRS en un proceso aparte
  (`--nvidia get|set`, escribir pide UAC); IDs de funciones y de ajustes verificados con
  `NvAPI_DRS_GetSettingNameFromId`. Los ajustes de fabricante del catálogo usan `Tweak.Available`.
- **Widgets** (`Services/Widgets/WidgetManager`, `Widgets/`): una `WidgetWindow` por widget, con la
  ventana del escritorio como dueña (así sigue a la vista con Win+D) y siempre al fondo; fondo con
  alfa 1 para que todo el rectángulo reciba el mouse. Posición como fracción del área de trabajo de
  su monitor. El visualizador usa `AudioSpectrum` (captura de bucle de WASAPI + FFT).
- **Diálogos de abrir archivo o carpeta**: siempre con `Services/FilePicker` (proceso aparte
  `--pick`); el diálogo de Windows dentro del proceso de Strata UI lo tumbó (corrupción del montón).
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
- **Animaciones**: `Services/Themes/Motion` (`In`/`Out`) según `PanelAnimation` y `AnimationSpeed`;
  usarlo en cualquier ventana emergente nueva.
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
3. Actualizar la tabla "Features" del README (en inglés) si cambió lo que hace un componente.
4. Commit `feat(área): resumen (vX.Y.Z)` y **etiqueta anotada**:
   `git tag -a vX.Y.Z -m "Strata UI vX.Y.Z: resumen"` (no etiquetas simples).
   Generar también el instalador (`build-installer.ps1`). Las actualizaciones del Hub buscan
   publicaciones de GitHub en `Derpchees/StrataUI` (`UpdateService.Repository`) con la etiqueta
   `vX.Y.Z` y el `.exe` adjunto; publicarlas solo si el usuario lo pide (es hacer push).
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
  izquierda (X negativa, 125 %). Usa las barras en vertical: la principal de Strata UI (con la bandeja)
  está en el borde derecho del monitor izquierdo. Sus monitores aceptan brillo por DDC/CI.
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
