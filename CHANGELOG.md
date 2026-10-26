# Changelog

Todos los cambios relevantes de Strata UI (llamado WINX hasta la 1.3) se documentan aquí.
El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [versionado semántico](https://semver.org/lang/es/).

## [Sin publicar]

## [3.14.0] - 2026-10-25

### Añadido
- **Archivos: comprimidos sin otro programa.** Un .zip, .7z, .rar, .tar.gz y unos 50 formatos más
  (con 7-Zip incluido) se abren como una carpeta: se navegan, se abren sus archivos, se copian y se
  extraen con su progreso ("Extraer aquí", "Extraer en…", "Extraer todo"). Con contraseña, se pide
  con el diálogo de Strata UI. "Extraer aquí" no mete una carpeta dentro de otra si todo viene en una.
- **Comprimir**: "Comprimir en «nombre.7z»" (7z con la compresión máxima, lo que mejor comprime) y
  "Comprimir…" para elegir nombre, formato (7z, zip o tar), nivel y contraseña (AES-256; en 7z
  también los nombres). También desde el escritorio.
- **Archivos: formatear unidades** (clic derecho en la unidad): NTFS, exFAT o FAT32, nombre y
  formato rápido. Nunca la unidad de Windows, la de Strata UI ni la de los datos del usuario.
- **La barra usa el ícono elegido en Vesktop** (su personalización de la bandeja) y el de "no leídos"
  cuando hay mensajes nuevos; si se cambia en Vesktop, la barra se actualiza sola.

### Cambiado
- **Permiso de administrador con el estilo de Strata UI**: copiar, mover, borrar, cambiar el nombre
  o crear una carpeta en un lugar protegido pregunta con el diálogo de Strata UI (escudo en
  "Continuar") en vez de las ventanas de Windows, y lo hace en un proceso elevado con el progreso en
  la misma tarjeta. Al reintentar, solo se repite lo que falló y al mismo destino.
- Abrir un comprimido ya no lanza NanaZip, 7-Zip ni WinRAR: ya no hace falta tenerlos.

### Corregido
- **Íconos ocultos de la bandeja**: con clic derecho, el panel se cerraba y el menú de la app no se
  podía usar. Ahora sigue abierto mientras el menú está visible y se cierra al terminar.
- Un error del reloj de la barra al cerrar Strata UI (quedaba en el registro).

## [3.13.0] - 2026-10-25

### Añadido
- **Archivos: progreso de copiar y mover dentro de la ventana**, sin la de Windows: velocidad, tiempo
  restante, el archivo en curso y un botón para cancelar. Mover dentro de la misma unidad es
  instantáneo; los archivos grandes se copian sin la caché de Windows (más rápido). Lo que falla por
  permisos se puede reintentar como administrador. Desde el escritorio, sin Archivos abierto, el
  resultado llega como notificación.
- **Archivos: búsqueda en las subcarpetas**: con una pausa al escribir, también busca más abajo y
  cada resultado dice en qué carpeta está.
- **Archivos: panel de vista previa** (Alt+P): imágenes con su tamaño real, la primera página de un
  PDF, el comienzo de textos y código, carátulas de video y música, y cuántos elementos tiene una
  carpeta.

### Cambiado
- **Diálogos propios con el tema de Strata UI** (nombres repetidos al copiar o mover, borrar para
  siempre, vaciar la Papelera, una unidad sin Papelera, desinstalar desde Inicio, Optimización) en
  vez de los de Windows o WPF-UI; salen sobre la ventana desde la que se pidieron, en su monitor.
- **El panel de sonido abre al instante**: la lista de dispositivos se guarda hasta que Windows avisa
  de un cambio (antes se leía tres veces en cada apertura, unos 650 ms).

### Corregido
- **Reemplazar y cancelar ya no pierde el archivo que estaba**: se copia aparte y se cambia al final.
- **Archivos: Pegar** quedaba apagado si se copiaban archivos en otra app con la ventana abierta.
- **Archivos: los botones de vista** (lista, cuadrícula, ocultos, extensiones, vista doble) no
  respondían a los lectores de pantalla ni a la automatización, y cada archivo se anunciaba con el
  nombre de una clase.
- Un elemento que ya no existe al copiar o mover ya no cancela todo lo demás.
- El mosaico Bluetooth del panel de sonido conserva su "Conectando…" si la lista se rehace.

## [3.12.1] - 2026-10-25

### Corregido
- **Archivos: Mayús+Supr** borraba a la Papelera; ahora borra para siempre, como el Explorador
  (Windows pregunta antes).
- **Archivos: un nombre que termina en punto** ("nota.") se aceptaba y Windows le quitaba el punto en
  silencio; ahora se avisa. El cambio de nombre tampoco puede mover el archivo a otra carpeta.
- **Carátulas de PDF**: con el navegador como visor de PDF, Windows no da miniatura y se le
  preguntaba igual por cada archivo; ahora, tras el primero, se usa directo la primera página.
- Un emoji partido en el texto de un ícono de la bandeja ya no rompe la escritura del registro.

## [3.12.0] - 2026-10-25

### Añadido
- **Batería de los dispositivos Bluetooth** en el panel ("Conectado · 80 % de batería"), la misma que
  muestra Configuración.
- **Salidas Bluetooth en el panel de sonido**: los audífonos y parlantes emparejados sin conexión
  aparecen como mosaico; al tocarlo se conectan y pasan a ser la salida.
- **Aviso de sensores lentos**: si una pieza (la placa, la GPU…) tarda segundos en leerse y se deja
  de leer, el Hub y el tooltip del medidor dicen cuál y por qué.

### Cambiado
- **Sin permiso de administrador después de actualizar**: el instalador (ya elevado) baja el módulo
  Lecturas avanzadas de la nueva versión directo a la carpeta protegida, en vez de borrarlo; así la
  tarea queda al día y las temperaturas vuelven solas.
- **El menú Inicio arma su lista una sola vez** al abrirlo (antes, tres veces seguidas).

### Corregido
- El registro ya no se llena con la pila completa cuando no hay internet al buscar actualizaciones,
  ni repite "Hora del sistema cambiada" varias veces por un mismo ajuste.

## [3.11.0] - 2026-10-25

### Añadido
- **Emparejar desde el panel de Bluetooth**: con la radio encendida, el panel busca los dispositivos
  cercanos (clásicos y LE) y los muestra en "Dispositivos disponibles"; se emparejan ahí mismo, sin
  abrir Configuración, también los que piden confirmar o escribir un PIN.

### Cambiado
- **Las lecturas avanzadas empiezan solas al instalar el módulo**: al terminar la descarga se inician
  (Windows pide permiso una vez) y los medidores muestran las temperaturas sin pulsar Iniciar. Tras
  una actualización pasa lo mismo si alguna barra o widget muestra CPU, GPU o FPS.

### Corregido
- **Bluetooth decía "Emparejado" aunque el dispositivo estuviera conectado**: el estado se lee de la
  misma propiedad que usa Windows y se actualiza en vivo mientras el panel está abierto.

## [3.10.2] - 2026-10-23

### Corregido
- **Las lecturas avanzadas parecían rotas tras actualizar**: el permiso de administrador se renueva
  con cada actualización de Strata UI o del módulo, y mientras tanto los medidores quedaban sin
  temperaturas ni FPS con la tarjeta del Hub diciendo solo "Detenidas". Ahora explica que hay que
  pulsar Iniciar y aceptar el aviso de Windows.

## [3.10.1] - 2026-10-23

### Corregido
- **No se veían las páginas de Optimización**: quedaban en una entrada dentro de Módulos opcionales
  que el menú del Hub no despliega. Ahora van directamente en Módulos opcionales, debajo de
  "Instalar y quitar".

## [3.10.0] - 2026-10-23

### Añadido
- **Nombre propio para las carpetas ancladas** en la barra lateral de Archivos (clic derecho ›
  "Cambiar el nombre en la barra lateral"): solo cambia cómo se ve ahí, la carpeta queda igual. Útil
  con dos carpetas que se llaman igual en lugares distintos.
- **Sincronía en "Sonar también en"**: un control de retraso para la segunda salida, por si suena
  antes que la principal (por ejemplo, con la principal por Bluetooth).

### Cambiado
- **Módulos opcionales**: Optimización, Archivos y las Lecturas avanzadas (temperaturas y FPS) ya
  no vienen en el instalador. Se instalan con un clic en Hub › Módulos opcionales, que junta los
  tres (y, dentro, las páginas de Optimización). El instalador pesa unos 10 MB menos. Sin Archivos,
  las carpetas se abren en el Explorador de Windows; el escritorio de Strata UI sigue igual.
- **Importar** pasa a la parte de abajo del Hub, junto a General.

### Corregido
- **"Sonar también en" se desincronizaba**: la segunda salida se iba atrasando con los minutos
  (los relojes de las dos salidas no van igual) y, cuando se vaciaba el búfer, saltaba. Ahora el
  camino es más corto y se corrige de a poco, sin saltos.

## [3.9.1] - 2026-10-22

### Añadido
- **Seleccionar con un recuadro** en Archivos: arrastrar desde el espacio libre (o entre las
  carátulas) marca todo lo que toca; con Ctrl se suma a lo ya seleccionado, y al llegar al borde la
  lista se desplaza sola.
- **Arrastrar y soltar en la barra lateral de Archivos**: soltar archivos sobre una carpeta (anclada,
  del usuario, unidad o red) los mueve o copia ahí, sobre la Papelera los manda a la Papelera, y
  soltar una carpeta en el espacio libre de la barra la ancla. Las ancladas se ordenan arrastrándolas.

### Corregido
- **La raíz de una unidad** (por ejemplo, `C:\`) abría la carpeta de Strata UI en lugar de la raíz.

## [3.9.0] - 2026-10-22

### Añadido
- **Escritorio de Strata UI**: los íconos del escritorio con el estilo de Archivos (carátulas,
  selección con recuadro, arrastrar para acomodarlos, cambiar nombre, soltar archivos, tamaños y
  orden) y su propio menú contextual: Vista, Ordenar por, Pegar, agregar un widget o editar el
  diseño, abrir en Archivos o en la terminal, personalizar Strata UI y lo que pone Windows ahí
  (Nuevo, Configuración de pantalla, Personalizar, el panel de la tarjeta de video…). "Mostrar
  íconos del escritorio" respeta cómo los tenías; los widgets quedan siempre encima de los íconos.
  Se apaga en Hub › General y, al salir, el escritorio de Windows vuelve como estaba.
- **Más formatos en el visor**: GIF animados, SVG, fotos RAW de cámara, HEIF/AVIF, y video y audio
  con los códecs de la Tienda (webm, VP9, AV1, HEVC, ogg, opus, mkv…): el visor usa ahora el
  reproductor moderno de Windows. Los PDF y documentos de oficina se abren con tu app (en la
  cuadrícula, la primera página del PDF sirve de carátula); los SVG también tienen carátula.
- **Pestañas y vista doble en Archivos**: pestañas en la barra de título (Ctrl+T, Ctrl+W, Ctrl+Tab,
  clic con la rueda para abrir una carpeta en otra pestaña o cerrar una) y dos carpetas lado a lado
  en la misma pestaña (Ctrl+Mayús+D), para arrastrar de una a otra.
- **Carátulas en Archivos**: videos, música, PDF y documentos muestran su vista previa (las pide a
  Windows un proceso aparte), con el botón de reproducir sobre los videos; íconos más nítidos en la
  cuadrícula, y los nombres largos ya no corren ni cortan las carátulas.
- **Anclar carpetas** a la barra lateral de Archivos (clic derecho) y la **Papelera de reciclaje**
  en la barra lateral, con "Vaciar la Papelera".
- **Mostrar u ocultar las extensiones** de los archivos con un botón.
- **Ícono propio de Archivos** (una carpeta): su botón en la barra ya no se mezcla con el del Hub.
- **Desplazamiento suave** con la rueda en Archivos.
- **Sonar también en** (panel de Sonido): lo que suena en la salida principal se oye además en otra,
  por ejemplo los parlantes de los dos monitores. Un interruptor, la lista de salidas (sugiere la de
  un monitor) y el volumen de la segunda; sigue a la salida principal si cambia.
- **Archivos reemplaza al Explorador dentro de Strata UI**: Win+E, el botón Explorador y las carpetas
  del menú Inicio y de Win+X abren Archivos (Hub › General para volver al Explorador de Windows).
- **Visor de Archivos** para imágenes, video y audio: la imagen se ajusta a la ventana, la rueda
  acerca donde está el mouse, se arrastra para moverla, doble clic para el tamaño real, girar y la
  orientación de las fotos del celular. Video y audio con controles que se ocultan solos, volumen y
  pantalla completa. Las flechas pasan al anterior o al siguiente de la carpeta.
- **Nube en Archivos**: OneDrive, Google Drive, Dropbox, iCloud Drive y demás (solo si están
  instalados: una carpeta que quedó de una app desinstalada ya no sale como nube) en la barra lateral, con el estado de cada archivo (solo en línea, en el equipo o siempre en el
  dispositivo) y "Mantener siempre en este dispositivo" / "Liberar espacio". Las miniaturas no
  descargan archivos que están solo en la nube.
- **Red en Archivos**: las ubicaciones de red de Este equipo y las unidades de red desconectadas; si
  una no responde, un aviso claro con "Reintentar" en lugar de quedarse cargando. Carpetas compartidas (`\\servidor\recurso`, y escribir `\\servidor` lista sus
  carpetas), unidades de red y "Conectar unidad de red"; las carpetas de red visitadas quedan en la
  barra lateral.
- **El menú contextual de Windows dentro del de Archivos**: las opciones que agregan Windows y tus
  apps (NanaZip, 7-Zip, Abrir con, Enviar a, Defender, Propiedades, Nuevo…), con sus íconos y
  submenús, también las de programas que instales después. Se leen en un proceso aparte para que
  una extensión que falle no cierre Strata UI.
- **Papelera de reciclaje dentro de Archivos**: lo borrado con su fecha y su ubicación original,
  Restaurar, Eliminar definitivamente y Vaciar la Papelera; soltar archivos encima los manda ahí.
- **Anclar a Inicio** desde el menú de Archivos.
- **Comprimidos con tu compresor**: se abren con el que tengas instalado (NanaZip, 7-Zip, WinRAR,
  PeaZip, Bandizip), zip incluido, en lugar de la carpeta comprimida de Windows; y "Extraer en una
  carpeta" con su ventana de progreso.

### Cambiado
- **Notificaciones agrupadas, más limpias**: cada app es una sola tarjeta con su ícono, nombre, hora
  y una píldora con cuántas hay; al desplegarla, las demás aparecen dentro de la misma tarjeta, con
  su hora y una X al pasar el mouse. Un clic en el mensaje lo abre.

### Corregido
- **Clic en notificaciones de apps que viven en la bandeja** (Thunderbird minimizado a la bandeja y
  otras): la app recibía el clic pero seguía oculta. Ahora, si no se muestra, Strata UI hace clic en
  su ícono de la bandeja (o muestra su ventana) y queda al frente.

## [3.8.0] - 2026-10-22

### Añadido
- **Archivos**, un explorador de archivos propio con el tema de Strata UI: portada con tus carpetas,
  unidades (con el espacio usado) y archivos recientes; cada carpeta en lista o en cuadrícula con
  miniaturas; ruta en migas que también se puede escribir (Ctrl+L), búsqueda en la carpeta, orden
  por nombre, fecha, tipo o tamaño y archivos ocultos a un clic. Copiar, cortar, pegar, cambiar el
  nombre, nueva carpeta y mandar a la Papelera usan el motor de Windows (con su progreso y Ctrl+Z), y
  el portapapeles y el arrastre funcionan con el Explorador y las demás apps. Se abre desde el menú
  del ícono de Strata UI en la bandeja, con el botón "Archivos de Strata UI" del pie de Inicio o con
  `StrataUI.exe --files [carpeta]`.
- **Notificaciones agrupadas por app**, como en el celular: varias de la misma app, sitio o app web
  se pliegan en una pila con la más reciente arriba y las demás asomando detrás. Un clic en la pila
  (o en "Ver todas") las despliega, y la X del grupo las quita todas juntas. Así el centro de
  notificaciones ya no se alarga con cada mensaje.

### Corregido
- **Clic en las notificaciones de apps de la Tienda** (WhatsApp, Spotify y otras empaquetadas): solo
  abría la app, sin ir al chat o a la canción. Ahora el clic llega a su activador, como desde Windows.
  Además, la app que se abre pasa al frente en lugar de solo parpadear en la barra.
- **Clic en notificaciones de apps sin activador**: si la app ya está abierta, se trae su ventana al
  frente en vez de abrir otra; y si Windows no la sabe abrir por su identidad, se busca en el menú
  Inicio por su nombre.

## [3.7.2] - 2026-10-22

### Corregido
- **Clic en las notificaciones de Thunderbird** (y otras apps de escritorio con su propio activador,
  como Firefox): no pasaba nada. Ahora el clic llega a la app como desde Windows y abre el correo o
  la conversación del aviso.

## [3.7.1] - 2026-10-18

### Corregido
- **Juegos repetidos en el menú Inicio**: un juego de Steam con signos en el nombre ("Black Myth: Wukong",
  "Warhammer 40,000: Space Marine 2") salía dos veces, porque su acceso directo no puede llevar ":" y
  se llama distinto. Ahora los nombres se comparan sin puntuación ni acentos y queda una sola entrada,
  con el ícono del juego.

## [3.7.0] - 2026-10-18

### Añadido
- **Desinstalar la Game Bar de Xbox** (Optimización › Apps preinstaladas): quita la Game Bar y la
  grabación de juegos en segundo plano, y Windows deja de pedir "una aplicación para abrir este
  vínculo ms-gamingoverlay" al iniciar un juego. Si ya se había quitado, ofrece limpiar esos restos;
  si se reinstala desde la Tienda, sus vínculos vuelven a abrirla.
- **Desactivar Edge** donde Windows no deja desinstalarlo (fuera del Espacio Económico Europeo):
  queda sin accesos directos, sin arranque rápido ni procesos en segundo plano, y sus actualizaciones
  no vuelven a crear accesos directos. Un botón lo restaura como estaba.

### Cambiado
- **Arrastrar íconos de la bandeja** se siente como el resto de la barra: el ícono va en la mano con
  su marco de acento, queda tenue en su lugar y los demás se deslizan en vez de saltar (también en el
  panel de ocultos, que ya no tapa la imagen al abrirse).
- **README** nuevo, más ordenado y sin emojis.

### Corregido
- Un ícono de la bandeja arrastrado hacia la derecha quedaba uno o más lugares antes de donde se
  soltó (la posición se contaba con los anclados que no se ven, como el volumen de Windows).
- **Desinstalar Edge desde Inicio** no hacía nada: ahora usa el mismo camino que Optimización
  (desinstalar o, si Windows no deja, desactivar).
- Inicio ya no ofrece "Desinstalar" en las partes de Windows que no se pueden quitar ("Introducción"
  daba un error).

## [3.6.1] - 2026-10-18

### Corregido
- **Menú Inicio**: los anclados se pueden volver a mover. El arrastre "a la barra de tareas" del menú
  se quedaba con el mouse antes que el de los anclados, y soltar en una celda vacía no hacía nada si
  había anclada alguna app que ya no está instalada (la celda se contaba con las apps ocultas).
- **Reloj**: sigue los cambios de zona horaria (la zona automática al viajar o cambiar de horario) y
  de hora del sistema; antes se quedaba con la zona que había al abrir Strata UI. También se vuelve a
  alinear al salir de la suspensión.

## [3.6.0] - 2026-10-17

### Añadido
- **Optimización › Memoria RAM**: agrupar los servicios de Windows (menos procesos svchost), desactivar
  SysMain, el servicio de telemetría, los Widgets, Vincular teléfono y los procesos de Edge en segundo
  plano. Todo se puede deshacer en Respaldo.
- **Desinstalar Microsoft Edge** (Optimización › Apps preinstaladas). WebView2 se queda y Windows
  Update no lo reinstala.
- **Sonido de notificación nuevo**: un "tin" de vaso de vidrio, corto y elegante.

### Cambiado
- Los menús y paneles ya no se ven opacos un instante al abrirse: aparecen cuando su vidrio ya está.
- "Abrir ubicación del archivo" no se ofrece en las apps de la Tienda (no tienen un archivo que mostrar).

### Corregido
- **A veces salían los avisos de Windows junto a los de Strata UI**: los programas de escritorio sin
  identificador propio no se apagaban (su ajuste de Windows está en otro nivel del registro), y una
  app nueva o que Windows reactivaba tardaba en apagarse. Ahora se apagan todos y al momento.

## [3.5.0] - 2026-10-17

### Añadido
- **Velocidad de internet en Mb/s o MB/s** (clic derecho en el medidor › Unidad) y **Probar velocidad**
  desde ese mismo clic derecho.
- **Clic en el medidor de RAM: la libera** (clic derecho › "Liberar RAM al hacer clic" para volver a
  abrir el Administrador de tareas, y "Liberar RAM ahora"). El botón aparte "Liberar RAM" ya no está:
  donde estaba queda el medidor de RAM.
- **Abrir ubicación del archivo** en todas las apps de Inicio (las de la Tienda abren su carpeta o la
  de Aplicaciones de Windows).

### Cambiado
- **Transparente total**: los paneles, el menú Inicio y el Hub son de vidrio de verdad (casi sin tinte:
  se ve el fondo); antes se veían casi como Mica.
- **Avisos y avisos en pantalla (volumen, brillo) de vidrio** con los temas Translúcido y Transparente
  total; con varios avisos, van juntos en un solo panel.
- **Inicio: colocación libre de verdad.** Soltar una app al costado de otra las intercambia; nada más
  se mueve. La celda donde caerá se marca.
- **Las herramientas de Windows** (Servicios, Desfragmentar…) ya no salen en "Todas las apps": solo al
  buscarlas.
- **Los cuadros de diálogo** (confirmar una desinstalación, abrir un archivo…) salen en el monitor donde
  se hizo la acción.
- **Arrastrar elementos en la barra** se siente firme: lo que se arrastra va pegado al puntero, los
  demás se deslizan en vez de saltar y el elemento ya no va y viene entre dos lugares.

### Corregido
- Inicio no se enteraba de lo instalado o desinstalado (sobre todo apps de la Tienda e instaladores que
  no ponen accesos directos): ahora vigila la lista de programas instalados y vuelve a leer el catálogo
  al abrirse.

## [3.4.0] - 2026-10-17

### Añadido
- **Prueba de velocidad desde el widget** (clic derecho › Probar velocidad): el widget muestra lo que va
  midiendo y, al terminar, el resultado. El medidor de la barra dice en su tooltip que el clic abre la prueba.

## [3.3.0] - 2026-10-17

### Añadido
- **Prueba de velocidad**: clic en el medidor de velocidad de internet de la barra abre un panel con
  lo que pasa ahora y una prueba completa (bajada, subida, ping y variación) con los servidores de
  Cloudflare. Sigue aunque se cierre el panel y avisa al terminar; el último resultado se guarda.

## [3.2.0] - 2026-10-17

### Añadido
- **Velocidad de internet** en la barra (bajada y subida en vivo) y como widget del escritorio. Cuenta
  el adaptador que sale a internet, sin sumar dos veces el tráfico de VPN como Tailscale o ZeroTier.
- **Notificaciones con emojis a color, links y stickers**: los emojis se ven como en Windows, los links
  se abren con un clic y las imágenes del aviso (stickers, fotos) aparecen debajo del texto. También
  llegan las fotos de contacto que las apps mandan por internet (WhatsApp).

### Corregido
- **Clic en una notificación de WhatsApp Web** (o de otro sitio) abría una pestaña nueva del
  navegador. Ahora abre su app web si está instalada; si no, va a la pestaña que la envió, como en
  Windows.

## [3.1.0] - 2026-10-17

### Añadido
- **Inicio y las apps abiertas se colocan como cualquier elemento**: a la izquierda, al centro o a
  la derecha de la barra (arriba, centro o abajo en las verticales), juntos o por separado,
  arrastrándolos en el modo edición o en la barra. La alineación es eso: el clic derecho de la
  barra en el modo edición trae atajos (juntos a la izquierda, al centro o a la derecha, o Inicio a
  la izquierda y las apps al centro). El menú Inicio se abre del lado de su botón. Tu alineación de
  antes se conserva.
- **Clic derecho en el menú Inicio › Ver**: tamaño de los íconos con un deslizador (de 20 a 96 px,
  se ve al moverlo) y los nombres de las apps ancladas. Sale del Hub.
- **Nueva carpeta** en el clic derecho de los anclados: una carpeta vacía a la que se arrastran apps
  (además de soltar una app sobre otra).
- **Ícono de Strata UI en la bandeja**: clic para abrir el Hub; clic derecho para editar el diseño,
  reiniciar o salir.
- **Abrir el Hub al iniciar con Windows** (Hub › General), apagado por defecto.

### Cambiado
- Arrastrar apps ancladas en Inicio es fluido: el ícono va pegado al puntero (antes era una ventana
  aparte que se quedaba atrás) y las pistas de mover o crear carpeta se animan.

### Corregido
- **Desinstalar una app web** (Brave, Chrome, Edge…) desde Inicio abría el desinstalador del
  navegador entero. Ahora quita solo esa app web, y el menú se actualiza al terminar.
- El Hub se abría solo al iniciar Windows: el inicio automático lanzaba Strata UI dos veces (la tarea
  y la clave Run del instalador) y la segunda le pedía el Hub a la primera.

## [3.0.0] - 2026-10-16

Todo lo que se ve en pantalla se arma en el **modo edición**; el Hub queda para lo general. Incluye
la 2.6.0, que no llegó a publicarse.

### Añadido
- **Opciones de las barras en el modo edición** (clic derecho en una barra): tamaño, estilo (el del
  tema, transparente total o un color propio, con su editor de color ahí mismo), alineación de
  Inicio y las apps, y centrar las apps. Se ven al instante en la vista previa.
- **Opciones de cada elemento en el modo edición** (clic derecho en él): segundos y fecha del reloj,
  porcentaje de la batería, temperaturas de CPU y GPU, controles multimedia y qué monitor mide el
  contador de FPS.
- **Dónde salen los avisos, arrastrando**: el modo edición muestra un aviso de ejemplo en su monitor
  y su esquina; se arrastra a otra esquina o a otro monitor.

### Cambiado
- **Temas: estilo y esquema por separado.** Los estilos son Mica (antes Sistema), Translúcido (antes
  Transparencia), Material y Transparente total; **Claro** y **Oscuro** pasan a ser esquemas, junto
  a "Según Windows" (el de siempre) y los de color. El esquema se mantiene al cambiar de estilo.
  Quien tenía Claro, Oscuro o Liquid Glass lo conserva como estilo + esquema.
- **Liquid Glass ya no está** (y con él la refracción en vivo, que capturaba la pantalla sin parar):
  pasa a Translúcido con su claro u oscuro.
- El Hub ya no repite lo que hace el modo edición: Barra de tareas muestra un resumen del diseño de
  cada monitor y deja las apps abiertas y las lecturas avanzadas; Bandeja, solo qué íconos se ven;
  Multimedia, solo sus controles; Notificaciones, dónde salen los avisos con un acceso al modo
  edición; Widgets, el acceso al modo edición. El monitor principal se elige en el modo edición.

## [2.6.0] - 2026-10-16

### Añadido
- **Modo edición**: el diseño de cada monitor se arma sobre la pantalla misma. Una capa tapa
  todo con el fondo de pantalla y muestra las barras y los widgets como se van a ver; los
  elementos se arrastran entre barras, bordes y monitores, se agregan desde la biblioteca (también
  desde el Hub, que queda encima) o con clic derecho, y los widgets se mueven, agregan, cambian de
  tamaño o se quitan ahí mismo. Nada cambia hasta **Aplicar**; Esc o **Cancelar** lo descartan. Se
  abre con clic derecho en la barra › *Editar diseño…*, desde Hub › Barra de tareas o con
  `StrataUI.exe --edit`.
- **Hasta cuatro barras por monitor**, una en cada borde: las de arriba y abajo ocupan todo el
  ancho y las de los lados van entre ellas. Un monitor también puede quedar sin barra.
- **Las apps abiertas son un elemento** como los demás: una barra puede llevar solo el área de
  estado, solo Inicio o solo las apps.
- **Un menú Inicio, una bandeja y un área de estado por monitor**: cada monitor lleva cada
  elemento una vez y la bandeja puede ir en varios (es la misma en todos).

### Cambiado
- Hub › Barra de tareas: en lugar de posición y segunda barra, qué bordes tienen barra y qué lleva
  cada una (activar un elemento en una barra lo trae de la otra del mismo monitor).
- El diseño de antes (posición, segunda barra, monitores sin barra y los elementos y zonas de cada
  barra) pasa solo al nuevo y se ve igual, también el de los monitores desconectados.
- Las lecturas avanzadas leen las temperaturas cada 2 s (los FPS siguen cada segundo) y, si se
  iniciaron solas por un medidor, se detienen cuando ninguna barra ni widget muestra CPU, GPU o FPS.

### Corregido
- Una barra cerrada (al recrearse) seguía atendiendo los cambios de los íconos ocultos de la bandeja.

## [2.5.0] - 2026-10-16

### Añadido
- **Exportar e importar la configuración** (Hub › General): un .zip con tus ajustes y los módulos
  importados, para respaldarlos o llevarlos a otro equipo. Al importar se conserva lo propio de
  este equipo (monitores y los valores de Windows que Strata UI devuelve al cerrarse).
- **Dashboard** en lugar de Resumen: uso de CPU, RAM y GPU en vivo, No molestar, sonido e inicio
  con Windows a un clic, Liberar RAM, Reiniciar y Salir, y accesos a cada sección.
- **Solo el sonido de Strata UI** (Hub › Notificaciones, activado por defecto): mientras Strata UI
  está abierto, apaga el sonido que Windows pone a las notificaciones de cada app (WhatsApp,
  navegadores…), así suena solo el tono de Strata UI. Al cerrarlo se devuelve el de cada app. El
  sonido que tocan las páginas web (el de WhatsApp Web, por ejemplo) se quita en cada sitio.
- **Estilo de la barra por monitor** (Barra de tareas › Barra): cada barra puede ir con el tema de
  todos, totalmente transparente o con un color propio (con su opacidad). El color propio de la
  barra que había en Colores pasa a cada barra.
- **Sin UAC después de instalar** (instalación para todos los usuarios): el instalador deja listo el
  proceso con permiso de administrador de las lecturas avanzadas, y ese proceso aplica por Strata
  UI las optimizaciones, deshacerlas, la limpieza, el punto de restauración, quitar apps para todos
  los usuarios y los ajustes de audio y de NVIDIA. Solo una lista fija de acciones con sus valores
  comprobados (nunca comandos libres) y solo para el Strata UI de Archivos de programa; lo demás, o
  en una instalación por usuario, sigue pidiendo UAC.

### Cambiado
- **Hub reorganizado**: Barra de tareas junta la barra, la bandeja del sistema, multimedia y los
  paneles en pestañas; Tema y colores van juntos; Actualizaciones pasa a General. El grupo
  Optimización ahora es **Módulos**, con una entrada por módulo y sus páginas dentro. El selector de
  monitor ocupa una línea y solo aparece con más de un monitor.
- Menú Inicio y las demás páginas con módulos importables ya no tienen los botones de exportar,
  carpeta y ayuda; la carpeta y el formato de los módulos están en General › Módulos.
- **Módulos › Importar**: un solo lugar para importar temas, animaciones, widgets, menús de Inicio
  y **sonidos de notificación** (.wav, .mp3, .m4a, .wma; nuevo), con su lista para usarlos o
  quitarlos. Las demás páginas ya no tienen su propio bloque de importar.
- **Paneles** tiene su propia entrada en el Hub.
- **El tema es de todos los monitores**: al elegirlo se aplica en todos (antes solo en el elegido).
- **Ventanas apiladas que se ven en cualquier tema**: agrupar las ventanas de cada app viene
  activado, y las tarjetas de atrás llevan el color del texto de la barra (antes, el tono tenue del
  botón, que en temas oscuros o en la barra transparente casi no se veía); con tres ventanas o más
  asoman dos.
- **Esquemas de color combinables**: Nord, Dracula, Catppuccin… ya no reemplazan al estilo, lo
  tiñen. Se elige un estilo (Mica, Transparencia, Liquid Glass, Material, Transparente total…) y un
  esquema por separado; sobre vidrio y transparencia los colores quedan translúcidos para que se
  siga viendo el efecto. Quien usaba un esquema lo conserva sobre el estilo Sistema.
- **Animaciones en todo**: las vistas previas de las ventanas también se van con la animación
  elegida, y los menús, tooltips y la bandeja oculta la muestran con más recorrido (antes casi no se
  notaba). Los menús contextuales, la bandeja oculta y las carpetas del menú Inicio también se
  cierran con ella (WPF los cierra de golpe: sale una imagen de cómo se veían). Abrir, cerrar y minimizar ventanas y sus menús los anima Windows: con "Sin animación" se
  apagan y con cualquier otra se encienden; al cerrar Strata UI vuelven como estaban.
- **Transparente total ahora es un tema** (antes un interruptor): solo la barra va sin fondo; los
  paneles, el menú Inicio y los menús llevan vidrio esmerilado para que se lean. Quien lo tenía
  activado pasa a este tema en ese monitor.
- Sin "Copiar a todos" en el selector de monitor: cada barra y sus widgets siguen con sus propios
  ajustes.
- **Widgets de CPU, GPU y FPS sin el arco**: solo el número grande, el nombre y la temperatura (o
  el juego).

### Seguridad
- El proceso elevado de las lecturas avanzadas solo atiende al Strata UI de su misma carpeta (antes
  bastaba con que el proceso se llamara StrataUI). Al deshacer optimizaciones con permiso de
  administrador solo se restauran claves del catálogo de ajustes: el respaldo está en una carpeta del
  usuario y otro programa podría haberle agregado otras.
- Quitar apps para todos los usuarios ya no lanza un script de PowerShell elevado: lo hace Strata
  UI, que solo acepta nombres de paquete.

### Corregido
- **El esquema de color solo se veía en el Hub**: cada monitor seguía con el estilo que tenía de
  cuando el tema era por monitor (por ejemplo, Transparente total en uno), y sobre los materiales
  translúcidos el esquema quedaba tan aclarado que no se reconocía. El tema ahora es uno solo para
  todos los monitores y el esquema se nota sobre cualquier estilo.
- **No sonaba el sonido de las notificaciones** (ni la vista previa en Importar): MediaPlayer de
  WPF dejaba de sonar dentro de Strata UI sin avisar. Ahora suena con WASAPI y cada reproducción, o
  su error, queda en el registro.
- **Clic en una app abierta para minimizarla**: a veces la traía al frente, porque al soltar el
  clic ya no figuraba como activa; ahora cuenta cómo estaba al presionar.
- **Con "Desvanecer", los paneles y el menú Inicio parecían aparecer de golpe**: su contenido se
  desvanecía, pero el vidrio de Windows detrás no puede, y aparecía de una vez. Ahora además asoman
  desde la barra con un recorrido corto. Las vistas previas de las ventanas se desvanecen enteras.
- **Equipo trabado con las lecturas avanzadas**: en algunas laptops leer una pieza del hardware
  (la placa, el controlador del equipo) tarda segundos y frena a todo el sistema (menús que
  tardaban 8 s en aparecer). Una pieza que tarda más de 1,5 s dos veces deja de leerse. El proceso
  elevado ahora se cierra a los 3 s de cerrar Strata UI aunque esté trabado, y el instalador lo
  termina si quedó vivo (antes la actualización fallaba con "archivo en uso").
- **Barras duplicadas al conectar un monitor**: cada monitor quedaba con dos barras, una encima
  de la otra. Guardar los monitores nuevos avisaba de un cambio de ajustes que, mientras se abrían
  las barras, abría otro juego completo.
- El registro opcional del menú contextual tiene un límite de 60 segundos y se ejecuta sin
  preguntas interactivas, para que Windows AppX no deje el instalador detenido al final.
  Los errores quedan en `logs/installer-integration.log`; también se limita la actualización
  de la tarea de inicio automático, sin preguntas de contraseña de `schtasks`.

## [2.4.2] - 2026-10-16

### Cambiado
- **Sonido de notificación propio**: un solo tono corto y redondo (Do5, como de vibráfono), en vez
  del de Windows, que es muy suave.

### Corregido
- **El sonido de las notificaciones no se oía** en laptops: la tarjeta de sonido se duerme para
  ahorrar energía y lo primero que le llega se pierde (en un sonido corto, todo). Ahora va precedido
  de un ruido inaudible que la despierta, y se reproduce con MediaPlayer (PlaySound devolvía éxito
  sin que se oyera nada).

## [2.4.1] - 2026-10-16

### Añadido
- **Sonido en las notificaciones**: cada aviso emergente suena con el sonido de notificación de
  Windows (el del esquema de sonidos, con su volumen). Se apaga en Hub › Notificaciones.

## [2.4.0] - 2026-10-16

### Añadido
- **Widgets de CPU, GPU y FPS**: medidores grandes para el escritorio, un arco de 270° del color de
  acento con brillo que se mueve con suavidad, el número en el centro y la temperatura (o el juego
  que se mide, en el de FPS; su arco llega al máximo con la frecuencia del monitor).
- **Campana de notificaciones en la barra** (con el número de pendientes): abre el centro solo con
  las notificaciones y "No molestar". Las barras que ya tenían reloj la reciben a su lado.
- **Punto naranja en el botón de energía de Inicio** cuando hay actualizaciones esperando un
  reinicio (y entonces su menú tiene "Actualizar y apagar" y "Actualizar y reiniciar").

### Cambiado
- **El reloj abre el calendario**, en un panel propio con el estilo de los demás (antes traía el
  calendario con el aspecto de Windows 7, debajo de las notificaciones). Las flechas o la rueda
  cambian de mes; clic en el mes vuelve a hoy.

### Corregido
- **El panel de notificaciones se salía de la pantalla** con monitores a distinta escala (100 % y
  125 %): su alto máximo se calculaba con la escala del monitor donde se creaba la ventana y no con
  la del monitor donde se abre.

## [2.3.2] - 2026-10-16

### Corregido
- Luz nocturna en un equipo donde nunca se usó: además del estado se crean sus ajustes (intensidad y
  horario, con los de fábrica), que Windows necesita para aplicarla; y si el valor guardado tiene un
  formato desconocido (otra versión de Windows), se reemplaza por uno válido en vez de no hacer nada.

## [2.3.1] - 2026-10-16

### Corregido
- **El panel de brillo no aparecía en PCs de escritorio** (monitores sin DDC/CI o mientras se
  detectaban): el botón se ocultaba sin monitores con brillo. Ahora siempre está, para la luz
  nocturna, y el panel avisa si no se puede cambiar el brillo.
- La luz nocturna no se podía usar en un equipo donde nunca se había activado (Windows todavía no
  había creado su valor): ahora se crea al encenderla desde el panel.

## [2.3.0] - 2026-10-16

### Añadido
- **Luz nocturna en el panel de brillo**: un interruptor para la luz nocturna de Windows (colores
  más cálidos para descansar la vista) y un enlace a su intensidad y horario en Configuración.
  Varios clics seguidos son un solo cambio para Windows.
- **Lo que se arrastra en la barra se ve en la mano**: una imagen del elemento, la ventana o el
  anclado con un marco del color de acento sigue al puntero, también de una barra o un monitor a
  otro; en su lugar queda tenue.
- **Contraseña de Wi-Fi como en el teléfono**: el último carácter escrito se ve un momento y un ojo
  muestra la contraseña entera para revisarla.
- **Anclar a la barra arrastrando desde Inicio**: una app de los anclados, de la lista de apps o de
  la búsqueda, soltada sobre la barra, queda anclada ahí.
- **El menú Inicio encuentra más de lo instalado**: además del catálogo de Windows (tienda e
  instaladores), los juegos de Steam, Epic y GOG aunque no tengan acceso directo en Inicio, y los
  accesos directos del escritorio a programas o juegos. Se actualiza al instalar o quitar un juego.
- **"Actualizar y apagar" y "Actualizar y reiniciar"** en el menú de energía de Inicio y en Win+X,
  como en Windows: aparecen cuando hay actualizaciones esperando un reinicio y las instalan.
- La flecha de los íconos ocultos de la bandeja gira: abierta apunta hacia donde se abre su panel y
  cerrada, al lado contrario (en los cuatro bordes).

### Cambiado
- **Menú Inicio más rápido y fluido**: se prepara una vez al iniciar (la primera apertura tardaba
  casi un segundo) y toma su tamaño guardado antes de mostrarse (se medía dos veces y daba un salto
  mientras entraba).
- Limpieza: se quitaron directivas `using` sin uso y las carpetas de compilación que quedaban de
  cuando se llamaba WINX.

### Corregido
- **Los ajustes de cada monitor se mezclaban al reiniciar o al conectar y quitar monitores**
  (posición y elementos de la barra, tema): se guardaban con el nombre "\\.\DISPLAYn" que Windows
  reparte cada vez, y la pantalla de la laptop sola recibe el mismo nombre que el monitor externo
  principal. Ahora cada monitor se sigue por su identificador de hardware: sus ajustes pasan a su
  nombre nuevo y, mientras no está conectado, se guardan hasta que vuelva.
- **Los anclados de la barra salían sin ícono después de iniciar Windows**: el ícono de la app llega
  después y el botón no se enteraba.
- **Wi-Fi se quedaba en "Conectando…" con la conexión ya hecha** (Windows a veces tarda o no
  responde): ahora se comprueba si el adaptador ya está en esa red, con un límite de tiempo, y los
  errores ya no dejan el panel colgado.
- Si la tarea de inicio automático la creó un Strata UI elevado y apunta a un ejecutable que ya no
  existe, se usa la clave Run (antes dejaba de iniciar con Windows); el instalador la apunta a la
  carpeta nueva.
- Cada menú Inicio "Perfil" abierto quedaba en memoria.
- Si un panel se cerraba mientras entraba, las animaciones de entrada y salida se peleaban la posición.
- Con los estilos Acercar y Rebote (y las animaciones importadas), los paneles y el menú Inicio
  casi no se animaban: el crecimiento y el fundido terminaban mientras se dibujaba el primer cuadro.

## [2.2.0] - 2026-10-16

### Cambiado
- **Paneles y menú Inicio se animan enteros**: la ventana completa (con su fondo) sale de detrás de
  la barra y vuelve a ella al cerrarse, como en Windows. Antes solo se movía el contenido dentro de
  un fondo quieto, y casi todo el recorrido pasaba mientras el panel todavía era transparente (el
  primer cuadro de una ventana nueva tarda; ahora la animación avanza con los cuadros que se ven).
- La foto del menú Inicio "Perfil" entra y sale junto con el menú (aparecía de golpe al final).
- **Botones de la barra al abrir y cerrar una app**: entran con la animación y la velocidad elegidas
  (antes era fija y, con las ventanas agrupadas, casi nunca se veía) y al cerrarse la app el botón
  sale con la misma animación en vez de desaparecer.
- Los avisos llegan desde el borde de la pantalla con un recorrido largo, sin el parpadeo del primer
  cuadro (se dibujaban enteros un instante antes de empezar a aparecer).

## [2.1.0] - 2026-10-15

### Cambiado
- **Optimización es un módulo aparte** (`StrataUI.Optimization.dll`): ya no viene en el instalador.
  Sin él, el grupo Optimización del Hub muestra una sola entrada con el botón **Instalar**, que lo
  descarga de la publicación de GitHub de la misma versión (para el usuario, sin administrador) y
  agrega sus páginas al momento. Se quita en Hub › General; al actualizar Strata UI, el módulo
  instalado se actualiza solo. El motor (y el resto de funciones que lo usan) sigue en la app, y
  los procesos elevados nunca cargan el módulo.
- El instalador pasa una instalación de la carpeta `WINX` (de cuando se llamaba así) a `StrataUI`
  y borra la vieja.

### Corregido
- Después de una actualización silenciosa desde el Hub en una instalación para todos los usuarios,
  Strata UI se volvía a abrir como administrador (sin íconos de bandeja de las apps normales, y lo
  que se abría desde él salía elevado).
- Lecturas avanzadas: si Strata UI cambia de carpeta, la tarea programada se renueva (antes seguía
  apuntando al ejecutable viejo).

## [2.0.0] - 2026-10-15

### Añadido
- **"Anclar a Inicio de Strata UI" en el menú contextual moderno de Windows 11** (no solo en
  "Mostrar más opciones"): un paquete MSIX sparse firmado con un certificado propio de Strata UI y
  un servidor COM pequeño (`StrataUI.ContextMenu.exe`). El instalador lo agrega con una tarea
  (confía en el certificado, lo que puede pedir permiso de administrador) y lo quita al
  desinstalar. Con el paquete, el mismo comando sale también en el menú clásico y en Nilesoft
  Shell; sin él, sigue el verbo clásico de antes.
- **Módulos importables**: temas, animaciones, widgets y aspectos del menú Inicio en archivos JSON
  declarativos (no pueden ejecutar nada), cada uno con su botón **Importar…** en su sección del Hub,
  su carpeta y "Quitar". Los temas importados aparecen en Tema › Importados (por monitor, con sus
  propios colores si los traen); las animaciones, en Animaciones › Estilo (desde dónde llegan,
  cuánto crecen, rebote); los widgets son líneas de texto con la hora, la fecha, la canción o la
  batería; los aspectos del menú Inicio cambian estilo, íconos, secciones, botones del pie, ícono y
  tamaño, y el menú actual se puede **exportar** para compartirlo. Formato y ejemplos en
  `docs/modules`.
- **Portugués, francés y alemán**, además de inglés y español (Hub › General › Idioma). Se pueden
  agregar idiomas propios con un `xx.json` en `%LOCALAPPDATA%\StrataUI\Languages` (aparecen solos en
  la lista; lo que les falte se ve en inglés) y corregir uno incluido poniendo ahí solo los textos
  que cambian.
- **Más fluidez**: el resaltado de los botones de la barra, los paneles y el menú Inicio entra y sale
  con un fundido, al presionar el contenido se encoge un poco (como en Windows 11), los botones de
  las apps que se abren entran con una animación y los widgets aparecen con un fundido.
- **Widgets de escritorio** (Hub › Widgets): reloj digital con o sin fecha y **visualizador de
  música** (barras con el acento que siguen lo que suena, con la canción debajo). Viven sobre el
  fondo, siguen a la vista con "Mostrar escritorio", se mueven arrastrándolos y su clic derecho da
  tamaño, fecha, bloquear posición y quitar. El visualizador solo se redibuja mientras hay sonido.
- **Esquemas de color**: Medianoche (negro OLED), Nord, Dracula, Catppuccin Mocha y Latte, Tokyo
  Night, Rosé Pine y Gruvbox, con sus colores y su acento (si se deja el de Windows).
- **Hub › Colores** (nueva página, separada de Tema): el acento, **colores propios para la barra y
  para los paneles y el menú Inicio** (muestras, código y opacidad; el texto se pone claro u oscuro
  solo para que se lea) y el fondo de pantalla. La página Apariencia pasa a llamarse **Tema**.
- **"Anclar a Inicio de Strata UI" en el clic derecho del Explorador** (archivos, accesos directos y
  carpetas): en el menú clásico ("Mostrar más opciones" en Windows 11) y en Nilesoft Shell. Se puede
  quitar en Hub › Menú Inicio.
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
- **El contador de FPS saltaba mucho** (en un juego quieto a 245 FPS mostraba de 23 a 199): contaba
  los eventos que llegaban cada segundo, y Windows los entrega en tandas. Ahora usa la hora de cada
  cuadro y muestra el promedio del último segundo (probado con Space Marine 2: 242–247).
- La generación de cuadros de DLSS salía como "tipo desconocido" en juegos que no dejan ver sus
  bibliotecas (anti-trampas): ahora se reconoce por las marcas de Reflex.
- **El menú de clic derecho de algunos íconos de la bandeja (WhatsApp) salía en otro monitor**
  cuando la bandeja está en un monitor a la izquierda o arriba del principal: esas apps leen mal las
  coordenadas negativas. Ahora Strata UI lleva el menú junto al ícono.
- La posición de los íconos de la bandeja que se da a las apps (para ubicar sus menús y avisos)
  llegaba como un rectángulo al revés.

### Cambiado
- Desanclar una app de Inicio deja su celda vacía en lugar de correr las demás (como al moverla).
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
