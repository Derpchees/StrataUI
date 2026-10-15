# Changelog

Todos los cambios relevantes de Strata UI (llamado WINX hasta la 1.3) se documentan aquí.
El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [versionado semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido
- **Optimización › Hardware**: el procesador y las gráficas del equipo y, con una tarjeta NVIDIA, los
  ajustes globales de su controlador (modo de energía, baja latencia, filtrado de texturas y caché de
  sombreadores), con "Dejar como estaba". Además, los servicios de datos de uso de NVIDIA, AMD e
  Intel, si están instalados.
- **Limpieza con un clic** (Hub › Optimización › Limpieza): temporales, volcados e informes de
  errores, descargas de Windows Update, caché de Optimización de distribución y miniaturas; opcionales
  la caché de los navegadores, de sombreadores y los registros viejos. Muestra cuánto ocupa cada cosa,
  recuerda lo elegido y lo de Windows lo hace con un solo permiso. Solo borra lo que tiene más de un
  día y se salta lo que esté en uso; la papelera de reciclaje no se toca.
- **Anclar apps a la barra**: desde el clic derecho de una ventana abierta o del menú Inicio
  ("Anclar a la barra de tareas"). Van delante de las ventanas y se ordenan arrastrándolas; abierta,
  la app usa ese mismo botón para sus ventanas (Mayús+clic o "Abrir otra ventana" abre otra). La
  primera vez se traen los anclados de la barra de Windows.
- **Posición de cada elemento en la barra**: al principio, en el centro o al final (izquierda, centro
  o derecha; arriba, centro o abajo en las barras verticales). Se elige en Hub › Barra de tareas, con
  clic derecho › Posición en la barra, o arrastrándolo en la barra (al arrastrar se ven las zonas
  vacías). Lo del centro queda al medio de la barra, o junto a las ventanas si van centradas.
- El tooltip de los medidores de CPU y GPU dice el modelo del procesador y de la tarjeta gráfica
  (las dos, si el equipo tiene integrada y dedicada).
- **Forzar cierre** en el clic derecho de las apps de la barra (como "Finalizar tarea" de Windows).
  Las ventanas del Explorador solo se cierran, para no tumbar el escritorio.
- Soltar un ícono de la bandeja sobre la flecha (^) lo pasa a los ocultos, como en Windows (además
  de soltarlo dentro del panel, que se abre al pasar sobre la flecha).

### Corregido
- **El menú de clic derecho de algunos íconos de la bandeja (WhatsApp) salía en otro monitor**
  cuando la bandeja está en un monitor a la izquierda o arriba del principal: esas apps leen mal las
  coordenadas negativas. Ahora Strata UI lleva el menú junto al ícono.
- La posición de los íconos de la bandeja que se da a las apps (para ubicar sus menús y avisos)
  llegaba como un rectángulo al revés.

### Cambiado
- **WINX ahora se llama Strata UI.** El programa es `StrataUI.exe`, los datos viven en
  `%LOCALAPPDATA%\StrataUI` y las publicaciones en `github.com/Derpchees/StrataUI`.
- Al actualizar desde WINX todo se conserva: la configuración, los anclados, el registro y las
  notificaciones pasan a la carpeta nueva la primera vez que se abre; el inicio automático pasa a la
  tarea "StrataUI"; la carpeta del menú Inicio y el acceso directo se renuevan y se borra el
  ejecutable viejo. Las lecturas avanzadas piden permiso otra vez (como en cada actualización) y al
  aceptarlo se quita la tarea y la copia de WINX.
- README nuevo, en inglés, para GitHub.
- **Logo nuevo**: tres capas, más simple y nítido en cualquier tamaño (dentro de la app se dibuja
  como vector; el `.ico` lleva cada tamaño de 16 a 256 px dibujado aparte).

## [1.3.0] - 2026-10-14

### Añadido
- **Anclar archivos, accesos directos y carpetas en Inicio**: clic derecho en un lugar vacío de los
  anclados › "Anclar archivo o acceso directo…" o "Anclar carpeta…". Se abren con su programa, y
  "Abrir ubicación del archivo" los muestra en su carpeta.
- **Cambiar el ícono de cualquier app de Inicio** (clic derecho › "Cambiar ícono…"): una imagen o el
  ícono de un `.ico`, `.exe` o `.dll`. "Restaurar ícono" vuelve al de Windows.
- **La tecla Windows abre Inicio en el monitor donde está el mouse** si ahí hay una barra con Inicio
  (Hub › Menú Inicio › Teclado; activado por defecto).

### Cambiado
- "Nueva sección" pasó al clic derecho del menú Inicio (en un lugar vacío de los anclados).
- Se quitaron los espacios manuales ("Dejar un espacio antes/después", "Quitar espacio"): con la
  colocación libre ya no hacen falta. Las celdas vacías al final de una sección se descartan solas.
- Los diálogos para elegir archivos (anclar, ícono, fondo de pantalla) se abren en un proceso aparte:
  dentro del proceso de WINX el de Windows podía cerrarlo.

### Corregido
- **El menú Inicio no se actualizaba al desinstalar una app** (ni al instalar una, como una app web
  de Brave o Chrome): ahora vigila las carpetas del menú Inicio y vuelve a leer el catálogo.

## [1.2.0] - 2026-10-12

### Añadido
- **Menú Inicio con colocación libre**: un anclado se arrastra a cualquier celda vacía (después del
  último o en la fila nueva que aparece al arrastrar) y queda ahí; su lugar queda vacío y nada se
  corre. Un contorno marca la celda donde caerá. Ya no hace falta crear huecos con clic derecho.
- Una app que se ancla ocupa el primer hueco del menú en vez de ir al final.

### Cambiado
- El medidor de RAM muestra lo usado y el total ("9.2/32 GB").

## [1.1.0] - 2026-10-12

### Añadido
- **Generación de cuadros en el contador de FPS**: con DLSS FG, FSR FG, XeSS FG, Lossless Scaling o
  la que inserta el controlador (Smooth Motion, AFMF), arriba se ven los FPS en pantalla y abajo los
  reales ("366 / 124 reales"); el tooltip dice cuál se usa. Los reales salen de las marcas de Reflex
  que llevan los juegos con DLSS FG o, si no, de lo que presenta el juego.
- El contador de FPS mide por defecto **la app activa en cualquier monitor** ("Automático"); se
  puede seguir fijando un monitor en Hub › Barra de tareas.

### Cambiado
- La temperatura de la CPU es la que da la placa base (la de la BIOS y las apps del fabricante, más
  estable que la del procesador); sin ella, la del procesador como antes.
- El contador de FPS pide a Windows solo los eventos que cuenta: el proceso de lecturas gasta mucho
  menos.

### Corregido
- **Las ventanas maximizadas quedaban debajo de las barras** cuando el Explorador se reiniciaba o
  arrancaba después de WINX al iniciar sesión: ahora WINX lo detecta y vuelve a reservar el espacio.
- Las lecturas avanzadas se quedaban sin temperaturas ni FPS: el envío se bloqueaba en la tubería
  con el proceso elevado.
- Después de compilar o actualizar WINX sin cambiar de versión, el proceso de lecturas podía seguir
  usando la copia anterior.
- Acentos dañados en el registro del inicio automático.

## [1.0.0] - 2026-10-12

Primera versión estable, con instalador para descargar desde GitHub Releases.

### Añadido
- **Qué lleva cada barra**: menú Inicio, bandeja, red, Bluetooth, brillo, batería, volumen, reloj y
  notificaciones, multimedia y los nuevos medidores se activan con un interruptor en Hub › Barra de
  tareas y se ordenan **arrastrándolos en la barra misma**, también a la barra de otro monitor (un
  clic sigue abriendo su panel). La bandeja va en una sola
  barra, y esa recibe la tecla Windows. Quien no cambie nada conserva lo de siempre.
- **Medidores de rendimiento**: uso de CPU, de GPU (el motor más ocupado, como el Administrador de
  tareas) y de RAM, sin permisos especiales. En las barras verticales van en tres líneas cortas.
- **Temperaturas y contador de FPS** con las "lecturas avanzadas": un proceso aparte con permiso
  de administrador. **Windows lo pide una sola vez** (y otra después de cada actualización): WINX
  crea una tarea programada a tu nombre que ejecuta una copia protegida en Archivos de programa
  (así ningún programa sin permisos puede aprovecharla). Se inician al hacer clic en el contador de
  FPS, desde el Hub o al abrir WINX, y el Hub permite quitar el permiso. El contador mide la app que
  ocupa el monitor elegido y se puede mostrar en la barra de otro. Con el permiso ya dado, empiezan
  solas al abrir WINX. La temperatura de la CPU necesita el controlador PawnIO (pawnio.eu): el Hub
  lo instala con un botón. Una GPU integrada (Intel) muestra la de la CPU, que es el mismo chip; sin
  dato se ve "-- °C" y el tooltip explica por qué.
- **Liberar RAM**: botón para la barra que vacía la memoria que las apps no usan y la caché en
  espera; junto a la barra dice "Liberando RAM…" y después cuánto liberó.
- **Clic derecho en los elementos de la barra**: abrir su página de Configuración de Windows
  (red, Bluetooth, pantalla, sonido, energía, fecha y hora, notificaciones…), sus opciones en WINX
  o quitarlo de esa barra.
- **Acento del fondo de pantalla**: Hub › Apariencia › Origen del color (el de Windows, del fondo o
  propio). Con Wallpaper Engine sale de la vista previa del fondo puesto. Mientras Wallpaper Engine
  tenga "Adjust Windows color" activado, la opción queda en pausa con un aviso: los dos se pisarían
  (y Wallpaper Engine copia el color que puso el autor de cada fondo, a menudo negro).
- Con "Aplicar el tema a Windows", Windows usa también el acento de WINX (Inicio, Configuración,
  barras de título) y recupera el suyo al desactivarlo.
- Botones del pie del menú Inicio: se ordenan arrastrando en el Hub.
- Notificaciones con la imagen del aviso (foto del remitente o logotipo del sitio), como en Windows.

### Cambiado
- **Las animaciones elegidas se aplican a toda la app**: también a menús contextuales, tooltips,
  íconos ocultos de la bandeja, carpetas del menú Inicio, el Hub y sus páginas, y a la salida de
  los avisos.
- El acento se ve igual en WINX y en los controles del Hub, y nunca queda invisible (un acento casi
  negro se aclara sobre el tema oscuro). WINX nota cuando cambia el acento de Windows.
- **Personalización por monitor**: arriba de cada página se elige el monitor y se editan sus
  ajustes: qué lleva su barra (un interruptor por elemento), alineación, tamaño, vista previa,
  agrupar, posición, reloj, temperaturas y batería; su tema y barra transparente (también los
  paneles y el menú Inicio que se abren desde ella); si los avisos salen ahí y en qué esquina;
  multimedia; y sus avisos en pantalla. "Copiar a todos" lleva los ajustes a los demás monitores.
  Lo que no depende del monitor (acento, fondo, animaciones, centro de notificaciones, lecturas
  avanzadas) va en "Para todos los monitores". Quien no cambie nada conserva lo de antes.
- **Avisos en pantalla por monitor y por tipo**: volumen, silencio, brillo, Bloq Mayús, Bloq Num y
  Bloq Despl se activan uno por uno en Paneles, en cada monitor junto a su barra; el de brillo
  sale en el monitor cuyo brillo cambió.
- **Hub reorganizado**: Resumen y Menú Inicio (igual en todos los monitores); Personalización por
  monitor con Barra de tareas, Bandeja del sistema, Notificaciones, Apariencia, Control multimedia
  y Paneles; Optimización como grupo fijo, siempre a la vista. Las páginas abren desde arriba.
- Instalador: asistente completo (bienvenida, carpeta de instalación, carpeta del menú Inicio,
  tareas y resumen) y opción de instalar para todos los usuarios.

### Corregido
- **El menú Inicio no se animaba al deslizar**: se movían solo sus asas para redimensionar (una
  capa invisible encima), no el menú.
- **Íconos de las notificaciones**: las de apps web en una pestaña normal salen con el ícono y el
  nombre del sitio (WINX lee el aviso original de la base de datos de notificaciones de Windows). Las
  apps de escritorio que registran su ícono para avisos (Thunderbird, TeamViewer, avisos del
  sistema) ya no salen con el genérico, y las apps sin identidad dejaron de compartir ícono entre sí.

## [0.17.0] - 2026-10-12

### Cambiado
- **WINX arranca antes que las demás apps al iniciar sesión**: el inicio automático pasa de la
  clave Run (que Windows demora) a una tarea programada sin retraso y con prioridad normal, también
  con batería. Así los íconos de bandeja de las apps que abren al iniciar llegan a WINX y no se
  quedan en la barra del Explorador. Quien ya lo tenía activado se cambia solo al abrir WINX. No
  corre como administrador a propósito: con WINX elevado las apps no podrían mandarle sus íconos y
  todo lo que se abriera desde WINX saldría elevado.

## [0.16.3] - 2026-10-12

### Corregido
- **Íconos de la bandeja que se perdían al reiniciar WINX** (Spotify, ControlCenter4 de Brother y
  cualquier app que registra su ícono una sola vez): WINX recuerda los íconos que tiene y, al volver
  a abrir, los devuelve mientras la app siga abierta; los clics le llegan como siempre.
- El apilado de las ventanas agrupadas ya no parece una raya suelta: la tarjeta de atrás va pegada a
  la placa.

## [0.16.2] - 2026-10-12

### Cambiado
- Las ventanas agrupadas de una app ya no muestran un número: el botón se ve apilado, como en
  Windows (la placa con el borde de otra asomando detrás), igual a partir de 2 ventanas. La placa
  queda tenue si ninguna está activa. En las barras verticales se apilan hacia abajo.

## [0.16.1] - 2026-10-12

### Corregido
- **Íconos de la bandeja**: si al iniciar WINX las apps mandaban sus íconos a la barra del
  Explorador, la bandeja de WINX se quedaba vacía o incompleta (y el botón de ocultos aparecía y
  desaparecía). Ahora WINX vigila a dónde van y, en cuanto le llegan a él, les pide a las apps que
  los vuelvan a registrar.
- Un ícono cuyo texto cambia (Thunderbird con "1 mensaje no leído", Steam sincronizando…) ya no
  cuenta como ícono nuevo: conserva su lugar entre los ocultos y no se vuelve a anclar solo.

## [0.16.0] - 2026-10-12

### Añadido
- **Apps web en la barra**: una pestaña con YouTube, YouTube Music, WhatsApp Web, Gmail, Spotify,
  Netflix, Discord, ChatGPT y otros sitios conocidos se muestra con el ícono del sitio (y el
  navegador en chiquito en la esquina). Los íconos se descargan una vez y se guardan en
  `%LOCALAPPDATA%\WINX\icons`.
- **Multimedia de apps web**: lo que suena en el navegador muestra el sitio (p. ej. YouTube) en
  lugar de "Brave", con su ícono en la esquina de la carátula; el clic trae al frente esa pestaña.
  Las demás apps también llevan su ícono en la carátula.
- **Agrupar las ventanas de cada app** (Hub › Barra de tareas): un solo botón por app con el
  número de ventanas; el clic muestra todas en la vista previa para elegir y el indicador marca si
  alguna está activa. Al arrastrar se mueve el grupo entero. Cada sitio web cuenta como su propia app.
- La vista previa muestra varias ventanas a la vez.

### Corregido
- **Borrar todo** en el centro de notificaciones: Windows a veces rechaza el borrado en bloque y las
  notificaciones volvían; ahora se quitan una por una y las que Windows no deja quitar ya no se muestran.
- Las notificaciones de apps web instaladas (WhatsApp Web de Brave…) muestran su nombre e ícono.
- **Barra totalmente transparente**: al activarla desde el Hub quedaba negra; ahora las barras se
  recrean al cambiarla, como con la posición.

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
