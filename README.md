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
| Barra de tareas         | ✅ por monitor, alineación y tamaño configurables              |
| Bandeja del sistema     | ✅ íconos visibles u ocultos, configurable desde el Hub        |
| Hub de configuración    | ✅ barra, bandeja, apariencia, optimización, general           |
| Optimización (debloat)  | ✅ apps, privacidad, IA, sistema, Update, Explorador… con respaldo |
| Panel de volumen        | ✅ dispositivo de salida y volumen por aplicación              |
| Panel de red            | ✅ conexión actual, Wi-Fi: buscar, conectar, desconectar       |
| Panel de Bluetooth      | ✅ radio, dispositivos, conectar audífonos y bocinas           |
| Menú Inicio             | ⏳ abre el de Windows                                          |
| Notificaciones          | ⏳ abre el centro de Windows                                   |

## Requisitos

- Windows 11
- [.NET SDK 10](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)

## Compilar y ejecutar

```powershell
.\scripts\run.ps1          # cierra WINX si está abierto, compila y lo vuelve a abrir
dotnet test tests\Winx.Tests -c Release
```

Clic derecho en un espacio vacío de la barra → **Configuración de WINX** abre el Hub;
**Salir de WINX** vuelve a la barra de Windows.

### Línea de comandos

| Argumento            | Efecto                                                                 |
|----------------------|------------------------------------------------------------------------|
| *(sin argumentos)*   | Inicia WINX; si ya está abierto, muestra el Hub.                       |
| `--hub <página>`     | Abre el Hub en una página: `Taskbar`, `Tray`, `Appearance`, `Apps`, `Privacy`, `AI`, `Backup`… |
| `--exit`             | Cierra limpiamente la instancia en ejecución.                          |
| `--restore-taskbar`  | Emergencia: vuelve a mostrar la barra de Windows si WINX se cerró mal. |

## Datos

Todo vive en `%LOCALAPPDATA%\WINX\`:

| Archivo               | Contenido                                                     |
|-----------------------|---------------------------------------------------------------|
| `settings.json`       | Preferencias de WINX                                          |
| `tweaks-backup.json`  | Valores originales del registro antes de cada optimización    |
| `logs\winx.log`       | Registro de eventos y errores                                 |

## Versionado

- La versión vive en un solo lugar: [Directory.Build.props](Directory.Build.props).
- Cada versión se documenta en [CHANGELOG.md](CHANGELOG.md) y se marca con un tag `vX.Y.Z`.
- Se sigue [versionado semántico](https://semver.org/lang/es/); mientras sea `0.x`, cada
  componente nuevo sube la versión menor.

## Estructura

```
src/Winx.Shell/
  App.xaml(.cs)        Arranque, instancia única, ciclo de vida del shell
  Taskbar.xaml(.cs)    Barra de tareas (una por monitor)
  Controls/            Botón de tarea e ícono de bandeja
  Flyouts/             Paneles flotantes: volumen, red, Bluetooth
  Hub/                 Ventana de configuración y sus páginas
  Tweaks/              Motor de optimizaciones, catálogo, respaldo, apps Appx, elevación
  Services/            Tema, configuración, audio, estado del sistema, registro
  Themes/              Paletas claro/oscuro y estilos de la barra
tests/Winx.Tests/      Pruebas del motor de optimizaciones y del catálogo
```

## Créditos

- [ManagedShell](https://github.com/cairoshell/ManagedShell) — barra, bandeja y AppBar.
- [WPF-UI](https://github.com/lepoco/wpfui) — controles Fluent del Hub y los paneles.
- [NAudio](https://github.com/naudio/NAudio) — acceso a Core Audio.
- Las opciones de optimización se inspiran en [Win11Debloat](https://github.com/Raphire/Win11Debloat) (MIT).
