# WINX

Shell alternativo para Windows 11: reemplaza la barra de tareas, la bandeja del sistema,
el menú Inicio, el centro de control y las notificaciones con componentes propios.

WINX corre **encima del Explorador de Windows** (los archivos y el escritorio siguen funcionando)
y oculta por completo los componentes nativos mientras está abierto. Al cerrarlo, todo vuelve
a la normalidad.

## Estado

| Componente           | Estado                                   |
|----------------------|------------------------------------------|
| Barra de tareas      | ✅ v0.1                                   |
| Bandeja del sistema  | ✅ v0.1                                   |
| Menú Inicio          | ⏳ abre el de Windows                     |
| Centro de control    | ⏳ abre el de Windows                     |
| Notificaciones       | ⏳ abre el centro de Windows              |
| Ajustes (tweaks)     | ⏳ registro, privacidad y debloat         |

## Requisitos

- Windows 11
- [.NET SDK 10](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)

## Compilar y ejecutar

```powershell
dotnet build Winx.sln
.\src\Winx.Shell\bin\Debug\net10.0-windows\WINX.exe
```

Clic derecho en un espacio vacío de la barra → **Salir de WINX** para volver a la barra de Windows.

### Línea de comandos

| Argumento            | Efecto                                                                 |
|----------------------|------------------------------------------------------------------------|
| `--exit`             | Cierra limpiamente la instancia en ejecución.                          |
| `--restore-taskbar`  | Emergencia: vuelve a mostrar la barra de Windows si WINX se cerró mal. |

El registro está en `%LOCALAPPDATA%\WINX\logs\winx.log`.

## Versionado

- La versión vive en un solo lugar: [Directory.Build.props](Directory.Build.props).
- Cada versión se documenta en [CHANGELOG.md](CHANGELOG.md) y se marca con un tag `vX.Y.Z`.
- Se sigue [versionado semántico](https://semver.org/lang/es/); mientras sea `0.x`, cada
  componente nuevo sube la versión menor.

## Estructura

```
src/Winx.Shell/
  App.xaml(.cs)        Arranque, ciclo de vida del shell y restauración de Explorer
  Taskbar.xaml(.cs)    Barra de tareas (una por monitor)
  Controls/            Botón de tarea e ícono de bandeja
  Services/            Tema, estado del sistema, íconos promovidos, registro
  Themes/              Paletas claro/oscuro y estilos compartidos
```
