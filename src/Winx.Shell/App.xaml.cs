using System.Diagnostics;
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
    private const string RestoreArg = "--restore-taskbar";
    private const string ExitArg = "--exit";

    private Mutex? _mutex;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private ShellManager? _shell;
    private readonly List<Taskbar> _taskbars = new();
    private int _screenCount;
    private bool _exiting;

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

        // Pide a la instancia en ejecución que se cierre limpiamente (restaura la barra de Windows).
        if (e.Args.Contains(ExitArg, StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var running))
            {
                running.Set();
                running.Dispose();
            }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent,
            (_, _) => Dispatcher.BeginInvoke(() => ExitShell()), null, Timeout.Infinite, true);

        Log.Info($"WINX {Version} iniciando");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;

        ThemeManager.Initialize();

        var config = ShellManager.DefaultShellConfig;
        config.TaskIconSize = IconSize.Large;
        config.MultiMonAwareTasksService = true;
        _shell = new ShellManager(config);

        TrayPromotion.Attach(_shell.NotificationArea);
        OpenTaskbars();
        _shell.ExplorerHelper.HideExplorerTaskbar = true;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // ManagedShell termina de inicializar los estados de las ventanas de forma asíncrona.
        var activeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        activeTimer.Tick += (_, _) =>
        {
            activeTimer.Stop();
            MarkForegroundWindowActive();
        };
        activeTimer.Start();
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
        bool multiMonitor = screens.Count > 1;

        // La barra principal primero, para que reciba la bandeja del sistema.
        foreach (var screen in screens.OrderByDescending(s => s.Primary))
        {
            var taskbar = new Taskbar(_shell!, screen, multiMonitor);
            taskbar.Show();
            _taskbars.Add(taskbar);
        }
    }

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
        });
    }

    public void ExitShell(bool restart = false)
    {
        if (_exiting)
            return;
        _exiting = true;
        Log.Info(restart ? "Reiniciando WINX" : "Cerrando WINX");

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _shell?.AppBarManager.SignalGracefulShutdown();
        CloseTaskbars();
        if (_shell != null)
        {
            _shell.ExplorerHelper.HideExplorerTaskbar = false;
            _shell.Dispose();
            _shell = null;
        }
        ThemeManager.Shutdown();

        _exitWait?.Unregister(null);
        _exitEvent?.Dispose();
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
