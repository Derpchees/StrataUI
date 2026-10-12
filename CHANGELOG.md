# Changelog

Todos los cambios relevantes de WINX se documentan aquí.
El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [versionado semántico](https://semver.org/lang/es/).

## [Sin publicar]

## [0.15.2] - 2026-10-12

### Cambiado
- La batería con porcentaje sigue el estilo de los demás íconos del área de estado: línea fina,
  un solo color y el mismo alto, con el relleno del nivel tenue y el rayo junto al número al cargar
  (sin verde ni rojo).

## [0.15.1] - 2026-10-12

### Cambiado
- Porcentaje de la batería rediseñado: una batería dibujada (cuerpo redondeado y relleno según el
  nivel) con el número centrado dentro; mientras carga, el relleno es verde y lleva un rayo junto
  al número; con 20 % o menos, rojo.

## [0.15.0] - 2026-10-12

### Añadido
- **Vista previa de las ventanas**: al pasar el mouse por una app abierta se ve su ventana en vivo
  (miniatura de Windows), con su título y un botón para cerrarla; clic: la trae al frente. Se
  desactiva en Hub › Barra de tareas.
- **Ordenar las ventanas de la barra** arrastrándolas, como en Windows (vale para todas las barras
  durante la sesión).
- **Panel de batería**: nivel, si carga o cuánto queda (o cuánto falta para cargarse), modo de
  energía de Windows (Eficiencia, Equilibrado, Rendimiento), ahorro de batería y salud de la batería.
- **Porcentaje de la batería** dentro del ícono (opción en Hub › Bandeja y área de estado) y rayo
  mientras carga; el ícono normal también indica la carga.
- **Avisos en pantalla (OSD)** junto a la barra al cambiar el volumen o el brillo, al silenciar y al
  pulsar Bloq Mayús, Bloq Num o Bloq Despl. Las teclas de volumen las atiende WINX (pasos de 2, como
  Windows) para que Windows no muestre también el suyo.
- **Barra totalmente transparente** (Hub › Apariencia): sin fondo, borde ni desenfoque. Cada parte de
  la barra pone su texto y sus íconos claros u oscuros según el fondo que tiene detrás, también con
  fondos animados (Wallpaper Engine).
- **Animaciones a elegir** para paneles, menú Inicio, avisos y OSD: Deslizar, Desvanecer, Acercar,
  Rebote, Subir desde lejos o ninguna, y su velocidad. Los paneles ahora también se animan al cerrarse.
- **Controles multimedia opcionales**: un interruptor en el Hub muestra u oculta anterior,
  reproducir/pausa y siguiente.

### Cambiado
- Con el centro de notificaciones de WINX activo se apagan los avisos emergentes de Windows (las
  notificaciones siguen llegando a su centro, que es de donde las lee WINX). Al salir de WINX o al
  desactivar su centro se devuelve lo que cada app tenía.
- Multimedia: la barra muestra la canción con su carátula, artista y álbum. El clic abre la app que
  suena (ya no hay panel multimedia); el clic central sigue pausando.
- Red, Bluetooth, brillo, volumen y batería van más juntos en la barra (también en vertical).

## [0.14.0] - 2026-10-12

### Añadido
- **Controles multimedia en la barra**: anterior, reproducir/pausa y siguiente junto a la
  carátula (en fila en barras horizontales, en columna en las verticales). El panel sigue
  abriéndose con un clic en la carátula.

### Corregido
- La carátula no aparecía con Spotify y otras apps que envían la imagen después del título:
  ahora se reintenta hasta que llega.

## [0.13.0] - 2026-10-12

### Añadido
- **Dos barras por monitor**: una segunda barra en el borde opuesto (arriba y abajo, o izquierda y
  derecha) que lleva las apps abiertas, el área de estado y el reloj (con la bandeja si es el
  monitor principal), o todo. Con las dos primeras opciones las barras se reparten el contenido.
- **Control multimedia**: carátula, canción y artista de lo que suena en cualquier app que se
  anuncie a Windows (Spotify, Reproductor multimedia, Deezer, YouTube y otros sitios en el
  navegador…). En la barra, la carátula (y en horizontal el título y el artista); al hacer clic,
  un panel con avance, anterior/pausa/siguiente y, si hay varias apps, cuál controlar. Clic
  central: pausa.
- **Optimización › Rendimiento**: menús instantáneos, respuesta más rápida al pasar el mouse, sin
  animación al minimizar, sin espera para las apps de inicio, cerrar antes las apps colgadas, sin
  limitación de energía, programación de GPU por hardware, más prioridad para juegos, menos reserva
  para multimedia de fondo, impedir apps de la Tienda en segundo plano y desactivar la indexación.
  Todas reversibles desde Respaldo.

## [0.12.0] - 2026-10-12

### Añadido
- **Centro de notificaciones** propio junto al reloj (y con Win+N): las notificaciones de Windows
  con su app y hora, quitar una o todas, clic para abrir la app, "No molestar" y calendario. El
  reloj muestra cuántas hay.
- **Avisos emergentes** de notificaciones en el monitor y la esquina elegidos en el Hub, con
  duración configurable; no aparecen con "No molestar" ni con una app a pantalla completa.
- **Brillo**: botón y panel con un control por monitor (DDC/CI en monitores externos, WMI en la
  pantalla del portátil) y uno para todos; la rueda del mouse sobre el botón lo ajusta.
- **Bandeja ordenable** como en Windows: arrastrar reordena los íconos ocultos, y entre la barra y
  los ocultos los ancla o desancla (la flecha ^ se abre al pasar por encima).
- **Huecos en el menú Inicio**: "Dejar un espacio antes/después"; soltar una app sobre un hueco la
  coloca ahí sin correr las demás.
- Clic derecho en las apps del menú Inicio: **Ejecutar como administrador**, **Abrir ubicación del
  archivo** y **Desinstalar** (las de la Tienda se quitan tras confirmar; las demás abren su
  desinstalador).
- **Actualizaciones** en el Hub: busca versiones nuevas en GitHub (automáticamente al iniciar y
  cada 6 horas, desactivable), muestra las novedades e instala con un clic.
- **Instalador** (Inno Setup): por usuario, sin administrador, con inicio automático opcional;
  cierra WINX al actualizar y lo vuelve a abrir.

### Cambiado
- **Hub rediseñado**: portada con accesos rápidos, navegación por grupos (Personalización,
  Sistema), cabecera con ícono y descripción en cada página. Los módulos del área de estado y el
  reloj pasan a la página "Bandeja y área de estado".
- **Menos consumo**: red y batería se actualizan cuando Windows avisa en lugar de cada 5 s en cada
  barra; un solo reloj para todas las barras que, sin segundos, despierta una vez por minuto;
  WINX devuelve la memoria que no usa tras arrancar y al cerrar paneles (de ~300 MB a ~20 MB de
  conjunto de trabajo en reposo); el instalador lleva el código precompilado (arranque más rápido).
- `--exit` espera a que la instancia en ejecución termine de cerrarse.
- El registro se rota al pasar de 5 MB (`winx.old.log`).

### Corregido
- "Desactivar la búsqueda web (Bing)" y demás directivas en `HKCU\Software\Policies` fallaban sin
  permisos: ahora piden administrador (UAC), también al restaurarlas.
- "Anclar a Inicio"/"Desanclar de Inicio" y "DISPOSITIVOS EMPAREJADOS" no se traducían.

## [0.11.1] - 2026-10-11

### Corregido
- En inglés, el botón y el menú Inicio decían "Home": compartían texto con la opción "Inicio" del
  Explorador, que ahora se llama "Inicio del Explorador".

### Cambiado
- CLAUDE.md: conceptos de la arquitectura, consejos para probar y trampas nuevas.

## [0.11.0] - 2026-10-11

### Añadido
- **Monitor principal de WINX**, independiente del de Windows: lleva la bandeja del sistema y abre
  el menú con la tecla Windows.
- Opción para mostrar red, Bluetooth, sonido y reloj en todas las barras o solo en la principal.
- **Alineación "Igual que Windows"** (nueva opción por defecto): sigue la de la barra de Windows,
  también si se cambia mientras WINX está abierto.
- **Mostrar las apps abiertas en**: igual que Windows, todas las barras, la principal y la del
  monitor de la ventana, o solo la de su monitor (reemplaza el interruptor anterior).
- **Centrar las apps abiertas** aunque Inicio esté a la izquierda.
- **Clic derecho en Inicio y Win+X**: el menú de Windows con sus opciones (aplicaciones instaladas,
  energía, visor de eventos, administrador de dispositivos, discos, Terminal y Terminal como
  administrador, Ejecutar, apagar o cerrar sesión, escritorio…), junto al botón en cualquier borde.

## [0.10.0] - 2026-10-11

### Añadido
- **Varios idiomas**: WINX usa el idioma de la interfaz de Windows. Por ahora español e inglés;
  en cualquier otro idioma se muestra en inglés. Se puede fijar en Hub › General › Idioma.
- Toda la interfaz está traducida: barra, paneles, menú Inicio, Hub y el catálogo de
  optimizaciones (los registros técnicos siguen en español).

## [0.9.0] - 2026-10-11

### Añadido
- Posición de la barra **en cada monitor**: abajo, arriba, izquierda o derecha (Hub › Barra de
  tareas › Monitores).
  - En los bordes laterales la barra es vertical: Inicio y ventanas arriba, bandeja, estado y
    reloj abajo, con la fecha sin el año.
  - Los paneles y el menú Inicio se abren pegados a la barra, entren por ese lado y tienen las
    asas de tamaño en los bordes libres; el indicador de ventana abierta va junto al borde.
  - En los temas flotantes la cápsula se separa de los tres lados libres.
- Mostrar u ocultar la barra en los monitores secundarios; las ventanas de un monitor sin barra
  aparecen en la principal.
- Opción para que cada barra muestre solo las ventanas de su monitor o todas.

## [0.8.0] - 2026-10-11

### Añadido
- Ícono de WINX en el ejecutable y en la ventana del Hub.
- Opción para mostrar u ocultar los nombres de las apps ancladas en el menú Inicio (sin ellos, el
  nombre aparece al pasar el mouse).
- Panel de sonido rediseñado:
  - Salida y entrada, cada una con su volumen y sus dispositivos como mosaicos cuadrados con
    ícono según el tipo (bocinas, audífonos, diadema, monitor, digital, Bluetooth, micrófono).
  - Opciones de cada dispositivo: nivel en vivo (en micrófonos, para probarlo), balance
    izquierda/derecha, mejoras de audio, control exclusivo y usarlo como predeterminado para
    llamadas. Las mejoras y el control exclusivo piden permiso de administrador (UAC).
  - Los dispositivos están plegados por defecto; la fila muestra el que está en uso.

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
