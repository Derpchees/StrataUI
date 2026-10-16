using System.Windows;
using System.Windows.Input;
using Strata.Shell.Controls;

namespace Strata.Tests;

/// <summary>La contraseña de Wi-Fi: puntos, el último carácter a la vista un momento y el ojo.</summary>
public sealed class RevealPasswordBoxTests
{
    /// <summary>Corre en un hilo STA con el control dentro de una ventana fuera de la pantalla.</summary>
    private static void WithBox(Action<RevealPasswordBox, Window> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var window = new Window { Left = -32000, Top = -32000, Width = 300, Height = 80, ShowActivated = false };
            try
            {
                var box = new RevealPasswordBox();
                window.Content = box;
                window.Show();
                test(box, window);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                window.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static void Type(RevealPasswordBox box, string text)
    {
        foreach (char c in text)
            box.Editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                new TextComposition(InputManager.Current, box.Editor, c.ToString()))
            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
    }

    private static void Press(RevealPasswordBox box, Window window, Key key) =>
        box.Editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    [Fact]
    public void Shows_dots_and_the_last_character_typed()
    {
        WithBox((box, _) =>
        {
            Type(box, "abc");
            Assert.Equal("abc", box.Password);
            Assert.Equal("●●c", box.Editor.Text);
        });
    }

    [Fact]
    public void The_eye_shows_the_whole_password_and_hides_it_again()
    {
        WithBox((box, _) =>
        {
            Type(box, "clave");
            box.ToggleReveal();
            Assert.Equal("clave", box.Editor.Text);
            box.ToggleReveal();
            Assert.Equal("●●●●●", box.Editor.Text);
            Assert.Equal("clave", box.Password);
        });
    }

    [Fact]
    public void Backspace_and_editing_in_the_middle_keep_the_real_password()
    {
        WithBox((box, window) =>
        {
            Type(box, "abcd");
            Press(box, window, Key.Back);
            Assert.Equal("abc", box.Password);
            box.Editor.CaretIndex = 1;
            Type(box, "X");
            Assert.Equal("aXbc", box.Password);
            Assert.Equal("●X●●", box.Editor.Text);
            box.Editor.Select(0, 2);
            Press(box, window, Key.Delete);
            Assert.Equal("bc", box.Password);
            Press(box, window, Key.Space);
            Assert.Equal(" bc", box.Password);
        });
    }
}
