using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using Forms = System.Windows.Forms;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Winx.Shell.Controls;

/// <summary>Ícono de la bandeja: reenvía el mouse a la app dueña del ícono.</summary>
public partial class TrayIcon : UserControl
{
    private static readonly HashSet<Guid> ReplacedSystemIcons = new[]
    {
        NotificationArea.VOLUME_GUID, NotificationArea.NETWORK_GUID, NotificationArea.POWER_GUID,
    }.Select(Guid.Parse).ToHashSet();

    public TrayIcon()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
            Visibility = Icon is { } icon && IsReplacedByWinx(icon) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Volumen, red y energía de Windows: el área de estado de WINX los sustituye.</summary>
    public static bool IsReplacedByWinx(ShellNotifyIcon icon) => ReplacedSystemIcons.Contains(icon.GUID);

    private ShellNotifyIcon? Icon => DataContext as ShellNotifyIcon;

    private static uint CursorParam => MouseHelper.GetCursorPositionParam();

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Icon?.IconMouseDown(e.ChangedButton, CursorParam, Forms.SystemInformation.DoubleClickTime);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Marcarlo como manejado evita que se abra el menú contextual de la barra.
        e.Handled = true;
        Icon?.IconMouseUp(e.ChangedButton, CursorParam, Forms.SystemInformation.DoubleClickTime);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (Icon is not { } icon)
            return;
        // Posición en pantalla (píxeles físicos) para que la app ubique su menú o tooltip.
        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        icon.Placement = new NativeMethods.Rect
        {
            Left = (int)topLeft.X,
            Top = (int)topLeft.Y,
            Right = (int)bottomRight.X,
            Bottom = (int)bottomRight.Y,
        };
        icon.IconMouseEnter(CursorParam);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => Icon?.IconMouseLeave(CursorParam);

    private void OnMouseMove(object sender, MouseEventArgs e) => Icon?.IconMouseMove(CursorParam);
}
