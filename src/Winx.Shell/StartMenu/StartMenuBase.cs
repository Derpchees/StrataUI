using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Winx.Shell.Flyouts;
using Winx.Shell.Services;

namespace Winx.Shell.StartMenu;

/// <summary>
/// Comportamiento común de los estilos de menú Inicio: modelo compartido, foco en el buscador,
/// escribir en cualquier parte busca, Enter abre el primer resultado, Esc limpia antes de cerrar,
/// y asas para cambiar el tamaño (que se recuerda por estilo).
/// </summary>
public abstract class StartMenuBase : FlyoutWindow
{
    private const double MinWidthDip = 300, MinHeightDip = 320;

    protected StartMenuBase()
    {
        DataContext = Model;
        Loaded += (_, _) =>
        {
            RestoreSize();
            if (!FillWorkArea)
                AddResizeGrips();
            SearchInput?.Focus();
        };
        PreviewTextInput += (_, e) =>
        {
            // Empezar a escribir con el foco en otro control lleva el texto al buscador.
            if (SearchInput is { IsKeyboardFocusWithin: false } box && !string.IsNullOrWhiteSpace(e.Text)
                && Keyboard.FocusedElement is not TextBox)
            {
                box.Focus();
                box.AppendText(e.Text);
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
            }
        };
    }

    public StartMenuModel Model { get; } = new();

    /// <summary>Caja de búsqueda del estilo concreto.</summary>
    protected abstract TextBox? SearchInput { get; }

    /// <summary>False en estilos que crecen con su contenido (solo se puede cambiar el ancho).</summary>
    protected virtual bool ResizableHeight => SizeToContent == SizeToContent.Manual;

    protected override bool ScrollContent => false;

    protected override FlyoutPlacement Placement =>
        WindowsTaskbarPrefs.Alignment(Settings.Current) == TaskbarAlignment.Left ? FlyoutPlacement.Left : FlyoutPlacement.Center;

    protected override bool HandleEscape()
    {
        if (Model.ShowAllApps || Model.IsSearching)
        {
            Model.Query = "";
            Model.ShowAllApps = false;
            return true;
        }
        return false;
    }

    protected void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Model.Submit();
            e.Handled = true;
        }
    }

    protected void OnShowAllApps(object sender, RoutedEventArgs e) => Model.ShowAllApps = true;

    protected void OnBackToHome(object sender, RoutedEventArgs e) => Model.ShowAllApps = false;

    // ── Tamaño ──────────────────────────────────────────────────────────────

    private string SizeKey => GetType().Name;

    private void RestoreSize()
    {
        if (!Settings.Current.StartMenuSizes.TryGetValue(SizeKey, out var size) || size.Length < 2)
            return;
        Width = Math.Max(MinWidthDip, size[0]);
        if (ResizableHeight)
            Height = Math.Max(MinHeightDip, size[1]);
    }

    private void SaveSize()
    {
        Settings.Current.StartMenuSizes[SizeKey] = [Width, ResizableHeight ? Height : 0];
        Settings.Save();
    }

    /// <summary>
    /// Asas invisibles en los bordes libres del menú (los que no están pegados a la barra) y en el
    /// lateral que se aleja del botón Inicio. En menús centrados el ancho crece por ambos lados.
    /// </summary>
    private void AddResizeGrips()
    {
        // Con la barra a un lado, el menú queda pegado a ella: crece hacia el lado contrario y hacia abajo.
        bool sideBar = BarPosition is TaskbarPosition.Left or TaskbarPosition.Right && Placement != FlyoutPlacement.ScreenCenter;
        bool growsLeft = sideBar ? BarPosition == TaskbarPosition.Right : Placement == FlyoutPlacement.Right;
        bool symmetric = !sideBar && Placement is FlyoutPlacement.Center or FlyoutPlacement.ScreenCenter;
        var sideAlign = growsLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        // El alto se cambia en el borde libre: arriba si la barra está abajo; abajo en los demás
        // casos (barra arriba o a un lado, o "solo búsqueda", que tiene fija la parte de arriba).
        var verticalEdge = BarPosition == TaskbarPosition.Bottom && Placement != FlyoutPlacement.ScreenCenter
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;


        var grips = new Grid();
        grips.Children.Add(Grip(Cursors.SizeWE, sideAlign, VerticalAlignment.Stretch, 6, double.NaN, horizontal: true, vertical: false));
        if (ResizableHeight)
        {
            grips.Children.Add(Grip(Cursors.SizeNS, HorizontalAlignment.Stretch, verticalEdge, double.NaN, 6, horizontal: false, vertical: true));
            var corner = (growsLeft, verticalEdge) switch
            {
                (true, VerticalAlignment.Top) or (false, VerticalAlignment.Bottom) => Cursors.SizeNWSE,
                _ => Cursors.SizeNESW,
            };
            grips.Children.Add(Grip(corner, sideAlign, verticalEdge, 14, 14, horizontal: true, vertical: true));
        }
        AddOverlay(grips);

        Thumb Grip(Cursor cursor, HorizontalAlignment h, VerticalAlignment v, double width, double height, bool horizontal, bool vertical)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Width = width,
                Height = height,
                Template = TransparentThumb(),
            };
            Point start = default;
            Size startSize = default;
            thumb.DragStarted += (_, _) =>
            {
                start = CursorPosition();
                startSize = new Size(ActualWidth, ActualHeight);
            };
            thumb.DragDelta += (_, _) =>
            {
                // Coordenadas de pantalla: la ventana se reposiciona mientras crece y las
                // coordenadas relativas al asa no servirían.
                var now = CursorPosition();
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (horizontal)
                {
                    double dx = (now.X - start.X) / scale * (growsLeft ? -1 : 1) * (symmetric ? 2 : 1);
                    Width = Math.Max(MinWidthDip, startSize.Width + dx);
                }
                if (vertical)
                {
                    double dy = (now.Y - start.Y) / scale * (verticalEdge == VerticalAlignment.Top ? -1 : 1);
                    Height = Math.Clamp(startSize.Height + dy, MinHeightDip, MaxHeight);
                }
            };
            thumb.DragCompleted += (_, _) => SaveSize();
            return thumb;
        }
    }

    private static ControlTemplate TransparentThumb()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        template.VisualTree = border;
        return template;
    }

    private static Point CursorPosition()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
