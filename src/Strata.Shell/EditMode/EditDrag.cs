using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Strata.Shell.Services;

namespace Strata.Shell.EditMode;

/// <summary>Lo que se arrastra en el modo edición.</summary>
internal abstract record DragPayload;

/// <summary>Un elemento de barra; <paramref name="FromDevice"/> es su monitor, o null si viene de la biblioteca.</summary>
internal sealed record ItemPayload(BarItem Item, string? FromDevice) : DragPayload;

/// <summary>Un widget (del borrador o uno nuevo de la biblioteca) y su tamaño en la capa, en DIP.</summary>
internal sealed record WidgetPayload(WidgetConfig Widget, Size Size, Vector Grab) : DragPayload;

/// <summary>Dónde caería lo arrastrado, con el rectángulo a resaltar (en DIP de la capa).</summary>
internal abstract record DropTarget(Rect Marker);

/// <summary>En una barra: posición en su lista (null: su lugar de siempre) y zona.</summary>
internal sealed record BarTarget(TaskbarPosition Edge, int? Index, BarZone Zone, Rect Marker) : DropTarget(Marker);

/// <summary>En un borde sin barra: crea la barra.</summary>
internal sealed record NewBarTarget(TaskbarPosition Edge, Rect Marker) : DropTarget(Marker);

/// <summary>Fuera de las barras: un elemento que venía de una barra se quita.</summary>
internal sealed record RemoveTarget(Rect Marker) : DropTarget(Marker);

/// <summary>Un lugar del escritorio para un widget (fracción del espacio libre).</summary>
internal sealed record DesktopTarget(double X, double Y, Rect Marker) : DropTarget(Marker);

/// <summary>
/// Arrastrar en el modo edición, de una capa a otra (otro monitor) o desde la biblioteca del Hub:
/// la captura del mouse queda en el elemento donde empezó y todo se ubica por la posición del
/// puntero en la pantalla. Empieza al mover unos píxeles con el botón pulsado; si no, es un clic.
/// </summary>
internal static class EditDrag
{
    private const double Threshold = 6;

    private static FrameworkElement? _source;
    private static DragPayload? _payload;
    private static Point _press;
    private static bool _started;
    private static bool _ending;
    private static Action? _onClick;

    public static bool Active => _started;

    /// <summary>
    /// Botón pulsado sobre algo que se puede arrastrar. <paramref name="onClick"/> corre si se suelta
    /// sin moverse.
    /// </summary>
    public static void Press(FrameworkElement source, DragPayload payload, MouseButtonEventArgs e, Action? onClick = null)
    {
        Abort();
        _source = source;
        _payload = payload;
        _onClick = onClick;
        _press = Cursor();
        if (!source.CaptureMouse())
        {
            Reset();
            return;
        }
        source.MouseMove += OnMove;
        source.MouseLeftButtonUp += OnUp;
        source.LostMouseCapture += OnLost;
        e.Handled = true;
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (_payload is not { } payload || _source is not { } source)
            return;
        var point = Cursor();
        if (!_started)
        {
            if ((point - _press).Length < Threshold)
                return;
            _started = true;
            Mouse.OverrideCursor = Cursors.SizeAll;
            Controls.DragGhost.Begin(source);
            source.Opacity = 0.4;
            EditSession.ShowDropZones(true);
        }
        Controls.DragGhost.Move();
        EditSession.UpdateMarker(point, payload);
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var payload = _payload;
        bool started = _started;
        var click = _onClick;
        e.Handled = true;
        Finish();
        try
        {
            if (started && payload != null)
                EditSession.Drop(Cursor(), payload);
            else
                click?.Invoke();
        }
        catch (Exception ex)
        {
            // Un error al soltar no debe dejar el modo edición trabado.
            Log.Error("Modo edición: no se pudo soltar", ex);
        }
    }

    private static void OnLost(object sender, MouseEventArgs e)
    {
        if (!_ending)
            Abort();
    }

    /// <summary>Corta el arrastre sin soltar nada (Esc, otra ventana se llevó el mouse, se cerró el modo).</summary>
    public static void Abort()
    {
        if (_source is null)
            return;
        Finish();
    }

    private static void Finish()
    {
        _ending = true;
        try
        {
            if (_source is { } source)
            {
                source.MouseMove -= OnMove;
                source.MouseLeftButtonUp -= OnUp;
                source.LostMouseCapture -= OnLost;
                source.Opacity = 1;
                if (source.IsMouseCaptured)
                    source.ReleaseMouseCapture();
            }
            if (_started)
            {
                Controls.DragGhost.End();
                Mouse.OverrideCursor = null;
                EditSession.ShowDropZones(false);
            }
        }
        finally
        {
            _ending = false;
            Reset();
        }
    }

    private static void Reset()
    {
        _source = null;
        _payload = null;
        _onClick = null;
        _started = false;
    }

    /// <summary>El puntero en píxeles de pantalla.</summary>
    internal static Point Cursor()
    {
        GetCursorPos(out var cursor);
        return new Point(cursor.X, cursor.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
