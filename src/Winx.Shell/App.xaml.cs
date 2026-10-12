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
using Winx.Shell.Services;

namespace Winx.Shell;

public partial class App : Application
{
    private const string MutexName = @"Local\WINX.Shell";
    private const string ExitEventName = @"Local\WINX.Shell.Exit";
    private const string OpenHubEventName = @"Local\WINX.Shell.OpenHub";
    private const string RestoreArg = "--restore-taskbar";
    private const string ExitArg = "--exit";

    private static readonly string HubRequestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINX", "hub-request.txt");

    private Mutex? _mutex;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private EventWaitHandle? _openHubEvent;
    private RegisteredWaitHandle? _openHubWait;
    private ShellManager? _shell;
    private readonly List<Taskbar> _taskbars = new();
    private Hub.HubWindow? _hub;
    private int _screenCount;
    private double _barHeight;
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

        // Herramienta de emergencia: devuelve la barra de Windows si WINX murió sin limpiar.
        if (e.Args.Contains(RestoreArg, StringComparer.OrdinalIgnoreCase))
        {
            NativeTaskbar.Restore();
            Shutdown();
            return;
        }

        // Tareas de administrador lanzadas por la instancia principal vía UAC (ver Tweaks/Elevation).
        if (e.Args.Length > 0 && e.Args[0] is "--apply-tweak" or "--restore-tweaks" or "--restore-point")
        {
            Shutdown(Tweaks.TweakEngine.RunElevatedCommand(e.Args));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == Services.Audio.AudioDeviceOptions.ElevatedArg)
        {
            Shutdown(Services.Audio.AudioDeviceOptions.RunElevated(e.Args));
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
                foreach (var other in Process.GetProcessesByName("WINX").Where(p => p.Id != Environment.ProcessId))
                {
                    using (other)
                        other.WaitForExit(15000);
                }
            }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Abrir WINX otra vez mientras ya corre muestra el Hub de la instancia existente.
            if (EventWaitHandle.TryOpenExisting(OpenHubEventName, out var openHub))
            {
                // "--hub Privacy" abre directamente esa página del Hub.
                int hubArg = Array.FindIndex(e.Args, a => a.Equals("--hub", StringComparison.OrdinalIgnoreCase));
                if (hubArg >= 0 && hubArg + 1 < e.Args.Length)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(HubRequestPath)!);
                    File.WriteAllText(HubRequestPath, e.Args[hubArg + 1]);
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
            (_, _) => Dispatcher.BeginInvoke(OpenHub), null, Timeout.Infinite, false);

        Services.Localization.Loc.Initialize(Settings.Current.Language);
        Log.Info($"WINX {Version} iniciando (idioma: {Services.Localization.Loc.Language})");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;

        // WPF asigna como MainWindow la primera ventana creada, y WPF-UI modifica esa ventana al
        // cambiar de tema (le quita los efectos de DWM). Una ventana invisible ocupa ese lugar
        // para que la barra, el Hub y los paneles conserven su material de fondo.
        MainWindow = new Window { ShowInTaskbar = false, ShowActivated = false };

        ThemeManager.Initialize();
        Services.Themes.WindowsTheming.Initialize();

        var config = ShellManager.DefaultShellConfig;
        config.TaskIconSize = IconSize.Large;
        config.MultiMonAwareTasksService = true;
        if (Settings.Current.PinnedTrayIcons is { } pinned)
            config.PinnedNotifyIcons = pinned;
        _shell = new ShellManager(config);

        TrayPromotion.Attach(_shell.NotificationArea);
        SystemStatus.Initialize();
        Services.StartMenu.StartMenuService.Initialize();
        Services.StartMenu.WinKeyHook.WinPressed += OnWinKey;
        Services.StartMenu.WinKeyHook.WinXPressed += () => MainTaskbar?.ShowWinXMenu();
        Services.StartMenu.WinKeyHook.WinNPressed += () => MainTaskbar?.ToggleNotificationCenter();
        UpdateKeyHook();
        OpenTaskbars();
        _shell.ExplorerHelper.HideExplorerTaskbar = true;
        _ = Services.Brightness.BrightnessService.RefreshAsync();
        Services.Notifications.NotificationService.Arrived += OnNotificationArrived;
        Services.Notifications.NotificationService.Update();
        Services.Updates.UpdateService.UpdateSchedule();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        Settings.Changed += OnSettingsChanged;
        _barHeight = Taskbar.ReservedSize(vertical: false);

        int hubIndex = Array.FindIndex(e.Args, a => a.Equals("--hub", StringComparison.OrdinalIgnoreCase));
        if (hubIndex >= 0)
        {
            OpenHub();
            if (hubIndex + 1 < e.Args.Length)
                _hub?.NavigateTo(e.Args[hubIndex + 1]);
        }

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

    private void OpenTaskbars()
    {
        var screens = AppBarScreen.FromAllScreens();
        _screenCount = screens.Count;
        _barLayout = BarLayoutKey();
        bool multiMonitor = screens.Count > 1;
        var s = Settings.Current;

        // La barra principal (de WINX, no necesariamente la de Windows) primero, para que reciba
        // la bandeja del sistema. Esa siempre existe; las demás pueden ocultarse.
        string main = MainScreenDevice(screens.Select(sc => (sc.DeviceName, sc.Primary)));
        foreach (var screen in screens.OrderByDescending(sc => sc.DeviceName == main))
        {
            bool isMain = screen.DeviceName == main;
            if (!isMain && s.TaskbarHiddenScreens.Contains(screen.DeviceName))
                continue;
            var taskbar = new Taskbar(_shell!, screen, multiMonitor, s.PositionOf(screen.DeviceName), isMain);
            taskbar.Show();
            _taskbars.Add(taskbar);
        }
        // Ya se sabe qué monitores quedaron sin barra: la principal muestra también sus ventanas.
        foreach (var taskbar in _taskbars)
            taskbar.RefreshTasks();
    }

    /// <summary>Hay una barra de WINX en ese monitor.</summary>
    public bool HasTaskbarOn(IntPtr hMonitor) => _taskbars.Any(t => t.Screen.HMonitor == hMonitor);

    /// <summary>
    /// Monitor principal de WINX: el elegido en el Hub si sigue conectado; si no, el de Windows.
    /// </summary>
    internal static string MainScreenDevice(IEnumerable<(string Device, bool Primary)> screens)
    {
        var list = screens.ToList();
        string? chosen = Settings.Current.TaskbarMainScreen;
        if (chosen != null && list.Any(sc => sc.Device == chosen))
            return chosen;
        return list.FirstOrDefault(sc => sc.Primary).Device ?? list.FirstOrDefault().Device ?? "";
    }

    /// <summary>Lo que obliga a recrear las barras: posición por monitor, monitores sin barra y principal.</summary>
    private static string BarLayoutKey()
    {
        var s = Settings.Current;
        return string.Join(";", s.TaskbarPositions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))
               + "|" + string.Join(";", s.TaskbarHiddenScreens.Order())
               + "|" + s.TaskbarMainScreen;
    }

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
            if (_exiting || AppBarScreen.FromAllScreens().Count == _screenCount)
                return;
            Log.Info("Cambió el número de monitores; recreando barras");
            CloseTaskbars();
            OpenTaskbars();
            _ = Services.Brightness.BrightnessService.RefreshAsync();
        });
    }

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
    private void OnNotificationArrived(Services.Notifications.WinxNotification item)
    {
        var s = Settings.Current;
        bool quiet = !s.NotificationBanners || s.NotificationDoNotDisturb
                     || _shell?.FullScreenHelper.FullScreenApps.Count > 0;
        if (item.IsLocal || !quiet)
            Notifications.NotificationBannerWindow.ShowBanner(item);
    }

    /// <summary>Tecla Windows: abre el menú Inicio de la barra principal.</summary>
    private void OnWinKey()
    {
        var taskbar = MainTaskbar;
        if (taskbar != null)
            StartMenu.StartMenuLauncher.Toggle(taskbar.StartAnchor);
    }

    private static void UpdateKeyHook() =>
        Services.StartMenu.WinKeyHook.Update(Settings.Current.StartUseWinKey, Settings.Current.NotificationsEnabled);

    /// <summary>Barra principal de WINX (bandeja, tecla Windows, Win+X, Win+N).</summary>
    private Taskbar? MainTaskbar => _taskbars.FirstOrDefault(t => t.IsMain) ?? _taskbars.FirstOrDefault();

    private void OnSettingsChanged()
    {
        UpdateKeyHook();
        Services.Notifications.NotificationService.Update();
        Services.Updates.UpdateService.UpdateSchedule();
        if (_exiting)
            return;
        if (BarLayoutKey() != _barLayout)
        {
            // Cambiar de borde implica registrar la barra de nuevo ante Windows: se recrean.
            Log.Info("Cambió la posición de las barras; recreando");
            Flyouts.FlyoutManager.CloseAll();
            CloseTaskbars();
            OpenTaskbars();
            _barHeight = Taskbar.ReservedSize(vertical: false);
            return;
        }
        if (Taskbar.ReservedSize(vertical: false) == _barHeight)
            return;
        _barHeight = Taskbar.ReservedSize(vertical: false);
        foreach (var taskbar in _taskbars)
            taskbar.Resize();
    }

    /// <summary>
    /// Reinicia el Explorador para aplicar ajustes y luego reinicia WINX, que vuelve a ocultar
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

    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void ExitShell(bool restart = false)
    {
        if (_exiting)
            return;
        _exiting = true;
        Log.Info(restart ? "Reiniciando WINX" : "Cerrando WINX");

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
        Settings.Changed -= OnSettingsChanged;
        Services.StartMenu.WinKeyHook.Update(false, false);
        Services.Notifications.NotificationService.Shutdown();
        Notifications.NotificationBannerWindow.CloseAll();
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
        _audio?.Dispose();
        Services.Brightness.BrightnessService.Shutdown();
        SystemStatus.Shutdown();
        Services.Updates.UpdateService.Shutdown();

        _exitWait?.Unregister(null);
        _exitEvent?.Dispose();
        _openHubWait?.Unregister(null);
        _openHubEvent?.Dispose();
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
    }
}
