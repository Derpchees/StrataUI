using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;

namespace Strata.Shell.Services.Modules;

/// <summary>Lo que se puede importar: cada tipo tiene su carpeta y su sección en el Hub.</summary>
public enum ModuleKind { Theme, Animation, Widget, StartMenu }

/// <summary>
/// Un módulo es un archivo JSON declarativo (nada de código ni XAML: no puede ejecutar nada). Lo
/// común: tipo, nombre, descripción y autor. El formato está en docs/modules/README.md.
/// </summary>
public abstract partial class ModuleBase
{
    [JsonPropertyOrder(-4)]
    public string? Type { get; set; }
    [JsonPropertyOrder(-3)]
    public string Name { get; set; } = "";
    [JsonPropertyOrder(-2)]
    public string? Description { get; set; }
    [JsonPropertyOrder(-1)]
    public string? Author { get; set; }

    /// <summary>Nombre del archivo sin extensión (lo pone <see cref="ModuleStore"/> al cargarlo).</summary>
    [JsonIgnore]
    public string Id { get; set; } = "";

    /// <summary>Lo que se muestra debajo del nombre: la descripción y el autor.</summary>
    [JsonIgnore]
    public string Summary =>
        string.Join(" · ", new[] { Description, Author is { Length: > 0 } ? Loc.F("por {0}", Author) : null }.Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>Null si el módulo es válido; si no, el motivo (ya traducido).</summary>
    internal virtual string? Validate()
    {
        Name = Name.Trim();
        if (Name.Length == 0)
            return Loc.T("Falta el nombre.");
        if (Name.Length > 60 || (Description?.Length ?? 0) > 300 || (Author?.Length ?? 0) > 60)
            return Loc.T("El nombre, la descripción o el autor son demasiado largos.");
        return null;
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    /// <summary>"#RRGGBB" o "#AARRGGBB" (no nombres ni otros formatos de WPF).</summary>
    internal static bool IsColor(string? value) => value != null && HexColor().IsMatch(value);

    internal static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}

// ── Temas ──────────────────────────────────────────────────────────────────

/// <summary>Colores de un esquema, como los de Nord o Dracula.</summary>
public sealed class ThemeColors
{
    public string Bar { get; set; } = "";
    public string Panel { get; set; } = "";
    public string Surface { get; set; } = "";
    public string Text { get; set; } = "";
    public string SubText { get; set; } = "";
    public string Border { get; set; } = "";
    public string? Accent { get; set; }
}

/// <summary>Un estilo (material, redondeo, vidrio, barra flotante) y, si trae colores, un esquema.</summary>
public sealed class ThemeModule : ModuleBase
{
    /// <summary>Claro u oscuro; sin valor sigue a Windows (o, con colores, se deduce del panel).</summary>
    public bool? Light { get; set; }
    public Backdrop Backdrop { get; set; } = Backdrop.Solid;
    public bool Glass { get; set; }
    public bool Tonal { get; set; }
    public bool Floating { get; set; }
    public double Radius { get; set; } = 8;
    public double PanelRadius { get; set; } = 12;
    public ThemeColors? Colors { get; set; }

    internal override string? Validate()
    {
        if (base.Validate() is { } error)
            return error;
        if (!Enum.IsDefined(Backdrop))
            return Loc.T("Material de fondo no válido.");
        if (Colors is { } c)
        {
            foreach (var value in new[] { c.Bar, c.Panel, c.Surface, c.Text, c.SubText, c.Border })
                if (!IsColor(value))
                    return Loc.F("Color no válido: {0}", value);
            if (c.Accent != null && !IsColor(c.Accent))
                return Loc.F("Color no válido: {0}", c.Accent);
        }
        return null;
    }

    private ThemeInfo? _theme;

    public ThemeInfo ToTheme() => _theme ??= Build();

    private ThemeInfo Build()
    {
        ColorScheme? scheme = Colors is { } c ? new(c.Bar, c.Panel, c.Surface, c.Text, c.SubText, c.Border, c.Accent) : null;
        // Con colores propios hace falta saber si es claro u oscuro (controles, iconos): lo dice el panel.
        bool? light = Light ?? (scheme is null ? null : ThemeManager.Luminance(Palette.Hex(scheme.Panel)) > 0.5);
        return new ThemeInfo(StrataTheme.System, Name, Description ?? "", light, Backdrop, Glass, Tonal, Floating,
                             Clamp(Radius, 0, 24), Clamp(PanelRadius, 0, 32), scheme, ModuleId: Id);
    }
}

// ── Animaciones ────────────────────────────────────────────────────────────

public enum MotionEasing { Cubic, Linear, Sine, Quint, Back, Elastic, Bounce }

/// <summary>
/// Un tramo (entrada o salida): desde dónde llega o hacia dónde se va. <see cref="Offset"/> es la
/// distancia desde el lado de la barra; <see cref="Scale"/>, el tamaño al empezar (o al terminar,
/// en la salida); <see cref="Duration"/>, multiplica la velocidad elegida en el Hub.
/// </summary>
public sealed class MotionStep
{
    public double Offset { get; set; } = 16;
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; }
    public double Duration { get; set; } = 1;
    public MotionEasing Easing { get; set; } = MotionEasing.Cubic;
    /// <summary>Cuánto se pasa de largo (Back) u oscila (Elastic).</summary>
    public double Amplitude { get; set; } = 0.4;

    internal void Normalize()
    {
        Offset = ModuleBase.Clamp(Offset, -200, 200);
        Scale = ModuleBase.Clamp(Scale, 0.3, 1.5);
        Opacity = ModuleBase.Clamp(Opacity, 0, 1);
        Duration = ModuleBase.Clamp(Duration, 0.2, 4);
        Amplitude = ModuleBase.Clamp(Amplitude, 0, 3);
        if (!Enum.IsDefined(Easing))
            Easing = MotionEasing.Cubic;
    }
}

public sealed class AnimationModule : ModuleBase
{
    public MotionStep Enter { get; set; } = new();
    /// <summary>Sin salida: la misma de la entrada, más corta.</summary>
    public MotionStep? Exit { get; set; }

    internal override string? Validate()
    {
        if (base.Validate() is { } error)
            return error;
        Enter ??= new MotionStep();
        Enter.Normalize();
        Exit ??= new MotionStep { Offset = Enter.Offset * 0.6, Scale = 1 - (1 - Enter.Scale) * 0.6, Duration = 0.8 };
        Exit.Normalize();
        return null;
    }
}

// ── Widgets ────────────────────────────────────────────────────────────────

/// <summary>
/// Una línea de texto del widget. <see cref="Text"/> admite marcadores: {time}, {time:HH:mm:ss},
/// {date}, {date:dddd d MMMM}, {song}, {artist}, {album}, {battery} y {user}. Una línea cuyos
/// marcadores quedan todos vacíos (sin música, sin batería) no se muestra.
/// </summary>
public sealed class WidgetLine
{
    public string Text { get; set; } = "";
    public double Size { get; set; } = 18;
    /// <summary>thin, light, normal, semibold, bold o black.</summary>
    public string? Weight { get; set; }
    /// <summary>"#RRGGBB", "#AARRGGBB" o "accent".</summary>
    public string? Color { get; set; }
    public string? Font { get; set; }
    /// <summary>left, center o right.</summary>
    public string? Align { get; set; }
    public double Opacity { get; set; } = 1;
    /// <summary>Separación con la línea de arriba (puede ser negativa para juntarlas).</summary>
    public double Spacing { get; set; }
}

public sealed class WidgetModule : ModuleBase
{
    public List<WidgetLine> Lines { get; set; } = new();
    public string? Background { get; set; }
    public double CornerRadius { get; set; } = 12;
    public double Padding { get; set; } = 12;
    /// <summary>Sombra detrás del texto para leerse sobre cualquier fondo.</summary>
    public bool Shadow { get; set; } = true;

    internal override string? Validate()
    {
        if (base.Validate() is { } error)
            return error;
        if (Lines is not { Count: > 0 and <= 12 })
            return Loc.T("Un widget lleva de 1 a 12 líneas.");
        if (Background != null && !IsColor(Background))
            return Loc.F("Color no válido: {0}", Background);
        foreach (var line in Lines)
        {
            if (line.Text is null || line.Text.Length > 200)
                return Loc.T("Una línea no tiene texto o es demasiado larga.");
            if (line.Color != null && line.Color != "accent" && !IsColor(line.Color))
                return Loc.F("Color no válido: {0}", line.Color);
            line.Size = Clamp(line.Size, 6, 200);
            line.Opacity = Clamp(line.Opacity, 0.05, 1);
            line.Spacing = Clamp(line.Spacing, -60, 60);
            if (line.Font is { Length: > 80 })
                line.Font = null;
        }
        CornerRadius = Clamp(CornerRadius, 0, 64);
        Padding = Clamp(Padding, 0, 64);
        return null;
    }
}

// ── Menú Inicio ────────────────────────────────────────────────────────────

/// <summary>
/// Un aspecto del menú Inicio: estilo, íconos, qué secciones muestra, los botones del pie, el
/// ícono del botón Inicio y el tamaño. Lo que no trae se deja como está.
/// </summary>
public sealed class StartMenuModule : ModuleBase
{
    public StartMenuStyle? Style { get; set; }
    public StartIconSize? IconSize { get; set; }
    public bool? PinnedLabels { get; set; }
    public bool? ShowPinned { get; set; }
    public bool? ShowAllApps { get; set; }
    public bool? ShowRecent { get; set; }
    public bool? ShowUser { get; set; }
    public bool? UserPicture { get; set; }
    public StartIconKind? StartIcon { get; set; }
    public List<string>? Footer { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    internal override string? Validate()
    {
        if (base.Validate() is { } error)
            return error;
        if (Style is { } style && !Enum.IsDefined(style) || IconSize is { } size && !Enum.IsDefined(size)
            || StartIcon is { } icon && !Enum.IsDefined(icon))
            return Loc.T("Algún valor no es válido.");
        // Los botones que esta versión no conoce se ignoran.
        Footer = Footer?.Where(id => Services.StartMenu.StartFooterCatalog.All.Any(b => b.Id == id)).Distinct().ToList();
        if (Width is { } w)
            Width = Clamp(w, 280, 4000);
        if (Height is { } h)
            Height = Clamp(h, 200, 3000);
        return null;
    }

    /// <summary>Lleva el aspecto a la configuración.</summary>
    internal void ApplyTo(StrataSettings s)
    {
        if (Style is { } style)
            s.StartStyle = style;
        if (IconSize is { } size)
            s.StartIconSize = size;
        if (PinnedLabels is { } labels)
            s.StartShowPinnedLabels = labels;
        if (ShowPinned is { } pinned)
            s.StartShowPinned = pinned;
        if (ShowAllApps is { } all)
            s.StartShowAllApps = all;
        if (ShowRecent is { } recent)
            s.StartShowRecent = recent;
        if (ShowUser is { } user)
            s.StartShowUser = user;
        if (UserPicture is { } picture)
            s.StartShowUserPicture = picture;
        if (StartIcon is { } icon)
            s.StartIcon = icon;
        if (Footer is { Count: > 0 } footer)
            s.StartFooterButtons = footer;
        // Alto 0: el menú toma el de su contenido (los estilos de alto fijo no lo guardan).
        if (Width is { } w)
            s.StartMenuSizes[SizeKey(s.StartStyle)] = [w, Height ?? 0];
    }

    /// <summary>Clave del tamaño guardado: la clase del menú (ver StartMenuBase.SizeKey).</summary>
    internal static string SizeKey(StartMenuStyle style) => style switch
    {
        StartMenuStyle.List => nameof(global::Strata.Shell.StartMenu.StartMenuList),
        StartMenuStyle.Fullscreen => nameof(global::Strata.Shell.StartMenu.StartMenuFullscreen),
        StartMenuStyle.Compact => nameof(global::Strata.Shell.StartMenu.StartMenuCompact),
        StartMenuStyle.Spotlight => nameof(global::Strata.Shell.StartMenu.StartMenuSpotlight),
        StartMenuStyle.Icons => nameof(global::Strata.Shell.StartMenu.StartMenuIcons),
        StartMenuStyle.Profile => nameof(global::Strata.Shell.StartMenu.StartMenuProfile),
        _ => nameof(global::Strata.Shell.StartMenu.StartMenuWin11),
    };

    /// <summary>El menú como está ahora, para compartirlo.</summary>
    internal static StartMenuModule From(StrataSettings s, string name)
    {
        var module = new StartMenuModule
        {
            Type = "startmenu",
            Name = name,
            Style = s.StartStyle,
            IconSize = s.StartIconSize,
            PinnedLabels = s.StartShowPinnedLabels,
            ShowPinned = s.StartShowPinned,
            ShowAllApps = s.StartShowAllApps,
            ShowRecent = s.StartShowRecent,
            ShowUser = s.StartShowUser,
            UserPicture = s.StartShowUserPicture,
            StartIcon = s.StartIcon,
            Footer = s.StartFooterButtons.ToList(),
        };
        if (s.StartMenuSizes.TryGetValue(SizeKey(s.StartStyle), out var size) && size.Length == 2)
            (module.Width, module.Height) = (size[0], size[1] > 0 ? size[1] : null);
        return module;
    }
}
