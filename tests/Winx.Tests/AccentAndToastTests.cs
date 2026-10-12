using System.Text.Json;
using System.Windows.Media;
using Winx.Shell.Services;
using Winx.Shell.Services.Notifications;
using Winx.Shell.Services.Themes;
using Xunit;

namespace Winx.Tests;

public class AccentAndToastTests
{
    // ── Wallpaper Engine ────────────────────────────────────────────────────

    private static JsonDocument Config(string mode, string file = "C:/wp/123/video.mp4") => JsonDocument.Parse($$"""
        {
          "?installdirectory": "C:/Steam/wallpaper_engine",
          "PC": {
            "general": {
              "user": { "adjustdwmcolormode": "{{mode}}" },
              "wallpaperconfig": { "selectedwallpapers": { "Monitor0": { "file": "{{file}}" } } }
            }
          }
        }
        """);

    [Theory]
    [InlineData("accent", true)]
    [InlineData("everything", true)]
    [InlineData("disabled", false)]
    [InlineData("", false)]
    public void Wallpaper_engine_color_mode_is_detected(string mode, bool expected)
    {
        using var config = Config(mode);
        Assert.Equal(expected, WallpaperEngine.AdjustsColor(config));
    }

    [Fact]
    public void Wallpaper_engine_without_the_option_does_not_adjust()
    {
        using var config = JsonDocument.Parse("""{ "PC": { "general": { "user": {} } } }""");
        Assert.False(WallpaperEngine.AdjustsColor(config));
    }

    [Fact]
    public void Wallpaper_engine_selected_file_is_read()
    {
        using var config = Config("disabled", "C:/wp/9/scene.json");
        Assert.Equal(["C:/wp/9/scene.json"], WallpaperEngine.SelectedFiles(config));
    }

    // ── Color del fondo ─────────────────────────────────────────────────────

    private static byte[] Image(params (Color Color, int Count)[] parts)
    {
        var pixels = new List<byte>();
        foreach (var (color, count) in parts)
            for (int i = 0; i < count; i++)
                pixels.AddRange([color.B, color.G, color.R, 255]);
        return pixels.ToArray();
    }

    [Fact]
    public void Dominant_vivid_color_wins_over_a_bigger_gray_area()
    {
        var picked = WallpaperAccent.Pick(Image((Color.FromRgb(40, 40, 40), 700), (Color.FromRgb(20, 90, 200), 300)));
        var (hue, _, _) = WallpaperAccent.ToHsv(picked);
        Assert.InRange(hue, 200, 230); // azul
    }

    [Fact]
    public void Black_wallpaper_gives_a_visible_gray_not_black()
    {
        var picked = WallpaperAccent.Pick(Image((Colors.Black, 1000)));
        var (_, saturation, value) = WallpaperAccent.ToHsv(picked);
        Assert.True(saturation < 0.15);
        Assert.True(value >= 0.5);
    }

    [Fact]
    public void Hsv_round_trip_keeps_the_color()
    {
        var color = Color.FromRgb(200, 120, 30);
        var (h, s, v) = WallpaperAccent.ToHsv(color);
        Assert.Equal(color, WallpaperAccent.FromHsv(h, s, v));
    }

    [Fact]
    public void Black_accent_is_lightened_on_the_dark_theme()
    {
        var shown = ThemeManager.Readable(Colors.Black, light: false);
        Assert.True(ThemeManager.Luminance(shown) >= 0.16);
    }

    [Fact]
    public void Readable_accent_is_left_alone()
    {
        var blue = Color.FromRgb(0x00, 0x78, 0xD4);
        Assert.Equal(blue, ThemeManager.Readable(blue, light: true));
    }

    // ── XML de las notificaciones ───────────────────────────────────────────

    [Fact]
    public void Browser_attribution_gives_the_site()
    {
        var details = NotificationDb.Parse("""
            <toast launch="1|0|Default|0|https://web.whatsapp.com/|p#1">
              <visual><binding template="ToastGeneric">
                <text>Ana</text><text>Hola</text><text placement="attribution">web.whatsapp.com</text>
              </binding></visual>
            </toast>
            """);
        Assert.Equal("web.whatsapp.com", details?.Domain);
    }

    [Fact]
    public void Chromium_launch_origin_is_used_without_attribution()
    {
        var details = NotificationDb.Parse("""<toast launch="1|0|Default|0|https://mail.google.com/|x"><visual><binding template="ToastGeneric"><text>Hi</text></binding></visual></toast>""");
        Assert.Equal("mail.google.com", details?.Domain);
    }

    [Fact]
    public void App_toast_has_no_site_and_ignores_missing_pictures()
    {
        var details = NotificationDb.Parse("""
            <toast launch="action=open"><visual><binding template="ToastGeneric">
              <text>Hi</text><image placement="appLogoOverride" src="C:\no\existe.png" hint-crop="circle"/>
            </binding></visual></toast>
            """);
        Assert.NotNull(details);
        Assert.Null(details!.Domain);
        Assert.Null(details.Picture);
        Assert.True(details.CircleCrop);
    }

    [Fact]
    public void Broken_xml_is_ignored() => Assert.Null(NotificationDb.Parse("<toast"));
}
