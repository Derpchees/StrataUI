using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Material del tema en una ventana FluentWindow (Hub, Archivos), también al cambiarlo con la
/// ventana abierta. El desenfoque limpio (tema Transparente total) lo pone la ventana: WPF-UI pinta
/// su fondo sólido y se deja transparente.
/// </summary>
public static class FluentBackdrop
{
    public static void Follow(FluentWindow window)
    {
        bool blurred = false;
        void Apply()
        {
            bool blur = ThemeManager.Theme.Backdrop == Backdrop.Blur;
            if (blur)
            {
                window.WindowBackdropType = WindowBackdropType.None;
                window.Background = Brushes.Transparent;
                if (window.IsLoaded)
                    WindowEffects.SetBlur(window, Backdrop.Blur);
                else
                    window.SourceInitialized += (_, _) => WindowEffects.SetBlur(window, Backdrop.Blur);
            }
            else
            {
                if (blurred)
                {
                    WindowEffects.SetBlur(window, Backdrop.Solid);
                    window.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                }
                window.WindowBackdropType = ThemeManager.WindowBackdrop;
            }
            blurred = blur;
        }
        Apply();
        ThemeManager.ThemeChanged += Apply;
        window.Closed += (_, _) => ThemeManager.ThemeChanged -= Apply;
    }
}
