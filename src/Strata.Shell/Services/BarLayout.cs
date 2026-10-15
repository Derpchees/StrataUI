using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services;

/// <summary>
/// Qué elementos lleva cada barra y en qué orden. Cada barra se identifica por su monitor y si es
/// la primera o la segunda (borde opuesto). La bandeja va en una sola barra: la primera que la
/// lleve; esa es la barra "principal" (tecla Windows, ventanas de monitores sin barra).
/// </summary>
public static class BarLayout
{
    /// <summary>Identificador estable de una barra: "\\.\DISPLAY1|1" (primera) o "|2" (segunda).</summary>
    public static string Id(string device, bool second) => device + (second ? "|2" : "|1");

    public static string DeviceOf(string barId) => barId[..barId.LastIndexOf('|')];

    public static bool IsSecond(string barId) => barId.EndsWith("|2", StringComparison.Ordinal);

    /// <summary>
    /// Lo de siempre (antes de poder elegirlo): Inicio en las barras con ventanas; el área de estado
    /// en las del monitor principal, en las dedicadas a ella o en todas si así se eligió; la bandeja
    /// en la que la llevaba.
    /// </summary>
    public static List<BarItem> Defaults(BarRole role, StrataSettings s)
    {
        var items = new List<BarItem>();
        if (role.ShowsApps)
            items.Add(BarItem.Start);
        bool status = role.ShowsStatus && (role.IsMainScreen || !role.ShowsApps || s.StatusOnAllTaskbars);
        if (status && s.ShowMediaModule)
            items.Add(BarItem.Media);
        if (role.HasTray)
            items.Add(BarItem.Tray);
        if (!status)
            return items;
        if (s.ShowNetworkModule)
            items.Add(BarItem.Network);
        if (s.ShowBluetoothModule)
            items.Add(BarItem.Bluetooth);
        if (s.ShowBrightnessModule)
            items.Add(BarItem.Brightness);
        if (s.ShowVolumeModule)
            items.Add(BarItem.Volume);
        if (s.ShowBatteryModule)
            items.Add(BarItem.Battery);
        items.Add(BarItem.Clock);
        return items;
    }

    /// <summary>Elementos de una barra: los elegidos en el Hub o, si no hay, los de siempre.</summary>
    public static List<BarItem> ItemsFor(string barId, BarRole role, StrataSettings s) =>
        s.BarItems.TryGetValue(barId, out var chosen) ? chosen.Distinct().ToList() : Defaults(role, s);

    /// <summary>
    /// Barra que lleva la bandeja: la primera (en el orden dado) que la tenga entre sus elementos;
    /// si ninguna, la que la llevaba siempre. Nunca queda sin bandeja.
    /// </summary>
    public static string TrayBar(IReadOnlyList<(string Id, BarRole Role)> bars, StrataSettings s) =>
        bars.FirstOrDefault(b => ItemsFor(b.Id, b.Role, s).Contains(BarItem.Tray)).Id
        ?? bars.FirstOrDefault(b => b.Role.HasTray).Id
        ?? bars.FirstOrDefault().Id
        ?? "";

    /// <summary>Barras con la bandeja elegida (cambiarla recrea las barras: la bandeja se asigna al crearlas).</summary>
    public static string TrayKey(StrataSettings s) =>
        string.Join(";", s.BarItems.Where(p => p.Value.Contains(BarItem.Tray)).Select(p => p.Key).Order());

    /// <summary>Nombre del elemento en el Hub.</summary>
    public static string Name(BarItem item) => item switch
    {
        BarItem.Start => Loc.T("Menú Inicio"),
        BarItem.Media => Loc.T("Multimedia"),
        BarItem.Tray => Loc.T("Bandeja del sistema"),
        BarItem.Network => Loc.T("Red"),
        BarItem.Bluetooth => Loc.T("Bluetooth"),
        BarItem.Brightness => Loc.T("Brillo"),
        BarItem.Volume => Loc.T("Volumen"),
        BarItem.Battery => Loc.T("Batería"),
        BarItem.Cpu => Loc.T("CPU"),
        BarItem.Gpu => Loc.T("GPU"),
        BarItem.Ram => Loc.T("Memoria RAM"),
        BarItem.Fps => Loc.T("Contador de FPS"),
        BarItem.FreeRam => Loc.T("Liberar RAM"),
        BarItem.Clock => Loc.T("Reloj y notificaciones"),
        _ => item.ToString(),
    };

    /// <summary>Ícono del elemento en el Hub.</summary>
    public static Wpf.Ui.Controls.SymbolRegular Icon(BarItem item) => item switch
    {
        BarItem.Start => Wpf.Ui.Controls.SymbolRegular.Grid24,
        BarItem.Media => Wpf.Ui.Controls.SymbolRegular.MusicNote224,
        BarItem.Tray => Wpf.Ui.Controls.SymbolRegular.ChevronUp24,
        BarItem.Network => Wpf.Ui.Controls.SymbolRegular.Wifi124,
        BarItem.Bluetooth => Wpf.Ui.Controls.SymbolRegular.Bluetooth24,
        BarItem.Brightness => Wpf.Ui.Controls.SymbolRegular.BrightnessHigh24,
        BarItem.Volume => Wpf.Ui.Controls.SymbolRegular.Speaker224,
        BarItem.Battery => Wpf.Ui.Controls.SymbolRegular.Battery524,
        BarItem.Cpu => Wpf.Ui.Controls.SymbolRegular.DeveloperBoard24,
        BarItem.Gpu => Wpf.Ui.Controls.SymbolRegular.Gauge24,
        BarItem.Ram => Wpf.Ui.Controls.SymbolRegular.Ram20,
        BarItem.Fps => Wpf.Ui.Controls.SymbolRegular.Games24,
        BarItem.FreeRam => Wpf.Ui.Controls.SymbolRegular.Broom24,
        BarItem.Clock => Wpf.Ui.Controls.SymbolRegular.Clock24,
        _ => Wpf.Ui.Controls.SymbolRegular.Circle24,
    };
}
