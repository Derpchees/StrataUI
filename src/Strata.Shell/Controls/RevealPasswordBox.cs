using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace Strata.Shell.Controls;

/// <summary>
/// Contraseña como en el teléfono: puntos, pero el último carácter escrito se ve un momento, y un ojo
/// la muestra entera para revisarla. Un TextBox que guarda la contraseña aparte y muestra la máscara
/// (PasswordBox no deja ver nada). No se puede copiar ni cortar mientras está oculta.
/// </summary>
public sealed class RevealPasswordBox : Grid
{
    private const char Dot = '●';
    private static readonly TimeSpan PeekTime = TimeSpan.FromSeconds(1);

    private readonly TextBox _box = new() { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 6, 36, 6) };
    private readonly Wpf.Ui.Controls.Button _eye;
    private readonly DispatcherTimer _peek = new() { Interval = PeekTime };
    private string _password = "";
    private int _peekIndex = -1;
    private bool _revealed;

    public RevealPasswordBox()
    {
        Children.Add(_box);
        _eye = new Wpf.Ui.Controls.Button
        {
            Appearance = ControlAppearance.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(0),
            Focusable = false,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Eye16 },
            ToolTip = Loc.T("Mostrar contraseña"),
        };
        _eye.Click += (_, _) => Reveal(!_revealed);
        Children.Add(_eye);

        _peek.Tick += (_, _) =>
        {
            _peek.Stop();
            _peekIndex = -1;
            Render(_box.CaretIndex);
        };
        _box.PreviewTextInput += OnTextInput;
        _box.PreviewKeyDown += OnKeyDown;
        CommandManager.AddPreviewExecutedHandler(_box, OnCommand);
        CommandManager.AddPreviewCanExecuteHandler(_box, OnCanCommand);
        DataObject.AddPastingHandler(_box, (_, e) => e.CancelCommand()); // el pegado va por OnCommand
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (!IsKeyboardFocusWithin)
                HidePeek();
        };
    }

    public string Password => _password;

    /// <summary>Solo pruebas: el cuadro de texto y el ojo.</summary>
    internal TextBox Editor => _box;
    internal void ToggleReveal() => Reveal(!_revealed);

    public new bool Focus() => _box.Focus();

    /// <summary>Muestra u oculta la contraseña entera.</summary>
    private void Reveal(bool show)
    {
        _revealed = show;
        ((SymbolIcon)_eye.Icon!).Symbol = show ? SymbolRegular.EyeOff16 : SymbolRegular.Eye16;
        _eye.ToolTip = show ? Loc.T("Ocultar contraseña") : Loc.T("Mostrar contraseña");
        HidePeek();
        _box.Focus();
    }

    private void HidePeek()
    {
        _peek.Stop();
        _peekIndex = -1;
        Render(_box.CaretIndex);
    }

    private void Replace(string text)
    {
        int start = _box.SelectionStart, length = _box.SelectionLength;
        _password = _password.Remove(start, length).Insert(start, text);
        int caret = start + text.Length;
        // Solo al escribir de a un carácter se ve el último; pegado, no.
        _peekIndex = !_revealed && text.Length == 1 ? caret - 1 : -1;
        _peek.Stop();
        if (_peekIndex >= 0)
            _peek.Start();
        Render(caret);
    }

    private void Render(int caret)
    {
        var shown = _revealed
            ? _password
            : string.Create(_password.Length, (_password, _peekIndex), static (span, state) =>
            {
                span.Fill(Dot);
                if (state._peekIndex >= 0 && state._peekIndex < state._password.Length)
                    span[state._peekIndex] = state._password[state._peekIndex];
            });
        _box.Text = shown;
        _box.CaretIndex = Math.Clamp(caret, 0, shown.Length);
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
            Replace(e.Text);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        int start = _box.SelectionStart, length = _box.SelectionLength;
        switch (e.Key)
        {
            case Key.Back when length > 0 || start > 0:
                if (length == 0)
                    _box.Select(start - 1, 1);
                Replace("");
                e.Handled = true;
                break;
            case Key.Delete when length > 0 || start < _password.Length:
                if (length == 0)
                    _box.Select(start, 1);
                Replace("");
                e.Handled = true;
                break;
            case Key.Back or Key.Delete:
                e.Handled = true;
                break;
            case Key.Space:
                // Espacio no llega como TextInput cuando lo maneja el TextBox antes.
                Replace(" ");
                e.Handled = true;
                break;
        }
    }

    private void OnCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Paste)
        {
            if (Clipboard.ContainsText())
                Replace(Clipboard.GetText().Replace("\r", "").Replace("\n", ""));
            e.Handled = true;
        }
        // El texto del cuadro es la máscara: cortar o deshacer lo desparejaría de la contraseña, y
        // copiar solo se permite con la contraseña a la vista.
        else if (e.Command == ApplicationCommands.Cut || e.Command == ApplicationCommands.Undo
                 || e.Command == ApplicationCommands.Redo || e.Command == ApplicationCommands.Copy && !_revealed)
            e.Handled = true;
    }

    private void OnCanCommand(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Cut || e.Command == ApplicationCommands.Copy)
        {
            e.CanExecute = e.Command == ApplicationCommands.Copy && _revealed;
            e.Handled = true;
        }
    }
}
