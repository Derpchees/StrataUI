using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Lo que se ve junto al puntero mientras se arrastran archivos (Archivos y el escritorio): el ícono,
/// cuántos son y, sobre una carpeta de Strata UI, qué va a pasar («Mover a Fotos»). WPF no muestra
/// nada propio y el arrastre se veía como un puntero suelto.
/// </summary>
public static class DragGhost
{
    private static Window? _window;
    private static TextBlock? _caption;

    /// <summary>Arrastra <paramref name="data"/> desde <paramref name="source"/> con la vista previa junto al puntero.</summary>
    public static DragDropEffects Run(UIElement source, DataObject data, DragDropEffects allowed, ImageSource? icon, int count)
    {
        Show(icon, count);
        GiveFeedbackEventHandler feedback = (_, _) => Follow();
        DragDrop.AddGiveFeedbackHandler(source, feedback);
        try
        {
            return DragDrop.DoDragDrop(source, data, allowed);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(source, feedback);
            _window?.Close();
            _window = null;
            _caption = null;
        }
    }

    /// <summary>
    /// Lo que pasará al soltar sobre <paramref name="target"/> (una carpeta); null o sin efecto lo
    /// quita. Lo llama quien recibe el arrastre en cada DragOver y al salir.
    /// </summary>
    public static void Caption(DragDropEffects effect, string? target)
    {
        if (_caption is null)
            return;
        string name = target is null ? "" : Path.GetFileName(target.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : target;
        string? text = target is null ? null : effect switch
        {
            DragDropEffects.Move => Loc.F("Mover a {0}", name),
            DragDropEffects.Copy => Loc.F("Copiar a {0}", name),
            DragDropEffects.Link => Loc.F("Crear acceso directo en {0}", name),
            _ => null,
        };
        _caption.Text = text ?? "";
        _caption.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Show(ImageSource? icon, int count)
    {
        _window?.Close();
        var image = new Image { Source = icon, Width = 40, Height = 40, Margin = new Thickness(2) };
        var stack = new Grid { Width = 48, Height = 48 };
        // Varios: una «hoja» detrás, como una pila.
        if (count > 1)
        {
            var behind = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 0, 0, 6), Opacity = 0.55 };
            behind.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorTertiaryBrush");
            stack.Children.Add(behind);
        }
        stack.Children.Add(image);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(stack);
        if (count > 1)
        {
            var badgeText = new TextBlock { Text = count.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                                            HorizontalAlignment = HorizontalAlignment.Center };
            var badge = new Border { CornerRadius = new CornerRadius(10), MinWidth = 20, Padding = new Thickness(6, 1, 6, 2),
                                     VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-14, -4, 0, 0), Child = badgeText };
            badge.SetResourceReference(Border.BackgroundProperty, "StrataAccentBrush");
            panel.Children.Add(badge);
        }
        _caption = new TextBlock { FontSize = 12, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                                   Visibility = Visibility.Collapsed };
        _caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        panel.Children.Add(_caption);
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = panel,
                                Margin = new Thickness(8) };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.25 };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            IsHitTestVisible = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = card,
            Opacity = 0.92,
            Left = -32000,
            Top = -32000,
        };
        window.SourceInitialized += (_, _) =>
        {
            // Que no reciba el mouse ni el foco: debajo está la carpeta donde se va a soltar.
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExTransparent | WsExNoActivate | WsExToolWindow);
        };
        _window = window;
        window.Show();
        Follow();
    }

    /// <summary>Abajo a la derecha del puntero, en píxeles (cada monitor con su escala).</summary>
    private static void Follow()
    {
        if (_window is null || !GetCursorPos(out var cursor))
            return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero)
            return;
        double scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        SetWindowPos(handle, HwndTopmost, cursor.X + (int)(8 * scale), cursor.Y + (int)(10 * scale), 0, 0,
                     SwpNoSize | SwpNoActivate);
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x1, SwpNoActivate = 0x10;
    private static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
