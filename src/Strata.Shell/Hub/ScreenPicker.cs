using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Hub;

/// <summary>
/// Elige el monitor que editan las páginas de Personalización por monitor. La elección es la misma
/// en todas las páginas (se recuerda mientras el Hub está abierto) y por defecto es el principal de
/// Strata UI.
/// </summary>
public sealed class ScreenPicker : UserControl
{
    private static string? _device;

    /// <summary>Monitor elegido (si ya no está conectado, el principal de Strata UI).</summary>
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
            name += " · " + Loc.T("principal de Strata UI");
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

        // Una línea: "Ajustes de [monitor]". Con un solo monitor no hace falta elegir.
        var label = new TextBlock
        {
            Text = Loc.T("Ajustes de"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Desktop24,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        row.Children.Add(label);
        row.Children.Add(_box);
        row.Loaded += (_, _) => label.Style = (Style)FindResource("CardTitle");

        Content = row;
        Margin = new Thickness(0, 0, 0, 8);
        ToolTip = Loc.T("Estos ajustes son solo de ese monitor. El menú Inicio es igual en todos.");
        Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            // Dentro de una pestaña, el de su entrada vale para todas.
            if (TabbedPage.IsEmbedded(this))
                Visibility = Visibility.Collapsed;
        };
        // Si se eligió en otra página, este también se pone al día.
        Changed += Sync;
        Unloaded += (_, _) => Changed -= Sync;
    }

    private void Sync() =>
        _box.SelectedItem = _box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Device) ?? _box.SelectedItem;
}
