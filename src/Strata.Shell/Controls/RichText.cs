using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Emoji;

namespace Strata.Shell.Controls;

/// <summary>
/// Texto de una notificación con sus links (se abren con un clic) y sus emojis a color
/// (<see cref="EmojiRenderer"/>). En XAML: <c>controls:RichText.Text="{Binding Body}"</c> en un
/// TextBlock, en lugar de <c>Text</c>.
/// </summary>
public static class RichText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(RichText), new PropertyMetadata(null, (d, e) =>
        {
            if (d is TextBlock block)
                Fill(block, e.NewValue as string ?? "");
        }));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public enum Kind { Text, Link, Emoji }

    private static void Fill(TextBlock block, string text)
    {
        block.Inlines.Clear();
        // El emoji un poco más grande que la letra, como en Windows, centrado en la línea.
        double size = Math.Round(block.FontSize * 1.25);
        foreach (var (kind, value) in Split(text))
        {
            switch (kind)
            {
                case Kind.Link:
                {
                    var link = new Hyperlink(new Run(value)) { ToolTip = value };
                    link.SetResourceReference(TextElement.ForegroundProperty, "StrataAccentBrush");
                    link.Click += (_, e) =>
                    {
                        e.Handled = true;
                        Open(value);
                    };
                    block.Inlines.Add(link);
                    break;
                }
                case Kind.Emoji when EmojiRenderer.Get(value) is { } image:
                    block.Inlines.Add(new InlineUIContainer(new Image
                    {
                        Source = image,
                        Width = size,
                        Height = size,
                        ToolTip = null,
                    })
                    { BaselineAlignment = BaselineAlignment.Center });
                    break;
                default:
                    block.Inlines.Add(new Run(value));
                    break;
            }
        }
    }

    private static void Open(string link)
    {
        try
        {
            string url = link.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + link : link;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {link}", ex);
        }
    }

    // Termina antes de la puntuación final ("…en https://x.com/a." no lleva el punto).
    private static readonly Regex Url = new(@"\b(?:https?://|www\.)[^\s<>""]+[^\s<>"".,;:!?)\]'’”]",
                                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Parte el texto en tramos de texto, links y emojis (cada emoji, con sus modificadores, en uno).</summary>
    public static List<(Kind Kind, string Value)> Split(string text)
    {
        var parts = new List<(Kind, string)>();
        int at = 0;
        foreach (Match link in Url.Matches(text))
        {
            AddText(parts, text[at..link.Index]);
            parts.Add((Kind.Link, link.Value));
            at = link.Index + link.Length;
        }
        AddText(parts, text[at..]);
        return parts;
    }

    private static void AddText(List<(Kind, string)> parts, string text)
    {
        var plain = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (IsEmoji(element))
            {
                if (plain.Length > 0)
                {
                    parts.Add((Kind.Text, plain.ToString()));
                    plain.Clear();
                }
                parts.Add((Kind.Emoji, element));
            }
            else
            {
                plain.Append(element);
            }
        }
        if (plain.Length > 0)
            parts.Add((Kind.Text, plain.ToString()));
    }

    /// <summary>
    /// Si un carácter (con sus modificadores) se ve como emoji: los de los planos de emojis, los
    /// que piden verse como emoji (U+FE0F), los números de tecla (1️⃣) y los símbolos que Unicode
    /// muestra como emoji por defecto (⭐, ✅, ⚡…). "✓" o "©" solos siguen siendo texto.
    /// </summary>
    public static bool IsEmoji(string element)
    {
        if (element.Length == 0 || !Rune.TryGetRuneAt(element, 0, out var first))
            return false;
        if (element.Contains('⃣'))
            return true; // tecla: 1️⃣ #️⃣
        if (first.Value is >= '0' and <= '9' or '#' or '*')
            return false;
        if (element.Contains('️'))
            return true;
        int c = first.Value;
        return c is >= 0x1F000 and <= 0x1FAFF || DefaultEmoji.Contains(c);
    }

    // Emoji_Presentation fuera de los planos de emojis (Unicode 15).
    private static readonly HashSet<int> DefaultEmoji =
    [
        0x231A, 0x231B, 0x23E9, 0x23EA, 0x23EB, 0x23EC, 0x23F0, 0x23F3, 0x25FD, 0x25FE, 0x2614, 0x2615,
        0x2648, 0x2649, 0x264A, 0x264B, 0x264C, 0x264D, 0x264E, 0x264F, 0x2650, 0x2651, 0x2652, 0x2653,
        0x267F, 0x2693, 0x26A1, 0x26AA, 0x26AB, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26CE, 0x26D4, 0x26EA,
        0x26F2, 0x26F3, 0x26F5, 0x26FA, 0x26FD, 0x2705, 0x270A, 0x270B, 0x2728, 0x274C, 0x274E, 0x2753,
        0x2754, 0x2755, 0x2757, 0x2795, 0x2796, 0x2797, 0x27B0, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B55,
    ];

    /// <summary>
    /// Si lo pulsado está dentro de un link (los textos no son visuales: se sube por el árbol lógico).
    /// Para que el clic en el link no active también la notificación.
    /// </summary>
    public static bool IsInsideLink(DependencyObject? source)
    {
        for (var current = source; current != null;
             current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is Hyperlink)
                return true;
        }
        return false;
    }
}
