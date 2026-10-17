using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.StartMenu;

namespace Strata.Shell.StartMenu;

/// <summary>
/// Submenú "Ver" del clic derecho en el menú Inicio: tamaño de los íconos (deslizador, se ve al
/// moverlo) y nombres de las apps ancladas. Antes estaba en Hub › Menú Inicio.
/// </summary>
internal static class StartViewMenu
{
    private static DispatcherTimer? _save;
    private static double _pending;

    public static MenuItem Create()
    {
        var view = new MenuItem { Header = Loc.T("Ver"), Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Eye24 } };

        var label = new System.Windows.Controls.TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        var slider = new Slider
        {
            Minimum = StartMenuService.MinIconPixels,
            Maximum = StartMenuService.MaxIconPixels,
            SmallChange = 2,
            LargeChange = 8,
            TickFrequency = 2,
            IsSnapToTickEnabled = true,
            Width = 220,
            Focusable = false,
        };
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(label);
        panel.Children.Add(slider);
        // El deslizador vive en el menú: el clic no debe cerrarlo ni resaltar la fila.
        var size = new MenuItem { Header = panel, StaysOpenOnClick = true };
        view.Items.Add(size);

        var labels = new MenuItem { Header = Loc.T("Mostrar nombres de las apps ancladas"), IsCheckable = true, StaysOpenOnClick = true };
        labels.Click += (_, _) => Settings.Update(s => s.StartShowPinnedLabels = labels.IsChecked);
        view.Items.Add(labels);

        bool loading = false;
        slider.ValueChanged += (_, e) =>
        {
            label.Text = Loc.F("Tamaño de los íconos: {0} px", Math.Round(e.NewValue));
            if (loading)
                return;
            // En vivo sin guardar; se guarda cuando deja de moverse.
            StartMenuService.ApplyIconSizes(e.NewValue);
            Save(e.NewValue);
        };
        view.SubmenuOpened += (_, _) =>
        {
            loading = true;
            slider.Value = StartMenuService.IconPixels(Settings.Current);
            label.Text = Loc.F("Tamaño de los íconos: {0} px", Math.Round(slider.Value));
            labels.IsChecked = Settings.Current.StartShowPinnedLabels;
            loading = false;
        };
        return view;
    }

    private static void Save(double pixels)
    {
        _pending = pixels;
        if (_save is null)
        {
            _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _save.Tick += (_, _) =>
            {
                _save.Stop();
                Settings.Update(s => s.StartIconPixels = _pending);
            };
        }
        _save.Stop();
        _save.Start();
    }
}
