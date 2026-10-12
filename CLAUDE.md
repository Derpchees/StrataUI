# WINX — guía para trabajar en el proyecto

Shell alternativo para Windows 11 (barra de tareas, bandeja, menú Inicio, paneles, Hub de
configuración y optimización). Corre **encima del Explorador** y oculta los componentes nativos;
al salir los restaura. C# + WPF + [ManagedShell](https://github.com/cairoshell/ManagedShell) +
WPF-UI 4.3 + NAudio sobre .NET 10. Ver [README.md](README.md) para la estructura.

## Idioma

- Responder al usuario **en español**.
- Comentarios del código, registros (`Log`), CHANGELOG y mensajes de commit: en español.
- **Textos de la interfaz**: se escriben en español y se marcan para traducir (estilo gettext, el
  texto español es la clave; ver `Services/Localization/Loc.cs`):
  - XAML: `Text="{l:T 'Mostrar fecha'}"` (con `xmlns:l="clr-namespace:Winx.Shell.Services.Localization"`).
  - C#: `Loc.T("Sin conexión")`; con datos, `Loc.F("Volumen: {0}%", percent)` (nada de `$"..."`).
    Siempre con el texto literal dentro de la llamada: la prueba solo ve `Loc.T("...")`, no
    `Loc.T(variable)` (un ayudante que recibe el texto debe recibirlo ya traducido).
  - Agregar la traducción en `src/Winx.Shell/Localization/en.json`. La prueba
    `LocalizationTests` falla si falta alguna o si no coinciden los huecos `{0}`.
  - Un idioma nuevo es otro `xx.json` más su código en `Loc.Translated`.

## Conceptos de la arquitectura

- **Barra principal de WINX ≠ monitor principal de Windows.** Se elige en el Hub
  (`Settings.TaskbarMainScreen`, resuelto por `App.MainScreenDevice`). La bandeja, la tecla
  Windows, Win+X y las ventanas de monitores sin barra van a la barra con `Taskbar.IsMain`;
  no usar `Screen.Primary` para eso.
- **Las barras se recrean, no se transforman**, cuando cambia la posición por monitor, los
  monitores sin barra o el principal (`App.BarLayoutKey`). Todo lo demás se aplica en vivo en
  `Taskbar.ApplySettings`, que corre con `Settings.Changed`.
- **Opciones "Igual que Windows"** (alineación, apps por monitor): leer siempre el valor efectivo
  con `WindowsTaskbarPrefs.Alignment(s)` / `WindowsTaskbarPrefs.AppsMode(s)`, nunca el ajuste crudo.
  Se releen cuando Windows avisa (`SystemEvents.UserPreferenceChanged`).
- **Paneles** (sonido, red, Bluetooth, menús Inicio): heredan de `Flyouts/FlyoutWindow`, que los
  coloca junto a la barra en cualquier borde (`BarPosition`), aplica el tema y la región, reserva
  lo que sobresale (`TopOverflow`) y puede seguir abierto sin foco (`StayOpenWhenDeactivated`).
  Se abren con `FlyoutManager.Toggle<T>(botón)`.
- **Opción nueva**: propiedad con valor por defecto en `WinxSettings` → control en la página del
  Hub que guarda con `Settings.Update(...)` → reacción en `Settings.Changed`. Los ajustes viven en
  `%LOCALAPPDATA%\WINX\settings.json`; una propiedad que falta en el archivo toma su valor por defecto.
- **Tareas de administrador**: un argumento `--algo` atendido al principio de `App.OnStartup` y
  lanzado con `Elevation.RunSelfAsync(...)` (UAC). El proceso elevado valida sus argumentos y
  solo hace lo previsto (ver `AudioDeviceOptions.RunElevated`, `TweakEngine.RunElevatedCommand`).

## Compilar, ejecutar y probar

```powershell
.\scripts\run.ps1                          # cierra WINX, compila y lo vuelve a abrir
dotnet test tests\Winx.Tests -c Release    # pruebas (xUnit)
```

- Argumentos útiles: `WINX.exe --hub Taskbar` abre el Hub en una página, `--exit` cierra la
  instancia en ejecución, `--restore-taskbar` devuelve la barra de Windows si WINX murió mal.

- Compilar mientras WINX corre falla porque el .exe está en uso: primero `WINX.exe --exit`
  (o `run.ps1`). Para solo comprobar que compila sin cerrarlo:
  `dotnet build src\Winx.Shell\Winx.Shell.csproj -o $env:TEMP\winx-check`.
- Registro: `%LOCALAPPDATA%\WINX\logs\winx.log` (las excepciones de la UI se registran ahí y no
  cierran la app: revisarlo después de cada prueba).
- Las pruebas que tocan la configuración usan `Settings.SuppressSave = true`.

## Control de versiones (no olvidar)

Cada tanda de cambios visibles para el usuario termina **publicada**, no en "Sin publicar":

1. Subir la versión en [Directory.Build.props](Directory.Build.props) (único lugar). Semver; en
   `0.x`, funciones nuevas suben la menor (0.7.0 → 0.8.0) y solo correcciones, el parche.
2. En [CHANGELOG.md](CHANGELOG.md), renombrar `## [Sin publicar]` a `## [X.Y.Z] - AAAA-MM-DD`
   (secciones Añadido / Cambiado / Corregido) y dejar un `## [Sin publicar]` vacío arriba.
3. Actualizar la tabla "Estado" del README si cambió lo que hace un componente.
4. Commit `feat(área): resumen (vX.Y.Z)` y **etiqueta anotada**:
   `git tag -a vX.Y.Z -m "WINX vX.Y.Z: resumen"` (no etiquetas simples).
5. Commits intermedios durante el trabajo están bien; la versión se cierra al terminar la tanda,
   antes de pasar a otra cosa.

- Identidad de git: `Derpchees <57698279+Derpchees@users.noreply.github.com>`. Nunca el correo
  del trabajo.
- Mensajes de commit en PowerShell 5.1: escribir el texto en un archivo y usar `git commit -F archivo`
  (`-F -` con here-string no funciona). Revisar que el archivo no tenga un mensaje viejo.
- No hacer push ni reescribir commits ya etiquetados sin que el usuario lo pida.

## Probar en la PC del usuario

El usuario usa la instancia de WINX que relanzo y cambia opciones por su cuenta.

- Antes de pruebas que cambian opciones, respaldar `%LOCALAPPDATA%\WINX\settings.json` y
  restaurarlo después (respetando lo que el usuario haya elegido mientras tanto).
- No probar en el sistema real nada que cambie Windows: tema en Windows, optimizaciones, conectar
  Wi-Fi o Bluetooth, cambiar el dispositivo de audio, quitar apps, escribir propiedades de audio.
- Lo que pide UAC no se puede probar automáticamente (el diálogo está en el escritorio seguro):
  probar la validación sin elevar y pedir al usuario que pruebe el resto.
- Pruebas con mouse simulado: comprobar antes que el panel esté abierto; si no, los clics caen en
  las ventanas del usuario.
- Su PC: Windows en inglés (en-US), así que WINX se ve en inglés salvo que elija Español en el
  Hub; los títulos de ventana también están traducidos ("Volume", "Start"). Dos monitores de
  2560×1440: el principal de Windows es `\\.\DISPLAY1` (X = 0, 100 %) y el otro está a su
  izquierda (X negativa, 125 %).
- Automatización (UIA): la barra y el Hub se llaman los dos "WINX"; distinguirlos por tamaño.
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
- **Barras verticales**: cualquier cosa nueva en la barra debe funcionar en los cuatro bordes
  (orientación en `Taskbar.ApplyOrientation`, indicador en `TaskButton.FitTo`, posición de
  ventanas emergentes según `Position`).
