# Changelog

Todos los cambios relevantes de WINX se documentan aquí.
El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [versionado semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido
- Ícono de WINX en el ejecutable y en la ventana del Hub.
- Opción para mostrar u ocultar los nombres de las apps ancladas en el menú Inicio (sin ellos, el
  nombre aparece al pasar el mouse).
- Panel de sonido rediseñado:
  - Salida y entrada, cada una con su volumen y sus dispositivos como mosaicos cuadrados con
    ícono según el tipo (bocinas, audífonos, diadema, monitor, digital, Bluetooth, micrófono).
  - Opciones de cada dispositivo: nivel en vivo (en micrófonos, para probarlo), balance
    izquierda/derecha y usarlo como predeterminado para llamadas.

### Corregido
- El dispositivo de entrada predeterminado y su volumen no se mostraban en ninguna parte.

## [0.7.0] - 2026-10-11

### Añadido
- Estilo de menú Inicio **Perfil**: como el de Windows 11, con tu foto grande arriba sobresaliendo
  del borde del menú, tu nombre y un saludo según la hora.
- Todos los menús Inicio (salvo pantalla completa) cambian de tamaño arrastrando sus bordes; el
  tamaño se recuerda por estilo.
- Tamaño de los íconos del menú Inicio: pequeños, medianos o grandes.
- Anclados organizados a mano (estilos Windows 11, Perfil y Pantalla completa):
  - Arrastrar para reordenar, también entre secciones.
  - Soltar una app sobre otra crea una carpeta; soltarla sobre una carpeta la agrega. Las carpetas
    se renombran, se desagrupan y sus apps se sacan arrastrándolas.
  - Secciones con nombre: crear, renombrar (doble clic en el título) y eliminar.
  - Los anclados anteriores pasan solos a una sección "Anclado".

## [0.6.0] - 2026-10-11

### Añadido
- Configuración del menú Inicio en el Hub:
  - Tres estilos minimalistas nuevos: **Compacto**, **Solo búsqueda** (barra centrada tipo
    Spotlight) e **Íconos** (cuadrícula sin texto).
  - Ícono del botón Inicio a elegir: WINX, Windows, Monocromo, Círculo, Punto, Cuadrícula, Lupa.
  - Mostrar u ocultar apps ancladas, todas las apps, archivos recientes y el usuario, con o sin
    la foto de la cuenta de Windows.
  - Botones del pie a elegir: energía (menú), bloquear, suspender, reiniciar, apagar, cerrar
    sesión, Explorador, carpetas, Configuración de Windows, Panel de control, Administrador de
    tareas, Terminal y Configuración de WINX.

### Corregido
- El color de acento de WINX se veía azul en vez del elegido: una clave fija en `App.xaml` tapaba
  la paleta del tema.
- Un panel que fallaba al abrirse quedaba "abierto" para siempre y no se podía volver a abrir.

## [0.5.0] - 2026-10-11

### Añadido
- **Liquid Glass con refracción real**: la barra, los paneles y el menú Inicio capturan lo que
  hay detrás y lo muestran a través de un shader propio (refracción de lente en los bordes,
  aberración cromática, saturación, tinte y brillo especular). Opción "Refracción en vivo" en
  Apariencia; mientras está activa, esas ventanas no aparecen en capturas ni grabaciones.

### Cambiado
- Los temas Material y Liquid Glass ahora cubren toda la app: tarjetas, botones, cajas de texto,
  menús contextuales, tooltips, desplegables y la selección del menú lateral del Hub.

### Corregido
- Aplicar un tema congelaba WINX: el aviso WM_SETTINGCHANGE al sistema se enviaba de forma
  síncrona desde el hilo de la interfaz. Ahora va en segundo plano (aplicar tarda ~10 ms).
- Cambiar entre barra fija y flotante recreaba la barra; ahora solo cambia de alto.
- El tema de controles se recargaba ante cualquier aviso del sistema aunque no cambiara nada.

## [0.4.0] - 2026-10-11

### Añadido
- Menú Inicio propio con tres estilos, elegibles en Hub › Menú Inicio:
  **Windows 11** (buscador, anclados, recientes, "Todas las apps" por letra),
  **Lista** (estilo Windows 10/7, con accesos a carpetas) y **Pantalla completa** (tipo Launchpad).
- Catálogo de apps desde `shell:AppsFolder` (Win32 y Store), búsqueda con prioridad por inicio de
  palabra, Enter abre el primer resultado o ejecuta el texto como comando, anclar/desanclar con
  clic derecho, perfil de usuario y menú de energía (bloquear, cerrar sesión, suspender,
  reiniciar, apagar).
- La tecla Windows abre el menú de WINX; los atajos (Win+E, Win+R…) siguen funcionando.

### Corregido
- Fuga de objetos COM en el panel de volumen (sesiones de audio creadas cada 1,5 s sin liberar)
  y en la detección de audio Bluetooth.
- El aviso "Reiniciar el Explorador" dejaba de aparecer al volver a una página de optimización.
- Cualquier cambio de configuración reaplicaba el tema completo (parpadeo del Hub).
- Reiniciar el Explorador congelaba la interfaz unos segundos.
- El hook de teclado corre en su propio hilo: un bloqueo de la interfaz ya no retrasa el teclado.

## [0.3.0] - 2026-10-11

### Añadido
- Sistema de temas para toda la app (barra, paneles y Hub), con galería y vista previa en
  Apariencia: **Sistema**, **Claro**, **Oscuro**, **Transparencia** (Acrylic),
  **Liquid Glass** y **Liquid Glass oscuro** (estilo iOS/HyperOS: desenfoque limpio, tinte
  mínimo, bordes de luz, barra flotante en forma de cápsula y paneles con esquinas grandes) y
  **Material** (Material Design: superficies tonales derivadas del acento).
- "Aplicar el tema a Windows": modo claro/oscuro y transparencia del sistema, y material de
  fondo y barra de título oscura en las ventanas del Explorador y del Panel de control,
  sin inyectar código. Los valores originales quedan en el respaldo.

### Corregido
- La cancelación de una búsqueda Wi-Fi al cerrar el panel ya no se registra como error.

## [0.2.1] - 2026-10-11

### Cambiado
- Panel de volumen compacto: por defecto solo muestra el volumen general; "Dispositivo de salida"
  y "Aplicaciones" son secciones plegables (contraídas) con un resumen. En cada app, el ícono
  funciona como botón de silencio.

### Corregido
- Las páginas del Hub no se podían desplazar ni con la barra ni con la rueda del mouse:
  WPF-UI las envolvía en un ScrollViewer con altura infinita que además capturaba la rueda.
- Los paneles flotantes se limitan al alto de la pantalla y muestran barra de desplazamiento;
  al expandir una sección se reacomodan sobre la barra en lugar de taparla.
- Con muchas ventanas abiertas, los botones de la barra se cortaban; ahora se desplazan con la
  rueda del mouse.

## [0.2.0] - 2026-10-11

### Añadido
- Panel de red propio: conexión actual (nombre, estado de Internet, IP), activar/desactivar
  Wi-Fi, buscar redes y conectarse con contraseña o desconectarse.
- Panel de Bluetooth propio: activar/desactivar, dispositivos emparejados con su estado,
  conectar/desconectar dispositivos de audio y quitar dispositivos.
- Hub de configuración (WPF-UI, Mica) con secciones Barra de tareas, Bandeja del sistema,
  Apariencia y General. Se abre desde el menú de la barra o ejecutando `WINX.exe` otra vez.
- Barra configurable: alineación (centro/izquierda), tamaño, segundos y fecha en el reloj.
- Área de estado modular: Red, Bluetooth, Volumen y Batería se activan por separado.
- Gestión de la bandeja: elegir qué íconos se muestran siempre y cuáles quedan ocultos;
  la elección se guarda entre sesiones.
- Tema de WINX (sistema/claro/oscuro), color de acento propio y cambio de fondo de pantalla
  con opción de restaurar el original.
- Inicio automático con Windows.
- Configuración en `%LOCALAPPDATA%\WINX\settings.json`.
- Panel de volumen propio: volumen general, cambio de dispositivo de salida y volumen por
  aplicación. La rueda del mouse sobre el ícono ajusta el volumen.
- Sección **Optimización** del Hub, inspirada en Win11Debloat: apps preinstaladas, privacidad,
  IA, sistema, Windows Update, apariencia de Windows, Explorador, multitarea, juegos y Brave.
  Cada cambio guarda el valor original (`tweaks-backup.json`) y se puede deshacer en Respaldo;
  también permite crear un punto de restauración. Lo que requiere administrador pide UAC
  solo para ese cambio.
- `WINX.exe --hub <página>` abre el Hub en una sección (p. ej. `--hub Privacy`).
- `scripts/run.ps1` para cerrar, compilar y relanzar WINX; proyecto de pruebas `tests/Winx.Tests`.

## [0.1.0] - 2026-10-11

### Añadido
- Barra de tareas propia (WPF + ManagedShell) que reemplaza y oculta la de Windows mientras WINX corre.
- Una barra por monitor; cada una muestra solo las ventanas de su pantalla.
- Botones de tareas centrados con indicador de estado (abierta, activa, pidiendo atención),
  clic para activar/minimizar y menú "Cerrar ventana".
- Bandeja del sistema con íconos anclados y menú de íconos ocultos; respeta los íconos
  promovidos en la configuración de Windows 11.
- Área de estado (red, volumen, batería), reloj con fecha y botón "Mostrar escritorio".
- Tema claro/oscuro y color de acento sincronizados en vivo con Windows.
- Menú contextual de la barra: Administrador de tareas, Configuración, Reiniciar y Salir de WINX.
- Argumentos `--exit` (cierre limpio de la instancia en ejecución) y `--restore-taskbar`
  (restauración de emergencia de la barra de Windows).
- Registro en `%LOCALAPPDATA%\WINX\logs\winx.log`.

### Pendiente
- Inicio y notificaciones propios (por ahora abren los de Windows).
