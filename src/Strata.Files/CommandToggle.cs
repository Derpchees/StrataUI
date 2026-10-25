using System.Windows;
using System.Windows.Controls.Primitives;

namespace Strata.Shell.Files;

/// <summary>
/// Botón de alternar que avisa Click también cuando lo alterna la automatización (lectores de
/// pantalla, UIA): ToggleButton solo lo avisa con el mouse o el teclado, y los manejadores de
/// Click de la ventana no se enteraban.
/// </summary>
public sealed class CommandToggle : ToggleButton
{
    private bool _clicking;

    protected override void OnClick()
    {
        _clicking = true;
        try
        {
            base.OnClick();
        }
        finally
        {
            _clicking = false;
        }
    }

    protected override void OnToggle()
    {
        base.OnToggle();
        if (!_clicking)
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }
}
