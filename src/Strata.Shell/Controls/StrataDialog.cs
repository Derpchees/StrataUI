using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;

namespace Strata.Shell.Controls;

/// <summary>
/// Pregunta o aviso con el tema de Strata UI (el material, el tinte y el acento del Hub y de Archivos),
/// en vez del MessageBox de WPF-UI con su fondo propio. Sale sobre la ventana que lo pidió, en su
/// monitor (<see cref="DialogPlacement"/>). Enter elige el primer botón; Esc o cerrar, ninguno.
/// </summary>
public sealed class StrataDialog : FluentWindow
{
    private readonly TaskCompletionSource<int> _result = new();
    private readonly Grid _root;
    private int _choice = -1;

    private StrataDialog(Window? owner, string title, string message, IReadOnlyList<string> buttons, UIElement? content, bool elevate)
    {
        Title = title;
        Owner = owner;
        ShowInTaskbar = owner is null;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 440;
        MinHeight = 0; // FluentWindow trae una altura mínima que dejaba espacio vacío debajo de los botones
        WindowStartupLocation = WindowStartupLocation.Manual;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        FluentBackdrop.Follow(this);

        var text = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        text.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 28, 8),
        });
        text.Children.Add(new System.Windows.Controls.TextBlock { Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        foreach (System.Windows.Controls.TextBlock block in text.Children)
            block.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                                       ReferenceEquals(block, text.Children[0]) ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        // Campos propios (contraseña, opciones de formato…) debajo del texto.
        if (content is FrameworkElement extra)
        {
            extra.Margin = new Thickness(0, 16, 0, 0);
            text.Children.Add(extra);
        }

        // Botones a lo ancho, como los de Windows 11: el primero con el acento.
        var row = new UniformGrid { Rows = 1, Margin = new Thickness(16, 14, 16, 16) };
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var button = new Wpf.Ui.Controls.Button
            {
                Content = buttons[i],
                Appearance = i == 0 ? ControlAppearance.Primary : ControlAppearance.Secondary,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(12, 7, 12, 7),
                IsDefault = i == 0,
                IsCancel = i == buttons.Count - 1 && buttons.Count > 1,
            };
            // Lo que pide permiso de administrador lleva el escudo, como en Windows.
            if (i == 0 && elevate)
                button.Icon = new SymbolIcon(SymbolRegular.Shield24);
            // El último es "Cancelar": cuenta como no elegir nada.
            button.Click += (_, _) => Finish(index == buttons.Count - 1 && buttons.Count > 1 ? -1 : index);
            row.Children.Add(button);
        }
        var footer = new Border { Child = row };
        footer.SetResourceReference(Border.BackgroundProperty, "StrataSurfaceBrush");

        var close = new Wpf.Ui.Controls.Button
        {
            Icon = new SymbolIcon(SymbolRegular.Dismiss16),
            Appearance = ControlAppearance.Transparent,
            BorderThickness = new Thickness(0),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
        };
        close.Click += (_, _) => Finish(-1);
        System.Windows.Automation.AutomationProperties.SetName(close, Services.Localization.Loc.T("Cerrar"));

        _root = new Grid();
        _root.SetResourceReference(Panel.BackgroundProperty, "StrataHubTintBrush");
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Children.Add(text);
        _root.Children.Add(close);
        Grid.SetRow(footer, 1);
        _root.Children.Add(footer);
        Content = _root;

        // Sin barra de título: se arrastra desde cualquier parte que no sea un botón.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source || FindButton(source) is null)
                DragMove();
        };
        Loaded += (_, _) =>
        {
            DialogPlacement.Place(this);
            Motion.In(_root, _root, TaskbarPosition.Bottom, 0.5);
        };
        // Mientras pregunta, la ventana de la que salió no responde (como un diálogo de Windows). Con
        // EnableWindow y no IsEnabled, que la pintaría toda gris.
        if (owner != null && new System.Windows.Interop.WindowInteropHelper(owner).Handle is var ownerHandle && ownerHandle != IntPtr.Zero)
        {
            EnableWindow(ownerHandle, false);
            Closing += (_, _) => EnableWindow(ownerHandle, true); // antes de cerrar: si no, Windows activa otra app
            Closed += (_, _) => owner.Activate();
        }
        Closed += (_, _) => _result.TrySetResult(_choice);
    }

    private static System.Windows.Controls.Primitives.ButtonBase? FindButton(DependencyObject element)
    {
        for (var current = element; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase button)
                return button;
        return null;
    }

    private void Finish(int choice)
    {
        _choice = choice;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Finish(-1);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hwnd, bool enable);

    /// <summary>
    /// Muestra el diálogo y devuelve el botón elegido (0 = el primero); -1 si se canceló (el último
    /// botón, cuando hay más de uno), con Esc o al cerrarlo.
    /// </summary>
    public static Task<int> ShowAsync(Window? owner, string title, string message, params string[] buttons)
    {
        var dialog = new StrataDialog(owner, title, message, buttons, null, elevate: false);
        dialog.Show();
        dialog.Activate();
        return dialog._result.Task;
    }

    /// <summary>
    /// Con campos propios debajo del mensaje (<paramref name="content"/>, que se lee al volver) y, si
    /// <paramref name="elevate"/>, el escudo de administrador en el primer botón.
    /// </summary>
    public static Task<int> ShowAsync(Window? owner, string title, string message, UIElement? content, bool elevate, params string[] buttons)
    {
        var dialog = new StrataDialog(owner, title, message, buttons, content, elevate);
        if (content != null)
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() => FocusFirst(content), System.Windows.Threading.DispatcherPriority.Input);
        dialog.Show();
        dialog.Activate();
        return dialog._result.Task;
    }

    /// <summary>El primer campo para escribir recibe el foco (la contraseña, el nombre).</summary>
    private static bool FocusFirst(DependencyObject root)
    {
        if (root is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox)
            return ((UIElement)root).Focus();
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FocusFirst(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                return true;
        }
        return false;
    }

    /// <summary>Pedir permiso de administrador (con el escudo en "Continuar"): true si se eligió seguir.</summary>
    public static async Task<bool> ConfirmAdminAsync(Window? owner, string title, string message) =>
        await ShowAsync(owner, title, message, null, true,
                        Services.Localization.Loc.T("Continuar"), Services.Localization.Loc.T("Cancelar")) == 0;

    /// <summary>Confirmar una acción: true si se eligió <paramref name="action"/>.</summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string action) =>
        await ShowAsync(owner, title, message, action, Services.Localization.Loc.T("Cancelar")) == 0;
}
