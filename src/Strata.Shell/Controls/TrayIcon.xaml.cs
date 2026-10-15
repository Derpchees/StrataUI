using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;
using Forms = System.Windows.Forms;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Strata.Shell.Controls;

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
            Visibility = Icon is { } icon && IsReplacedByStrata(icon) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Volumen, red y energía de Windows: el área de estado de Strata UI los sustituye.</summary>
    public static bool IsReplacedByStrata(ShellNotifyIcon icon) => ReplacedSystemIcons.Contains(icon.GUID);

    private ShellNotifyIcon? Icon => DataContext as ShellNotifyIcon;

    private static uint CursorParam => MouseHelper.GetCursorPositionParam();

    /// <summary>Se empezó a arrastrar el ícono (para reordenar la bandeja).</summary>
    public static event Action<TrayIcon>? DragStarted;

    /// <summary>
    /// El clic izquierdo se envía a la app al soltar, no al pulsar: así un arrastre (para ordenar)
    /// no llega a la app como clic.
    /// </summary>
    private Point? _pressedAt;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ChangedButton == MouseButton.Left)
        {
            _pressedAt = e.GetPosition(this);
            return;
        }
        Icon?.IconMouseDown(e.ChangedButton, CursorParam, Forms.SystemInformation.DoubleClickTime);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Marcarlo como manejado evita que se abra el menú contextual de la barra.
        e.Handled = true;
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_pressedAt is null)
                return; // terminó un arrastre
            _pressedAt = null;
            Icon?.IconMouseDown(MouseButton.Left, CursorParam, Forms.SystemInformation.DoubleClickTime);
        }
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

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedAt is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            var delta = e.GetPosition(this) - start;
            if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _pressedAt = null;
                DragStarted?.Invoke(this);
                return;
            }
        }
        else if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedAt = null;
        }
        Icon?.IconMouseMove(CursorParam);
    }

    /// <summary>Ícono de la bandeja que representa este control.</summary>
    public ShellNotifyIcon? NotifyIcon => Icon;
}
