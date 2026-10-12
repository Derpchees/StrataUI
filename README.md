# WINX

Shell alternativo para Windows 11: reemplaza la barra de tareas, la bandeja del sistema,
el menú Inicio, los paneles de estado y las notificaciones con componentes propios, e incluye
un Hub para personalizar y optimizar Windows.

WINX corre **encima del Explorador de Windows** (los archivos y el escritorio siguen funcionando)
y oculta por completo los componentes nativos mientras está abierto. Al cerrarlo, todo vuelve
a la normalidad.

## Estado

| Componente              | Estado                                                        |
|-------------------------|---------------------------------------------------------------|
| Barra de tareas         | ✅ una o dos por monitor (bordes opuestos, repartiendo apps y área de estado), en cualquier borde, alineación y tamaño configurables |
| Bandeja del sistema     | ✅ íconos visibles u ocultos; se ordenan arrastrando, también entre la barra y los ocultos |
| Hub de configuración    | ✅ portada, barra, menú Inicio, bandeja y área de estado, notificaciones, apariencia, optimización, actualizaciones, general |
| Optimización (debloat)  | ✅ rendimiento, apps, privacidad, IA, sistema, Update, Explorador… con respaldo |
| Temas                   | ✅ Sistema, Claro, Oscuro, Transparencia, Liquid Glass (claro/oscuro), Material |
| Panel de sonido         | ✅ salida y entrada con mosaicos de dispositivos, opciones (balance, mejoras de audio, modo exclusivo, llamadas) y volumen por aplicación |
| Panel de red            | ✅ conexión actual, Wi-Fi: buscar, conectar, desconectar       |
| Panel de Bluetooth      | ✅ radio, dispositivos, conectar audífonos y bocinas           |
| Multimedia              | ✅ carátula, canción, artista y controles en la barra para cualquier app (Spotify, Reproductor multimedia, navegador…); panel con avance |
| Brillo                  | ✅ cada monitor por DDC/CI (y pantalla de portátil por WMI), todos a la vez, rueda del mouse |
| Menú Inicio             | ✅ 7 estilos (Windows 11, Perfil, Lista, Pantalla completa, Compacto, Solo búsqueda, Íconos), redimensionable, anclados con carpetas, secciones y huecos, tecla Windows; clic derecho: ejecutar como administrador, abrir ubicación, desinstalar |
| Notificaciones          | ✅ centro propio junto al reloj (Win+N), avisos en el monitor y la esquina elegidos, No molestar, calendario |
| Actualizaciones         | ✅ búsqueda automática en GitHub Releases e instalación con un clic |
| Instalador              | ✅ Inno Setup, por usuario y sin administrador (`scripts\build-installer.ps1`) |
| Idiomas                 | ✅ español e inglés, según el idioma de Windows o elegido en el Hub |

## Requisitos

- Windows 11
- [.NET SDK 10](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)

## Compilar y ejecutar

```powershell
.\scripts\run.ps1              # cierra WINX si está abierto, compila y lo vuelve a abrir
dotnet test tests\Winx.Tests -c Release
.\scripts\build-installer.ps1  # artifacts\WINX-Setup-X.Y.Z.exe (requiere Inno Setup 6)
```

### Publicar una versión

1. Generar el instalador con `scripts\build-installer.ps1`.
2. Crear en GitHub (`Derpchees/WINX`) una publicación con la etiqueta `vX.Y.Z`, las notas del
   CHANGELOG y el `.exe` adjunto. El Hub (Actualizaciones) la encuentra por esa etiqueta y descarga
   el `.exe`.

Clic derecho en un espacio vacío de la barra → **Configuración de WINX** abre el Hub;
**Salir de WINX** vuelve a la barra de Windows.

### Línea de comandos

| Argumento            | Efecto                                                                 |
|----------------------|------------------------------------------------------------------------|
| *(sin argumentos)*   | Inicia WINX; si ya está abierto, muestra el Hub.                       |
| `--hub <página>`     | Abre el Hub en una página: `Home`, `Taskbar`, `Start`, `Tray`, `Notifications`, `Appearance`, `Updates`, `Privacy`, `Backup`… |
| `--exit`             | Cierra limpiamente la instancia en ejecución (y espera a que termine). |
| `--restore-taskbar`  | Emergencia: vuelve a mostrar la barra de Windows si WINX se cerró mal. |

(`--apply-tweak`, `--restore-tweaks`, `--restore-point` y `--audio-property` son de uso interno:
los lanza WINX elevado con UAC para lo que necesita permisos de administrador.)

## Datos

Todo vive en `%LOCALAPPDATA%\WINX\`:

| Archivo               | Contenido                                                     |
|-----------------------|---------------------------------------------------------------|
| `settings.json`       | Preferencias de WINX                                          |
| `tweaks-backup.json`  | Valores originales del registro antes de cada optimización    |
| `logs\winx.log`       | Registro de eventos y errores                                 |

## Versionado

- La versión vive en un solo lugar: [Directory.Build.props](Directory.Build.props).
- Cada versión se documenta en [CHANGELOG.md](CHANGELOG.md) y se marca con un tag anotado `vX.Y.Z`
  (el procedimiento completo está en [CLAUDE.md](CLAUDE.md)).
- Se sigue [versionado semántico](https://semver.org/lang/es/); mientras sea `0.x`, cada
  componente nuevo sube la versión menor.

## Estructura

```
src/Winx.Shell/
  App.xaml(.cs)        Arranque, instancia única, ciclo de vida del shell
  Taskbar.xaml(.cs)    Barra de tareas (una por monitor)
  Controls/            Botón de tarea e ícono de bandeja
  Flyouts/             Paneles flotantes: sonido, red, Bluetooth, brillo, centro de notificaciones
  Notifications/       Avisos emergentes de notificaciones
  StartMenu/           Menú Inicio: modelo común, estilos, anclados con carpetas y secciones
  Hub/                 Ventana de configuración y sus páginas
  Tweaks/              Motor de optimizaciones, catálogo, respaldo, apps Appx, elevación
  Services/            Configuración, audio, red, Bluetooth, brillo, notificaciones, actualizaciones,
                       estado del sistema, memoria, registro
  Services/Themes/     Catálogo de temas, paleta generada, efectos de DWM, tema en Windows
  Themes/              Estilos compartidos de la barra
  Localization/        Traducciones de la interfaz (en.json; el español está en el código)
tests/Winx.Tests/      Pruebas: optimizaciones, catálogo, anclados, audio, barra, traducciones,
                       desinstalador, actualizaciones
installer/             Script de Inno Setup
scripts/               run.ps1 (compilar y relanzar), build-installer.ps1
```

## Créditos

- [ManagedShell](https://github.com/cairoshell/ManagedShell) — barra, bandeja y AppBar.
- [WPF-UI](https://github.com/lepoco/wpfui) — controles Fluent del Hub y los paneles.
- [NAudio](https://github.com/naudio/NAudio) — acceso a Core Audio.
- Las opciones de optimización se inspiran en [Win11Debloat](https://github.com/Raphire/Win11Debloat) (MIT).
