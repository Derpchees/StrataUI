using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Enums;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using Microsoft.Win32;
using Strata.Shell.Services;

namespace Strata.Shell;

public partial class App : Application
{
    private const string MutexName = @"Local\StrataUI.Shell";
    private const string ExitEventName = @"Local\StrataUI.Shell.Exit";
    private const string OpenHubEventName = @"Local\StrataUI.Shell.OpenHub";
    private const string RestoreArg = "--restore-taskbar";
    private const string ExitArg = "--exit";
    private const string EditArg = "--edit";
    /// <summary>Pedido de otra instancia (en hub-request.txt): abrir el modo edición en lugar del Hub.</summary>
    private const string EditRequest = "::edit";
    /// <summary>Pedido de otra instancia: abrir el explorador de archivos (seguido de la ruta, si hay).</summary>
    private const string FilesRequest = "::files ";
    private const string FilesArg = "--files";

    private static readonly string HubRequestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "hub-request.txt");

    private Mutex? _mutex;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private EventWaitHandle? _openHubEvent;
    private bool _explorerMenu;
    private RegisteredWaitHandle? _openHubWait;
    private ShellManager? _shell;
    private readonly List<Taskbar> _taskbars = new();
    private Hub.HubWindow? _hub;
    private int _screenCount;
    private bool _exiting;

    /// <summary>Servicios de ManagedShell (bandeja, ventanas); disponibles mientras el shell corre.</summary>
    public ShellManager Shell => _shell ?? throw new InvalidOperationException("El shell no está iniciado");

    private Services.Audio.AudioService? _audio;

    /// <summary>Audio del sistema (se crea al primer uso, en el hilo de la UI).</summary>
    public Services.Audio.AudioService Audio => _audio ??= new Services.Audio.AudioService();

    public static string Version =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Antes de tocar la configuración o el registro: los datos de cuando se llamaba WINX.
        LegacyMigration.Run();

        // Herramienta de emergencia: devuelve la barra de Windows si Strata UI murió sin limpiar.
        if (e.Args.Contains(RestoreArg, StringComparer.OrdinalIgnoreCase))
        {
            NativeTaskbar.Restore();
            Services.Notifications.WindowsBanners.Restore();
            Services.Themes.WindowsAnimations.Restore();
            Shutdown();
            return;
        }

        // Tareas de administrador lanzadas por la instancia principal vía UAC (ver Tweaks/Elevation).
        if (e.Args.Length > 0 && e.Args[0] is "--apply-tweak" or "--restore-tweaks" or "--restore-point")
        {
            Shutdown(Tweaks.TweakEngine.RunElevatedCommand(e.Args));
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == Tweaks.EdgeRemover.Arg)
        {
            Shutdown(Tweaks.EdgeRemover.RunElevated());
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == Tweaks.EdgeRemover.RestoreArg)
        {
            Shutdown(Tweaks.EdgeRemover.RunElevatedRestore());
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == Tweaks.ElevatedBroker.RemoveAppxArg)
        {
            Shutdown(Tweaks.AppxManager.RunElevatedRemove(e.Args));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == Services.Audio.AudioDeviceOptions.ElevatedArg)
        {
            Shutdown(Services.Audio.AudioDeviceOptions.RunElevated(e.Args));
            return;
        }
        // Lecturas avanzadas (temperaturas, FPS) y liberar RAM: ver Services/Sensors.
        if (e.Args.Length > 0 && e.Args[0] == SensorsTask.TaskArg)
        {
            Shutdown(Sensors.RunHelper(e.Args));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == SensorsTask.InstallArg)
        {
            Shutdown(SensorsTask.RunInstall(e.Args));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == SensorsTask.RemoveArg)
        {
            Shutdown(SensorsTask.RunRemove(e.Args));
            return;
        }
        // Limpieza de las carpetas de Windows, con permiso de administrador (ver Services/Cleanup).
        if (e.Args.Length > 0 && e.Args[0] == Services.Cleanup.JunkCleaner.ElevatedArg)
        {
            Shutdown(Services.Cleanup.JunkCleaner.RunElevated(e.Args));
            return;
        }
        // Ajustes del controlador de NVIDIA, en un proceso aparte (ver Services/Vendors).
        if (e.Args.Length > 0 && e.Args[0] == Services.Vendors.NvidiaSettings.Arg)
        {
            Shutdown(Services.Vendors.NvidiaSettings.RunHelper(e.Args));
            return;
        }
        // "Anclar a Inicio de Strata UI" desde el menú del Explorador (ver Services/StartMenu/ExplorerMenu).
        if (e.Args.Length > 0 && e.Args[0] == Services.StartMenu.ExplorerMenu.PinArg)
        {
            Shutdown(Services.StartMenu.ExplorerMenu.RunRequest(e.Args));
            return;
        }
        // Diálogo de abrir archivo o carpeta en un proceso aparte (ver Services/FilePicker).
        if (e.Args.Length > 0 && e.Args[0] == FilePicker.Arg)
        {
            Shutdown(FilePicker.RunPicker(e.Args));
            return;
        }

        // Pide a la instancia en ejecución que se cierre limpiamente (restaura la barra de Windows).
        if (e.Args.Contains(ExitArg, StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var running))
            {
                running.Set();
                running.Dispose();
                // Espera a que termine de cerrarse: el instalador y el desinstalador usan --exit
                // y no deben tocar los archivos mientras la instancia aún los usa.
                foreach (var other in Process.GetProcessesByName("StrataUI").Where(p => p.Id != Environment.ProcessId))
                {
                    using (other)
                        other.WaitForExit(15000);
                }
            }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        bool autostart = e.Args.Contains(AutoStart.Arg, StringComparer.OrdinalIgnoreCase);
        if (!isFirstInstance)
        {
            // Abrir Strata UI otra vez mientras ya corre muestra el Hub de la instancia existente,
            // salvo si es el inicio de sesión (la tarea y la clave Run pueden lanzarlo dos veces).
            if (!autostart && EventWaitHandle.TryOpenExisting(OpenHubEventName, out var openHub))
            {
                // "--hub Privacy" abre directamente esa página del Hub; "--edit", el modo edición.
                int hubArg = Array.FindIndex(e.Args, a => a.Equals("--hub", StringComparison.OrdinalIgnoreCase));
                int filesArg = Array.FindIndex(e.Args, a => a.Equals(FilesArg, StringComparison.OrdinalIgnoreCase));
                string? request = e.Args.Contains(EditArg, StringComparer.OrdinalIgnoreCase) ? EditRequest
                    : filesArg >= 0 ? FilesRequest + (filesArg + 1 < e.Args.Length ? e.Args[filesArg + 1] : "")
                    : hubArg >= 0 && hubArg + 1 < e.Args.Length ? e.Args[hubArg + 1] : null;
                if (request != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(HubRequestPath)!);
                    File.WriteAllText(HubRequestPath, request);
                }
                // Este proceso lo lanzó el usuario, así que puede ceder el permiso de primer plano.
                AllowSetForegroundWindow(AsfwAny);
                openHub.Set();
                openHub.Dispose();
            }
            Shutdown();
            return;
        }

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent,
            (_, _) => Dispatcher.BeginInvoke(() => ExitShell()), null, Timeout.Infinite, true);
        _openHubEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenHubEventName);
        _openHubWait = ThreadPool.RegisterWaitForSingleObject(_openHubEvent,
            (_, _) => Dispatcher.BeginInvoke(OnOpenHubRequested), null, Timeout.Infinite, false);
        _startedAt = DateTime.UtcNow;
        // La tarea creada por un Strata UI elevado no se puede actualizar y abre sin el argumento: un
        // arranque en los primeros minutos de Windows también cuenta como inicio de sesión.
        autostart |= Environment.TickCount64 < 5 * 60_000;

        Services.Localization.Loc.Initialize(Settings.Current.Language);
        Log.Info($"Strata UI {Version} iniciando (idioma: {Services.Localization.Loc.Language})");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;

        // WPF asigna como MainWindow la primera ventana creada, y WPF-UI modifica esa ventana al
        // cambiar de tema (le quita los efectos de DWM). Una ventana invisible ocupa ese lugar
        // para que la barra, el Hub y los paneles conserven su material de fondo.
        MainWindow = new Window { ShowInTaskbar = false, ShowActivated = false };

        ThemeManager.Initialize();
        DialogPlacement.Watch();
        Services.Themes.WindowsTheming.Initialize();
        Services.Themes.Motion.Initialize();

        // Diagnóstico: con STRATAUI_SHELL_LOG=ruta, ManagedShell escribe su registro detallado (bandeja, tareas).
        if (Environment.GetEnvironmentVariable("STRATAUI_SHELL_LOG") is { Length: > 0 } shellLog)
        {
            var file = new ManagedShell.Common.Logging.Observers.FileLog(shellLog);
            file.Open();
            ManagedShell.Common.Logging.ShellLogger.Severity = ManagedShell.Common.Logging.LogSeverity.Debug;
            ManagedShell.Common.Logging.ShellLogger.Attach(file, false);
        }

        var config = ShellManager.DefaultShellConfig;
        config.TaskIconSize = IconSize.Large;
        config.MultiMonAwareTasksService = true;
        if (Settings.Current.PinnedTrayIcons is { } pinned)
            config.PinnedNotifyIcons = pinned;
        _shell = new ShellManager(config);

        TrayPromotion.Attach(_shell.NotificationArea);
        TrayMemory.Attach(_shell.NotificationArea, TrayOwnership.OwnTrayWindow());
        TrayOwnership.Start();
        StrataTrayIcon.Show();
        SystemStatus.Initialize();
        Services.StartMenu.StartMenuService.Initialize();
        Services.StartMenu.WinKeyHook.WinPressed += OnWinKey;
        Services.StartMenu.WinKeyHook.WinXPressed += () => AppsTaskbar?.ShowWinXMenu();
        Services.StartMenu.WinKeyHook.WinNPressed += () => StatusTaskbar?.ToggleNotificationCenter();
        Services.StartMenu.WinKeyHook.WinEPressed += () => OpenFiles();
        UpdateKeyHook();
        OpenTaskbars();
        _shell.ExplorerHelper.HideExplorerTaskbar = true;
        ExplorerWatch.Restarted += OnExplorerRestarted;
        ExplorerWatch.Start();
        _ = Services.Brightness.BrightnessService.RefreshAsync();
        Services.Media.MediaService.Initialize();
        _audioMirror = Settings.Current.AudioMirrorDevice;
        if (_audioMirror != null)
            StartAudioMirror();
        Services.Notifications.NotificationService.Arrived += OnNotificationArrived;
        // La prueba de velocidad sigue con el panel cerrado: entonces avisa al terminar.
        SpeedTest.Finished += result =>
        {
            if (!Flyouts.NetSpeedFlyout.IsShowing)
                Services.Notifications.NotificationService.ShowLocal(Services.Localization.Loc.T("Prueba de velocidad terminada"), SpeedTest.Summary(result));
        };
        Services.Notifications.NotificationService.Update();
        Services.Notifications.WindowsBanners.Update();
        Services.Themes.WindowsAnimations.Update();
        Notifications.Osd.Initialize();
        Services.Updates.UpdateService.UpdateSchedule();
        OptimizationModule.StartupMaintenance();
        if (Settings.Current.SensorsAtStartup && Settings.Current.Layouts.Values.SelectMany(l => l.Bars).Any(bar =>
                bar.Items.Contains(BarItem.Fps) || bar.Items.Contains(BarItem.Cpu) || bar.Items.Contains(BarItem.Gpu)))
            _ = Sensors.StartAsync(automatic: true);
        _ = Task.Run(AutoStart.Migrate);
        _ = Task.Run(Tweaks.GameBarRemover.RepairIfReinstalled);
        // El menú Inicio, armado una vez cuando ya cargó el catálogo de apps: así la primera apertura es rápida.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            if (!_exiting)
                StartMenu.StartMenuLauncher.WarmUp();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle));
        Services.StartMenu.ExplorerMenu.Listen();
        Services.Widgets.WidgetManager.Initialize();
        _explorerMenu = Settings.Current.StartExplorerMenu;
        Services.StartMenu.ExplorerMenu.Update();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        Settings.Changed += OnSettingsChanged;

        int hubIndex = Array.FindIndex(e.Args, a => a.Equals("--hub", StringComparison.OrdinalIgnoreCase));
        if (autostart && Settings.Current.HubAtStartup && hubIndex < 0)
            OpenHub();
        if (hubIndex >= 0)
        {
            OpenHub();
            if (hubIndex + 1 < e.Args.Length)
                _hub?.NavigateTo(e.Args[hubIndex + 1]);
        }
        // Con las barras ya ubicadas (su tamaño define el escritorio que se dibuja).
        if (e.Args.Contains(EditArg, StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(EditMode.EditSession.Start, DispatcherPriority.ApplicationIdle);
        int filesIndex = Array.FindIndex(e.Args, a => a.Equals(FilesArg, StringComparison.OrdinalIgnoreCase));
        if (filesIndex >= 0)
            OpenFiles(filesIndex + 1 < e.Args.Length ? e.Args[filesIndex + 1] : null);

        // ManagedShell termina de inicializar los estados de las ventanas de forma asíncrona.
        var activeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        activeTimer.Tick += (_, _) =>
        {
            activeTimer.Stop();
            MarkForegroundWindowActive();
        };
        activeTimer.Start();

        // Lo que se cargó al arrancar (íconos, catálogo de apps, temas) ya no hace falta en memoria.
        MemoryTrim.Schedule(TimeSpan.FromSeconds(20));
    }

    /// <summary>ManagedShell solo marca la ventana activa cuando cambia el foco;
    /// al arrancar marcamos la que ya lo tenía.</summary>
    private void MarkForegroundWindowActive()
    {
        if (_shell is null)
            return;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        var window = _shell.Tasks.GroupedWindows.OfType<ApplicationWindow>()
            .FirstOrDefault(w => w.Handle == foreground);
        if (window != null)
        {
            window.State = ApplicationWindow.WindowState.Active;
            Log.Info($"Ventana activa al iniciar: {window.Title} → {window.State}");
        }
    }

    /// <summary>Monitores y nombres con que se armaron las barras (para notar si Windows los renombró).</summary>
    private string _monitors = "";

    // Abriendo las barras: guardar los monitores avisa de un cambio de ajustes y OnSettingsChanged, con
    // _barLayout aún del arreglo anterior, abriría otro juego de barras encima de este.
    private bool _openingTaskbars;

    private void OpenTaskbars()
    {
        _openingTaskbars = true;
        try
        {
            OpenTaskbarsCore();
        }
        finally
        {
            _openingTaskbars = false;
        }
    }

    private void OpenTaskbarsCore()
    {
        // Antes de leer nada por monitor: si Windows cambió los nombres, los ajustes siguen a su monitor.
        var present = MonitorKeys.Present();
        _monitors = string.Join(";", present.Select(p => p.Device + "=" + p.Id).Order());
        var s = Settings.Current;
        bool changed = MonitorKeys.Reconcile(s, present);
        if (changed)
            Log.Info("Monitores: " + _monitors);
        changed |= BarLayout.AddNotificationBell(s);

        var screens = AppBarScreen.FromAllScreens();
        _screenCount = screens.Count;
        bool multiMonitor = screens.Count > 1;
        string main = MainScreenDevice(screens.Select(sc => (sc.DeviceName, sc.Primary)));

        // Cada monitor conectado queda con su diseño escrito (lo de siempre si nunca se configuró):
        // así el modo edición, el Hub y los arrastres en la barra trabajan sobre la misma lista.
        foreach (var screen in screens.Where(sc => !s.Layouts.ContainsKey(sc.DeviceName)))
        {
            s.Layouts[screen.DeviceName] = BarLayout.DefaultLayout(screen.DeviceName == main, s);
            changed = true;
        }
        if (changed)
            Settings.Update(_ => { });
        _barLayout = BarLayoutKey();

        // El monitor principal primero y, en cada monitor, arriba y abajo antes que los lados: las
        // barras horizontales se quedan con las esquinas.
        var plan = new List<(AppBarScreen Screen, BarConfig Bar)>();
        foreach (var screen in screens.OrderByDescending(sc => sc.DeviceName == main))
        {
            var layout = s.Layouts[screen.DeviceName];
            BarLayout.Normalize(layout);
            foreach (var bar in BarLayout.InCreationOrder(layout))
                plan.Add((screen, bar));
        }

        // La bandeja es la misma en cada barra que la lleve; la primera (la del monitor principal, si
        // la tiene) es la que se informa a las apps que preguntan dónde está.
        bool hostAssigned = false;
        foreach (var (screen, bar) in plan)
        {
            bool tray = bar.Items.Contains(BarItem.Tray);
            var taskbar = new Taskbar(_shell!, screen, multiMonitor, bar.Edge, BarLayout.Id(screen.DeviceName, bar.Edge),
                                      isMainScreen: screen.DeviceName == main, hasTray: tray, isTrayHost: tray && !hostAssigned);
            hostAssigned |= tray;
            taskbar.Show();
            _taskbars.Add(taskbar);
        }
        _appsHost = _taskbars.FirstOrDefault(t => t.IsMainScreen && t.Has(BarItem.Apps))?.Device
                    ?? _taskbars.FirstOrDefault(t => t.Has(BarItem.Apps))?.Device;
        // Ya se sabe qué monitores quedaron sin apps abiertas: la barra principal muestra también sus ventanas.
        foreach (var taskbar in _taskbars)
            taskbar.RefreshTasks();
    }

    private string? _appsHost;

    /// <summary>
    /// Monitor cuyas barras muestran también las ventanas de los monitores sin apps abiertas: el
    /// principal de Strata UI o, si no las lleva, el primero que las tenga.
    /// </summary>
    public string? AppsHostDevice => _appsHost;

    /// <summary>Hay una barra de Strata UI con las apps abiertas en ese monitor.</summary>
    public bool HasTaskbarOn(IntPtr hMonitor) => _taskbars.Any(t => t.Screen.HMonitor == hMonitor && t.Has(BarItem.Apps));

    /// <summary>
    /// Monitor principal de Strata UI: el elegido en el Hub si sigue conectado; si no, el de Windows.
    /// </summary>
    internal static string MainScreenDevice(IEnumerable<(string Device, bool Primary)> screens)
    {
        var list = screens.ToList();
        string? chosen = Settings.Current.TaskbarMainScreen;
        if (chosen != null && list.Any(sc => sc.Device == chosen))
            return chosen;
        return list.FirstOrDefault(sc => sc.Primary).Device ?? list.FirstOrDefault().Device ?? "";
    }

    /// <summary>
    /// Lo que obliga a recrear las barras: qué barras tiene cada monitor (y cuáles llevan la bandeja
    /// o las apps) y el monitor principal.
    /// </summary>
    private static string BarLayoutKey()
    {
        var s = Settings.Current;
        return BarLayout.StructureKey(s)
               + "|" + s.TaskbarMainScreen
               // La barra transparente también se aplica recreándolas: cambiar en vivo de Acrylic a
               // transparente deja negra la parte transparente (DWM no suelta el material anterior).
               + "|" + string.Join(";", ScreenDevices().Select(d => d + "=" + ThemeManager.ClearBarFor(d)));
    }

    private static IEnumerable<string> ScreenDevices() =>
        System.Windows.Forms.Screen.AllScreens.Select(sc => sc.DeviceName).Order();

    /// <summary>Algún monitor muestra el aviso de volumen o silencio: el gancho de teclado atiende esas teclas.</summary>
    private static bool AnyVolumeOsd() =>
        ScreenDevices().Any(d => Settings.Current.For(d) is { OsdVolume: true } or { OsdMute: true });

    /// <summary>Barras abiertas (el Hub las muestra para elegir qué lleva cada una).</summary>
    public IReadOnlyList<Taskbar> Taskbars => _taskbars;

    private string _barLayout = "";

    private void CloseTaskbars()
    {
        foreach (var taskbar in _taskbars)
        {
            taskbar.AllowClose = true;
            taskbar.Close();
        }
        _taskbars.Clear();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // AppBarWindow ya reacciona a cambios de resolución/DPI; aquí solo
        // atendemos monitores que se conectan o desconectan.
        Dispatcher.BeginInvoke(() =>
        {
            // También si el número es el mismo pero Windows les cambió los nombres (o cambió un monitor).
            string monitors = string.Join(";", MonitorKeys.Present().Select(p => p.Device + "=" + p.Id).Order());
            if (_exiting || AppBarScreen.FromAllScreens().Count == _screenCount && monitors == _monitors)
                return;
            Log.Info("Cambiaron los monitores; recreando barras");
            EditMode.EditSession.Cancel(); // sus capas eran de los monitores de antes
            CloseTaskbars();
            OpenTaskbars();
            _ = Services.Brightness.BrightnessService.RefreshAsync();
        });
    }

    /// <summary>
    /// El Explorador se reinició (o arrancó después de Strata UI): no conoce las barras de Strata UI, así que
    /// no reserva su espacio y las ventanas maximizadas quedan debajo. Se vuelven a crear (eso las
    /// registra ante el nuevo Explorador) y se oculta otra vez su barra.
    /// </summary>
    private void OnExplorerRestarted()
    {
        if (_exiting || _shell is null)
            return;
        Log.Info("Barra del Explorador nueva: se registran otra vez las barras de Strata UI");
        _shell.ExplorerHelper.SetTaskbarState(NativeMethods.ABState.AutoHide);
        _shell.ExplorerHelper.SetTaskbarVisibility(SwpHideWindow);
        _shell.ExplorerHelper.SetSecondaryTaskbarVisibility(SwpHideWindow);
        Flyouts.FlyoutManager.CloseAll();
        EditMode.EditSession.Cancel();
        CloseTaskbars();
        OpenTaskbars();
    }

    private const int SwpHideWindow = 0x0080;

    /// <summary>
    /// Windows avisa cuando cambian sus preferencias (p. ej. la alineación de su barra): las barras
    /// que las siguen las vuelven a leer.
    /// </summary>
    private void OnWindowsPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_exiting)
                return;
            foreach (var taskbar in _taskbars)
                taskbar.RefreshFromWindows();
        });

    /// <summary>
    /// Aviso emergente de una notificación nueva, salvo con "No molestar", con los avisos
    /// desactivados o mientras una app ocupa la pantalla completa (juegos, videos, presentaciones).
    /// </summary>
    private void OnNotificationArrived(Services.Notifications.StrataNotification item)
    {
        if (!item.IsLocal)
            Services.Notifications.WindowsBanners.OnNotification(item.AppId);
        var s = Settings.Current;
        bool quiet = !s.NotificationBanners || s.NotificationDoNotDisturb
                     || _shell?.FullScreenHelper.FullScreenApps.Count > 0;
        if (item.IsLocal || !quiet)
            Notifications.NotificationBannerWindow.ShowBanner(item);
    }

    /// <summary>
    /// Tecla Windows: abre el menú Inicio de la barra principal o, si está elegido, el de la barra
    /// con Inicio del monitor donde está el mouse.
    /// </summary>
    private void OnWinKey()
    {
        var taskbar = (Settings.Current.StartOnMouseMonitor ? MouseMonitorStartTaskbar() : null) ?? AppsTaskbar;
        if (taskbar != null)
            StartMenu.StartMenuLauncher.Toggle(taskbar.StartAnchor);
    }

    private static void UpdateKeyHook() =>
        Services.StartMenu.WinKeyHook.Update(Settings.Current.StartUseWinKey, Settings.Current.NotificationsEnabled, AnyVolumeOsd(),
                                             Settings.Current.FilesReplaceExplorer);

    /// <summary>Barra con el botón Inicio en el monitor del mouse (null si ese monitor no tiene).</summary>
    private Taskbar? MouseMonitorStartTaskbar()
    {
        var device = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).DeviceName;
        return _taskbars.FirstOrDefault(t => t.Device == device && t.Has(BarItem.Start));
    }

    /// <summary>Barra con el botón Inicio (tecla Windows, Win+X): la del monitor principal si lo lleva.</summary>
    private Taskbar? AppsTaskbar =>
        _taskbars.FirstOrDefault(t => t.IsMainScreen && t.Has(BarItem.Start))
        ?? _taskbars.FirstOrDefault(t => t.Has(BarItem.Start))
        ?? _taskbars.FirstOrDefault(t => t.IsMain)
        ?? _taskbars.FirstOrDefault();

    /// <summary>Barra junto a la que aparecen los avisos en pantalla (volumen, brillo, Bloq Mayús…).</summary>
    public Taskbar? OsdTaskbar => StatusTaskbar ?? AppsTaskbar;

    /// <summary>Barra con la campana o el reloj (Win+N): la del monitor principal si la lleva.</summary>
    private Taskbar? StatusTaskbar =>
        _taskbars.FirstOrDefault(t => t.IsMainScreen && t.Has(BarItem.Notifications))
        ?? _taskbars.FirstOrDefault(t => t.Has(BarItem.Notifications))
        ?? _taskbars.FirstOrDefault(t => t.IsMainScreen && t.Has(BarItem.Clock))
        ?? _taskbars.FirstOrDefault(t => t.Has(BarItem.Clock))
        ?? _taskbars.FirstOrDefault(t => t.IsMain)
        ?? _taskbars.FirstOrDefault();

    private string? _audioMirror;
    private bool _audioMirrorWatching;

    /// <summary>"Sonar también en": aplica la salida elegida y la sigue cuando cambian los dispositivos.</summary>
    private void StartAudioMirror()
    {
        if (!_audioMirrorWatching)
        {
            Audio.DevicesChanged += Services.Audio.AudioMirror.Update;
            _audioMirrorWatching = true;
        }
        Services.Audio.AudioMirror.Update();
    }

    private void OnSettingsChanged()
    {
        UpdateKeyHook();
        if (Settings.Current.AudioMirrorDevice != _audioMirror)
        {
            _audioMirror = Settings.Current.AudioMirrorDevice;
            StartAudioMirror();
        }
        if (Settings.Current.StartExplorerMenu != _explorerMenu)
        {
            _explorerMenu = Settings.Current.StartExplorerMenu;
            Services.StartMenu.ExplorerMenu.Update();
        }
        Services.Notifications.NotificationService.Update();
        if (!_exiting)
        {
            Services.Notifications.WindowsBanners.Update();
            Services.Themes.WindowsAnimations.Update();
        }
        Services.Updates.UpdateService.UpdateSchedule();
        if (_exiting)
            return;
        if (!_openingTaskbars && BarLayoutKey() != _barLayout)
        {
            // Cambiar de borde implica registrar la barra de nuevo ante Windows: se recrean.
            Log.Info("Cambió la posición de las barras; recreando");
            Flyouts.FlyoutManager.CloseAll();
            CloseTaskbars();
            OpenTaskbars();
        }
        // El tamaño de cada barra (por monitor) lo ajusta ella misma en ApplySettings.
        Sensors.StopIfUnused(_taskbars.Any(t => t.Has(BarItem.Cpu) || t.Has(BarItem.Gpu) || t.Has(BarItem.Fps))
                             || Settings.Current.Widgets.Any(w => w.Kind is WidgetKind.Cpu or WidgetKind.Gpu or WidgetKind.Fps));
    }

    /// <summary>
    /// Reinicia el Explorador para aplicar ajustes y luego reinicia Strata UI, que vuelve a ocultar
    /// la barra nativa y a registrar la bandeja sobre el nuevo Explorador.
    /// </summary>
    public async void RestartExplorer()
    {
        Log.Info("Reiniciando el Explorador");
        // En segundo plano: esperar en el hilo de la UI congelaría la barra y los paneles.
        await Task.Run(async () =>
        {
            foreach (var explorer in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    explorer.Kill();
                    explorer.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Log.Error("No se pudo cerrar el Explorador", ex);
                }
            }
            // Windows suele relanzarlo solo (AutoRestartShell); si no, lo iniciamos.
            await Task.Delay(1500);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            await Task.Delay(2000);
        });
        ExitShell(restart: true);
    }

    /// <summary>Cuándo arrancó esta instancia (para ignorar copias que llegan a la vez).</summary>
    private DateTime _startedAt;

    /// <summary>
    /// Otra copia pidió el Hub. Al iniciar sesión pueden arrancar dos (la tarea programada y la
    /// clave Run que deja el instalador): la que llega en el primer minuto no abre el Hub. Los
    /// pedidos con página o del modo edición (--hub X, --edit) sí se atienden.
    /// </summary>
    private void OnOpenHubRequested()
    {
        if (DateTime.UtcNow - _startedAt < TimeSpan.FromSeconds(60)
            && !File.Exists(HubRequestPath) && !Settings.Current.HubAtStartup)
        {
            Log.Info("Se ignoró el pedido de abrir el Hub de otra copia lanzada al iniciar sesión");
            return;
        }
        OpenHub();
    }

    /// <summary>Abre el Hub en una página ("Updates", "Notifications"…).</summary>
    public void OpenHub(string page)
    {
        OpenHub();
        _hub?.NavigateTo(page);
    }

    public void OpenHub()
    {
        if (_exiting)
            return;
        // Otra instancia pidió el modo edición (StrataUI.exe --edit).
        if (File.Exists(HubRequestPath))
        {
            try
            {
                string request = File.ReadAllText(HubRequestPath);
                if (request.Trim() == EditRequest)
                {
                    File.Delete(HubRequestPath);
                    EditMode.EditSession.Start();
                    return;
                }
                if (request.StartsWith(FilesRequest, StringComparison.Ordinal))
                {
                    File.Delete(HubRequestPath);
                    OpenFiles(request[FilesRequest.Length..].Trim() is { Length: > 0 } path ? path : null);
                    return;
                }
            }
            catch (IOException)
            {
            }
        }
        if (_hub is null)
        {
            _hub = new Hub.HubWindow();
            _hub.Closed += (_, _) =>
            {
                _hub = null;
                MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
            };
        }
        _hub.Show();
        if (File.Exists(HubRequestPath))
        {
            try
            {
                _hub.NavigateTo(File.ReadAllText(HubRequestPath).Trim());
                File.Delete(HubRequestPath);
            }
            catch (IOException)
            {
            }
        }
        if (_hub.WindowState == WindowState.Minimized)
            _hub.WindowState = WindowState.Normal;
        // Topmost momentáneo: garantiza que quede encima aunque Windows niegue el foco.
        _hub.Topmost = true;
        _hub.Activate();
        _hub.Topmost = false;
    }

    /// <summary>Abre una ventana nueva del explorador de archivos (en esa carpeta o en Este equipo).</summary>
    public void OpenFiles(string? path = null)
    {
        if (_exiting)
            return;
        var window = new Files.FilesWindow(path);
        window.Show();
        // Topmost momentáneo, como el Hub: queda encima aunque Windows niegue el foco.
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
    }

    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void ExitShell(bool restart = false)
    {
        if (_exiting)
            return;
        _exiting = true;
        Log.Info(restart ? "Reiniciando Strata UI" : "Cerrando Strata UI");

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
        Settings.Changed -= OnSettingsChanged;
        EditMode.EditSession.Cancel();
        StrataTrayIcon.Remove();
        TrayOwnership.Stop();
        ExplorerWatch.Stop();
        TrayMemory.Flush();
        Services.StartMenu.WinKeyHook.Update(false, false);
        Services.Notifications.NotificationService.Shutdown();
        Services.Notifications.WindowsBanners.Restore();
        Services.Themes.WindowsAnimations.Restore();
        Notifications.NotificationBannerWindow.CloseAll();
        Notifications.Osd.Shutdown();
        Controls.TaskPreviewWindow.CloseAll();
        Services.StartMenu.StartMenuService.Shutdown();
        Flyouts.FlyoutManager.CloseAll();
        _hub?.Close();
        _shell?.AppBarManager.SignalGracefulShutdown();
        CloseTaskbars();
        if (_shell != null)
        {
            _shell.ExplorerHelper.HideExplorerTaskbar = false;
            _shell.Dispose();
            _shell = null;
        }
        Services.Themes.WindowsTheming.Shutdown();
        ThemeManager.Shutdown();
        Services.Audio.AudioMirror.Stop();
        _audio?.Dispose();
        Services.Brightness.BrightnessService.Shutdown();
        SystemStatus.Shutdown();
        Services.Updates.UpdateService.Shutdown();

        _exitWait?.Unregister(null);
        _exitEvent?.Dispose();
        _openHubWait?.Unregister(null);
        _openHubEvent?.Dispose();
        Services.StartMenu.ExplorerMenu.Stop();
        Services.Widgets.WidgetManager.Shutdown();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;

        if (restart && Environment.ProcessPath is { } exe)
            Process.Start(exe);
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Un error en la UI no debe dejar al usuario sin barra: se registra y se sigue.
        Log.Error("Excepción no controlada en la UI", e.Exception);
        e.Handled = true;
    }

    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Excepción fatal", e.ExceptionObject as Exception);
        try
        {
            if (_shell != null)
                _shell.ExplorerHelper.HideExplorerTaskbar = false;
        }
        catch
        {
            // Si ManagedShell ya no responde, recurrimos a la restauración directa.
        }
        NativeTaskbar.Restore();
        Services.Notifications.WindowsBanners.Restore();
        Services.Themes.WindowsAnimations.Restore();
    }
}
