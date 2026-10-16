using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Hub › Colores: el acento (de Windows, del fondo o propio), colores propios para la barra y los
/// paneles encima del tema, y el fondo de pantalla.
/// </summary>
public partial class ColorsPage : Page
{
    // Paleta de acentos de Windows.
    private static readonly string[] AccentPalette =
    [
        "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343",
        "#E74856", "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089",
        "#0078D4", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798",
        "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
        "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10",
    ];

    // Colores para superficies: neutros y tonos oscuros profundos, más el acento.
    private static readonly string[] SurfacePalette =
    [
        "#000000", "#121212", "#1C1C1C", "#2B2B2B", "#3A3A3A", "#E6E6E6", "#F3F3F3", "#FFFFFF",
        "#1E2A3A", "#2D1B3D", "#1B3A2F", "#3A1B1B", "#3A2F1B", "#1B2F3A",
    ];

    private bool _loading = true;

    public ColorsPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        Select(AccentSourceBox, s.AccentFromWallpaper ? "Wallpaper" : s.AccentColor is null ? "Windows" : "Custom");
        _loading = false;
        BuildSwatches();
        UpdateSwatches();
        // El color de cada barra está en Barra de tareas (es de cada monitor).
        CustomColors.Children.Add(CustomColorCard(this, Loc.T("Paneles y menú Inicio"), Loc.T("Fondo de los paneles, el menú Inicio y el centro de notificaciones."),
            SymbolRegular.PanelRight24, () => Settings.Current.CustomPanelColor, (st, value) => st.CustomPanelColor = value));
        UpdateWallpaper();

        // Wallpaper Engine pudo abrirse, cerrarse o cambiar su opción mientras el Hub estaba cerrado.
        WallpaperEngine.Recheck();
        UpdateWallpaperEngine();
        WallpaperEngine.Changed += UpdateWallpaperEngine;
        Unloaded += (_, _) => WallpaperEngine.Changed -= UpdateWallpaperEngine;
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    private void UpdateWallpaperEngine()
    {
        bool blocked = WallpaperEngine.AdjustingWindowsColor;
        WallpaperEngineBar.IsOpen = blocked;
        WallpaperAccentItem.IsEnabled = !blocked;
        WallpaperAccentItem.Content = blocked ? Loc.T("Del fondo de pantalla (en pausa)") : Loc.T("Del fondo de pantalla");
    }

    // ── Acento ──────────────────────────────────────────────────────────────

    private void BuildSwatches()
    {
        foreach (var hex in AccentPalette)
        {
            var swatch = Swatch(hex, 36);
            swatch.Click += (_, _) =>
            {
                Settings.Update(s =>
                {
                    s.AccentColor = hex;
                    s.AccentFromWallpaper = false;
                });
                UpdateSwatches();
            };
            Swatches.Children.Add(swatch);
        }
    }

    private void UpdateSwatches()
    {
        var s = Settings.Current;
        var current = s.AccentFromWallpaper ? null : s.AccentColor;
        SwatchPanel.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        foreach (System.Windows.Controls.Button swatch in Swatches.Children)
            Mark(this, swatch, string.Equals((string)swatch.Tag, current, StringComparison.OrdinalIgnoreCase));
    }

    private void AccentSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentSourceBox.SelectedItem is not ComboBoxItem { Tag: string source })
            return;
        // Al pasar a color propio se parte del acento que se ve ahora.
        var shown = ThemeManager.Accent;
        Settings.Update(s =>
        {
            s.AccentFromWallpaper = source == "Wallpaper";
            if (source == "Windows")
                s.AccentColor = null;
            else if (source == "Custom" && s.AccentColor is null)
                s.AccentColor = $"#{shown.R:X2}{shown.G:X2}{shown.B:X2}";
        });
        UpdateSwatches();
    }

    // ── Colores propios ─────────────────────────────────────────────────────

    /// <summary>
    /// Tarjeta de un color propio: interruptor y, si está activo, muestras, código y opacidad. Se
    /// guarda como "#AARRGGBB" (null = el del tema). Sin <paramref name="toggle"/>, el editor siempre a
    /// la vista (lo muestra u oculta quien lo usa, como el estilo de cada barra en Barra de tareas).
    /// </summary>
    internal static FrameworkElement CustomColorCard(FrameworkElement host, string title, string description, SymbolRegular icon,
                                                     Func<string?> read, Action<StrataSettings, string?> write, bool toggle = true)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)host.FindResource("CardTitle"), Text = title });
        header.Children.Add(new TextBlock { Style = (Style)host.FindResource("CardDescription"), Text = description, TextWrapping = TextWrapping.Wrap });
        var toggleSwitch = new ToggleSwitch { IsChecked = read() != null };
        var card = new CardControl
        {
            Style = (Style)host.FindResource("HubCard"),
            Header = header,
            Content = toggle ? toggleSwitch : null,
            Icon = new SymbolIcon { Symbol = icon },
        };

        var editor = new StackPanel();
        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var hexBox = new System.Windows.Controls.TextBox { Width = 110, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var opacity = new Slider { Minimum = 20, Maximum = 100, Width = 200, VerticalAlignment = VerticalAlignment.Center,
                                   IsSnapToTickEnabled = true, TickFrequency = 5 };
        var opacityText = new TextBlock { Style = (Style)host.FindResource("CardDescription"), Width = 48, VerticalAlignment = VerticalAlignment.Center,
                                          Margin = new Thickness(8, 0, 0, 0) };
        var preview = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0),
                                   BorderThickness = new Thickness(1), BorderBrush = (Brush)host.FindResource("CardStrokeColorDefaultBrush") };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(preview);
        row.Children.Add(new TextBlock { Style = (Style)host.FindResource("CardDescription"), Text = Loc.T("Código"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(hexBox);
        row.Children.Add(new TextBlock { Style = (Style)host.FindResource("CardDescription"), Text = Loc.T("Opacidad"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(opacity);
        row.Children.Add(opacityText);
        editor.Children.Add(swatches);
        editor.Children.Add(row);
        var editorPanel = new Border
        {
            Child = editor,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)host.FindResource("CardBackgroundFillColorDefaultBrush"),
        };

        bool syncing = false;
        Color Current() => Parse(read()) ?? Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1C);
        void Show()
        {
            syncing = true;
            var color = Current();
            editorPanel.Visibility = !toggle || read() != null ? Visibility.Visible : Visibility.Collapsed;
            hexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            opacity.Value = Math.Round(color.A / 255.0 * 100);
            opacityText.Text = $"{opacity.Value:0} %";
            preview.Background = new SolidColorBrush(color);
            foreach (System.Windows.Controls.Button swatch in swatches.Children)
                Mark(host, swatch, string.Equals((string)swatch.Tag, hexBox.Text, StringComparison.OrdinalIgnoreCase));
            syncing = false;
        }
        void Save(Color rgb, double percent)
        {
            byte alpha = (byte)Math.Round(Math.Clamp(percent, 20, 100) / 100 * 255);
            string value = $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
            Settings.Update(s => write(s, value));
            Show();
        }

        foreach (var hex in SurfacePalette.Append("accent"))
        {
            bool accent = hex == "accent";
            var color = accent ? ThemeManager.Accent : Palette.Hex(hex);
            var swatch = Swatch(accent ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : hex, 30);
            if (accent)
                swatch.ToolTip = Loc.T("Color de acento");
            swatch.Click += (_, _) => Save(color, opacity.Value);
            swatches.Children.Add(swatch);
        }
        toggleSwitch.Click += (_, _) =>
        {
            if (toggleSwitch.IsChecked == true)
                Save(Current(), Current().A / 255.0 * 100);
            else
            {
                Settings.Update(s => write(s, null));
                Show();
            }
        };
        hexBox.LostFocus += (_, _) =>
        {
            if (!syncing && Parse(hexBox.Text.Trim()) is { } typed)
                Save(typed, opacity.Value);
            else
                Show();
        };
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && Parse(hexBox.Text.Trim()) is { } typed)
                Save(typed, opacity.Value);
        };
        opacity.ValueChanged += (_, _) =>
        {
            opacityText.Text = $"{opacity.Value:0} %";
            if (!syncing)
                Save(Current(), opacity.Value);
        };
        Show();

        var panel = new StackPanel();
        panel.Children.Add(card);
        panel.Children.Add(editorPanel);
        return panel;
    }

    private static Color? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var value = text.StartsWith('#') ? text : "#" + text;
            return value.Length is 7 or 9 && int.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
                ? (Color)ColorConverter.ConvertFromString(value)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static System.Windows.Controls.Button Swatch(string hex, double size) => new()
    {
        Width = size,
        Height = size,
        Margin = new Thickness(0, 0, 6, 6),
        Tag = hex,
        ToolTip = hex,
        Background = new SolidColorBrush(Palette.Hex(hex)),
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand,
    };

    private static void Mark(FrameworkElement host, System.Windows.Controls.Button swatch, bool selected)
    {
        swatch.BorderThickness = new Thickness(selected ? 3 : 0);
        swatch.BorderBrush = (Brush)host.FindResource("TextFillColorPrimaryBrush");
    }

    // ── Fondo de pantalla ───────────────────────────────────────────────────

    private void UpdateWallpaper()
    {
        var path = Wallpaper.Current;
        WallpaperPath.Text = string.IsNullOrEmpty(path) ? Loc.T("Color sólido") : path;
        WallpaperPreview.Source = LoadThumbnail(path);
        RestoreWallpaperButton.Visibility = Settings.Current.OriginalWallpaper is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static BitmapImage? LoadThumbnail(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 320;
            image.CacheOption = BitmapCacheOption.OnLoad; // no deja el archivo bloqueado
            image.EndInit();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private async void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var files = await FilePicker.PickFilesAsync(Loc.T("Elegir fondo de pantalla"),
                                                    Loc.T("Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Todos los archivos|*.*"));
        if (files.Length == 0)
            return;
        Settings.Current.OriginalWallpaper ??= Wallpaper.Current;
        Settings.Save();
        TrySetWallpaper(files[0]);
    }

    private void RestoreWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.Current.OriginalWallpaper is not { } original)
            return;
        if (TrySetWallpaper(original))
        {
            Settings.Current.OriginalWallpaper = null;
            Settings.Save();
            UpdateWallpaper();
        }
    }

    private bool TrySetWallpaper(string path)
    {
        try
        {
            Wallpaper.Set(path);
            UpdateWallpaper();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el fondo", ex);
            System.Windows.MessageBox.Show(Loc.F("No se pudo cambiar el fondo:\n{0}", ex.Message), "Strata UI");
            return false;
        }
    }
}
