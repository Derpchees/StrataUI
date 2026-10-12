using Winx.Shell.Hub.Pages;
using Winx.Shell.Services;
using Wpf.Ui.Controls;

namespace Winx.Shell.Hub;

/// <summary>
/// Ventana central de configuración de WINX.
/// Las páginas declaran ScrollViewer.CanContentScroll="False" y traen su propio ScrollViewer:
/// así WPF-UI no las envuelve en uno con altura infinita (que impedía desplazar con la rueda).
/// </summary>
public partial class HubWindow : FluentWindow
{
    private Type _startPage = typeof(HomePage);

    public HubWindow()
    {
        InitializeComponent();
        ApplyBackdrop();
        ThemeManager.ThemeChanged += ApplyBackdrop;
        Closed += (_, _) => ThemeManager.ThemeChanged -= ApplyBackdrop;
        Loaded += (_, _) => Nav.Navigate(_startPage);
    }

    private void ApplyBackdrop() => WindowBackdropType = ThemeManager.WindowBackdrop;

    /// <summary>Navega a una página por nombre: "Privacy", "PrivacyPage", "Apps"…</summary>
    public void NavigateTo(string page)
    {
        var type = typeof(TaskbarPage).Assembly.GetTypes().FirstOrDefault(t =>
            t.Namespace == typeof(TaskbarPage).Namespace && !t.IsAbstract &&
            (t.Name.Equals(page, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Equals(page + "Page", StringComparison.OrdinalIgnoreCase)));
        if (type is null)
            return;
        _startPage = type;
        if (IsLoaded)
            Nav.Navigate(type);
    }
}
