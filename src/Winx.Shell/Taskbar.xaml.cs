using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.UWPInterop;
using ManagedShell.WindowsTasks;
using ManagedShell.WindowsTray;
using Winx.Shell.Controls;
using Winx.Shell.Services;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Winx.Shell;

public partial class Taskbar : AppBarWindow
{
    private const double BarHeight = 48;

    private readonly ShellManager _shell;
    private readonly bool _multiMonitor;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _statusTimer;

    public Taskbar(ShellManager shell, AppBarScreen screen, bool multiMonitor)
        : base(shell.AppBarManager, shell.ExplorerHelper, shell.FullScreenHelper,
               screen, AppBarEdge.Bottom, AppBarMode.Normal, BarHeight)
    {
        _shell = shell;
        _multiMonitor = multiMonitor;
        InitializeComponent();

        VersionItem.Header = $"WINX v{App.Version}";
        TaskList.ItemsSource = CreateTaskView();

        if (screen.Primary)
        {
            var tray = shell.NotificationArea;
            PinnedTray.ItemsSource = tray.PinnedIcons;
            HiddenTray.ItemsSource = tray.UnpinnedIcons;
            ((INotifyCollectionChanged)tray.UnpinnedIcons).CollectionChanged += (_, _) => UpdateChevron();
            UpdateChevron();
            LocationChanged += (_, _) => ReportTrayPosition();
            SizeChanged += (_, _) => ReportTrayPosition();
        }
        else
        {
            TrayArea.Visibility = Visibility.Collapsed;
        }

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    private ICollectionView CreateTaskView()
    {
        var view = _shell.Tasks.CreateGroupedWindowsCollection();
        view.Filter = item => item is ApplicationWindow window
                              && window.ShowInTaskbar
                              && (!_multiMonitor || window.HMonitor == Screen.HMonitor);

        if (view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.ShowInTaskbar));
            live.LiveFilteringProperties.Add(nameof(ApplicationWindow.HMonitor));
            live.IsLiveFiltering = true;
        }
        return view;
    }

    private void UpdateChevron() =>
        ChevronButton.Visibility = _shell.NotificationArea.UnpinnedIcons
            .OfType<ShellNotifyIcon>().Any(icon => !TrayIcon.IsReplacedByWinx(icon))
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Algunas apps preguntan dónde está la bandeja para colocar sus menús.</summary>
    private void ReportTrayPosition()
    {
        var rect = WindowRect;
        _shell.NotificationArea.SetTrayHostSizeData(new TrayHostSizeData
        {
            edge = NativeMethods.ABEdge.ABE_BOTTOM,
            rc = new NativeMethods.Rect
            {
                Left = (int)(rect.Left * DpiScale),
                Top = (int)(rect.Top * DpiScale),
                Right = (int)(rect.Right * DpiScale),
                Bottom = (int)(rect.Bottom * DpiScale),
            },
        });
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("t");
        DateText.Text = now.ToString("d");
        ClockButton.ToolTip = now.ToString("D");
    }

    private void UpdateStatus()
    {
        var network = SystemStatus.GetNetwork();
        NetworkGlyph.Text = network.Glyph.ToString();
        NetworkGlyph.Opacity = network.Connected ? 1 : 0.4;

        var battery = SystemStatus.GetBattery();
        string tooltip = network.Description;
        if (battery is null)
        {
            BatteryGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            BatteryGlyph.Visibility = Visibility.Visible;
            BatteryGlyph.Text = battery.Glyph.ToString();
            tooltip += $"\nBatería: {battery.Percent}%{(battery.Charging ? " (cargando)" : "")}";
        }
        StatusButton.ToolTip = tooltip;
    }

    protected override void CustomClosing()
    {
        _clockTimer.Stop();
        _statusTimer.Stop();
        base.CustomClosing();
    }

    // Mientras no existan los paneles propios (próximas versiones), se abren los de Windows.

    private void StartButton_Click(object sender, RoutedEventArgs e) => ShellHelper.ShowStartMenu();

    private void ClockButton_Click(object sender, RoutedEventArgs e) => ShellHelper.ShowNotificationCenter();

    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImmersiveShellHelper.ShowControlCenter();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo abrir el centro de control", ex);
            ShellHelper.ShowActionCenter();
        }
    }

    private void ShowDesktop_Click(object sender, MouseButtonEventArgs e)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null)
            return;
        dynamic shellApp = Activator.CreateInstance(type)!;
        shellApp.ToggleDesktop();
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e) => ShellHelper.StartTaskManager();

    private void WindowsSettings_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });

    private void RestartWinx_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell(restart: true);

    private void ExitWinx_Click(object sender, RoutedEventArgs e) => App.Current.ExitShell();
}
