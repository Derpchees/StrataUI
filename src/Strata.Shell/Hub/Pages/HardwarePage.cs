using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Vendors;
using Strata.Shell.Tweaks;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Optimizaciones según el fabricante: el equipo detectado, los ajustes globales del controlador de
/// NVIDIA (si hay una tarjeta NVIDIA) y los servicios de AMD, NVIDIA e Intel que estén instalados.
/// </summary>
public sealed class HardwarePage() : TweaksPage(TweakCatalog.Hardware, Loc.T("Hardware"),
    Loc.T("Ajustes del controlador de tu tarjeta gráfica y servicios de los fabricantes."), SymbolRegular.DeveloperBoard24)
{
    private readonly Dictionary<uint, ComboBox> _nvidiaBoxes = new();
    private Dictionary<uint, uint?> _nvidiaValues = new();
    private TextBlock? _nvidiaStatus;
    private bool _syncing;

    protected override IEnumerable<UIElement> ExtraContent()
    {
        yield return Section(Loc.T("Tu equipo"));
        var machine = new StackPanel();
        machine.Children.Add(Line(SymbolRegular.DeveloperBoard24, HardwareInfo.CpuName ?? Loc.T("Procesador desconocido")));
        foreach (var gpu in HardwareInfo.GpuNames)
            machine.Children.Add(Line(SymbolRegular.Gauge24, gpu));
        yield return Panel(machine);

        if (NvidiaSettings.Available)
        {
            yield return Section(Loc.T("NVIDIA · Configuración global del controlador"));
            _nvidiaStatus = new TextBlock { Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                                            Text = Loc.T("Los mismos ajustes que el Panel de control de NVIDIA. Cambiarlos pide permiso de administrador.") };
            yield return _nvidiaStatus;
            foreach (var setting in NvidiaSettings.All)
                yield return NvidiaCard(setting);
            var restore = new Wpf.Ui.Controls.Button { Content = Loc.T("Dejar como estaba"), Margin = new Thickness(0, 4, 0, 12) };
            restore.Click += async (_, _) =>
            {
                restore.IsEnabled = false;
                bool ok = await NvidiaSettings.RestoreAsync();
                SetStatus(ok ? Loc.T("Se restauraron los valores de antes.") : Loc.T("No se pudieron restaurar (¿se canceló el permiso?)."));
                await ReadNvidiaAsync();
                restore.IsEnabled = true;
            };
            yield return restore;
            Loaded += async (_, _) => await ReadNvidiaAsync();
        }

        if (!NvidiaSettings.Available && !TweakCatalog.All.Any(t => t.Category == TweakCatalog.Hardware && t.IsAvailable))
            yield return new TextBlock { Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap,
                                         Text = Loc.T("No hay ajustes de fabricante para este equipo.") };
        else if (TweakCatalog.All.Any(t => t.Category == TweakCatalog.Hardware && t.IsAvailable))
            yield return Section(Loc.T("Servicios de los fabricantes"));
    }

    private CardControl NvidiaCard(NvidiaSetting setting)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = setting.Title });
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = setting.Description, TextWrapping = TextWrapping.Wrap });
        var box = new ComboBox { MinWidth = 200, IsEnabled = false };
        foreach (var (name, _) in setting.Options)
            box.Items.Add(name);
        box.SelectionChanged += async (_, _) =>
        {
            if (_syncing || box.SelectedIndex < 0 || box.SelectedIndex >= setting.Options.Length)
                return;
            var value = setting.Options[box.SelectedIndex].Value;
            var current = _nvidiaValues.GetValueOrDefault(setting.Id);
            if (value == current)
                return;
            box.IsEnabled = false;
            bool ok = await NvidiaSettings.SetAsync(setting, value, current);
            SetStatus(ok ? Loc.T("Listo. Los juegos que estén abiertos lo toman al reiniciarlos.")
                         : Loc.T("No se pudo cambiar (¿se canceló el permiso?)."));
            await ReadNvidiaAsync();
        };
        _nvidiaBoxes[setting.Id] = box;
        return new CardControl { Style = (Style)FindResource("HubCard"), Header = header, Content = box };
    }

    private async Task ReadNvidiaAsync()
    {
        var values = await NvidiaSettings.ReadAsync();
        if (values is null)
        {
            SetStatus(Loc.T("No se pudo leer la configuración del controlador de NVIDIA."));
            return;
        }
        _nvidiaValues = values;
        _syncing = true;
        foreach (var setting in NvidiaSettings.All)
        {
            var box = _nvidiaBoxes[setting.Id];
            var value = values.GetValueOrDefault(setting.Id);
            int index = Array.FindIndex(setting.Options, o => o.Value == value);
            // Un valor que no está entre las opciones (cambiado en el Panel de NVIDIA) se muestra aparte.
            while (box.Items.Count > setting.Options.Length)
                box.Items.RemoveAt(box.Items.Count - 1);
            if (index < 0)
            {
                box.Items.Add(Loc.T("Otro (elegido en el Panel de NVIDIA)"));
                index = box.Items.Count - 1;
            }
            box.SelectedIndex = index;
            box.IsEnabled = true;
        }
        _syncing = false;
    }

    private void SetStatus(string text)
    {
        if (_nvidiaStatus != null)
            _nvidiaStatus.Text = text;
    }

    private TextBlock Section(string text) =>
        new() { Style = (Style)FindResource("HubSectionTitle"), Text = text };

    private FrameworkElement Line(SymbolRegular icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new SymbolIcon { Symbol = icon, FontSize = 18, Margin = new Thickness(0, 0, 12, 0) });
        row.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private Border Panel(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(18, 12, 18, 12),
        Margin = new Thickness(0, 0, 0, 12),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
    };
}
