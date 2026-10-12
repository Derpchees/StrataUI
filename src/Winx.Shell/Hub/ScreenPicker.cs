using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Hub;

/// <summary>
/// Elige el monitor que editan las páginas de Personalización por monitor. La elección es la misma
/// en todas las páginas (se recuerda mientras el Hub está abierto) y por defecto es el principal de
/// WINX. "Copiar a todos" pone los ajustes de este monitor en los demás.
/// </summary>
public sealed class ScreenPicker : UserControl
{
    private static string? _device;

    /// <summary>Monitor elegido (si ya no está conectado, el principal de WINX).</summary>
    public static string Device
    {
        get
        {
            var screens = Forms.Screen.AllScreens;
            return _device != null && screens.Any(s => s.DeviceName == _device) ? _device : ThemeManager.MainDevice;
        }
    }

    /// <summary>Cambió el monitor elegido: las páginas vuelven a leer sus ajustes.</summary>
    public static event Action? Changed;

    /// <summary>Monitores de izquierda a derecha (así se numeran en todo el Hub).</summary>
    public static List<Forms.Screen> Screens() =>
        Forms.Screen.AllScreens.OrderBy(sc => sc.Bounds.X).ThenBy(sc => sc.Bounds.Y).ToList();

    public static string NameOf(Forms.Screen screen, int index, int count)
    {
        string name = count == 1 ? Loc.T("Monitor") : Loc.F("Monitor {0}", index + 1);
        if (screen.DeviceName == ThemeManager.MainDevice && count > 1)
            name += " · " + Loc.T("principal de WINX");
        return $"{name} ({screen.Bounds.Width} × {screen.Bounds.Height})";
    }

    private readonly ComboBox _box = new() { MinWidth = 280 };

    public ScreenPicker()
    {
        var screens = Screens();
        for (int i = 0; i < screens.Count; i++)
            _box.Items.Add(new ComboBoxItem { Content = NameOf(screens[i], i, screens.Count), Tag = screens[i].DeviceName });
        _box.SelectedItem = _box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Device) ?? _box.Items[0];
        _box.SelectionChanged += (_, _) =>
        {
            if (_box.SelectedItem is ComboBoxItem { Tag: string device } && device != Device)
            {
                _device = device;
                Changed?.Invoke();
            }
        };

        var copy = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("Copiar a todos"),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.CopySelect20 },
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = Loc.T("Pone los ajustes de este monitor en todos los demás"),
            Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
        };
        copy.Click += (_, _) => CopyToAll();

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(_box);
        controls.Children.Add(copy);

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Loc.T("Monitor que estás personalizando") });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("Estos ajustes son solo de ese monitor. El menú Inicio es igual en todos."),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        header.Loaded += (_, _) =>
        {
            ((TextBlock)header.Children[0]).Style = (Style)FindResource("CardTitle");
            ((TextBlock)header.Children[1]).Style = (Style)FindResource("CardDescription");
        };

        Content = new Wpf.Ui.Controls.CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(18, 14, 18, 14),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Desktop24 },
            Header = header,
            Content = controls,
        };
        // Si se eligió en otra página, este también se pone al día.
        Changed += Sync;
        Unloaded += (_, _) => Changed -= Sync;
    }

    private void Sync() =>
        _box.SelectedItem = _box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Device) ?? _box.SelectedItem;

    /// <summary>Los ajustes y los elementos de las barras de este monitor, en todos los demás.</summary>
    private static void CopyToAll()
    {
        string from = Device;
        Settings.Update(s =>
        {
            var source = s.For(from);
            foreach (var screen in Forms.Screen.AllScreens.Where(sc => sc.DeviceName != from))
                s.Screens[screen.DeviceName] = source.Clone();
        });
    }
}
