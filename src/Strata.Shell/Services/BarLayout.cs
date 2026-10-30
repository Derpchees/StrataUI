using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services;

/// <summary>
/// Qué lleva una barra según lo de siempre (antes de poder elegirlo): bandeja, si es la del
/// monitor principal, ventanas abiertas y área de estado. Solo sirve para armar los elementos
/// por defecto y para leer los ajustes de antes (<see cref="LegacyLayouts"/>).
/// </summary>
public sealed record BarRole(bool HasTray, bool IsMainScreen, bool ShowsApps = true, bool ShowsStatus = true);

/// <summary>
/// El diseño de las barras (<see cref="StrataSettings.Layouts"/>): qué barras tiene cada monitor
/// (una por borde) y qué lleva cada una, en orden y por zonas. Cada monitor lleva cada elemento
/// una sola vez; la bandeja es la misma en todos los que la lleven.
/// </summary>
public static class BarLayout
{
    /// <summary>Identificador estable de una barra: su monitor y su borde ("\\.\DISPLAY1|Bottom").</summary>
    public static string Id(string device, TaskbarPosition edge) => device + "|" + edge;

    public static string DeviceOf(string barId) => barId[..barId.LastIndexOf('|')];

    public static TaskbarPosition EdgeOf(string barId) =>
        Enum.TryParse<TaskbarPosition>(barId[(barId.LastIndexOf('|') + 1)..], out var edge) ? edge : TaskbarPosition.Bottom;

    public static bool IsVertical(TaskbarPosition edge) => edge is TaskbarPosition.Left or TaskbarPosition.Right;

    /// <summary>Elementos que se pueden poner, en el orden en que se listan (en la barra, el elegido).</summary>
    public static readonly BarItem[] All =
    [
        BarItem.Start, BarItem.Apps, BarItem.Tray, BarItem.Media, BarItem.Network, BarItem.Bluetooth, BarItem.Volume,
        BarItem.Brightness, BarItem.Battery, BarItem.Clock, BarItem.Notifications, BarItem.Cpu, BarItem.Gpu, BarItem.Ram,
        BarItem.Fps, BarItem.NetSpeed, BarItem.Screenshot, BarItem.Power,
    ];

    /// <summary>
    /// El botón "Liberar RAM" se fusionó con el medidor de RAM (su clic la libera): donde estaba
    /// queda el medidor (si el monitor no lo tenía ya). Una vez. True si cambió algo.
    /// </summary>
    public static bool MergeFreeRam(StrataSettings s)
    {
        if (s.FreeRamMerged)
            return false;
        s.FreeRamMerged = true;
        foreach (var layout in s.Layouts.Values)
        {
            bool hasRam = layout.Bars.Any(b => b.Items.Contains(BarItem.Ram));
            foreach (var bar in layout.Bars)
            {
                int at = bar.Items.IndexOf(BarItem.FreeRam);
                if (at < 0)
                    continue;
                var zone = ZoneOf(bar, BarItem.FreeRam);
                bar.Items.RemoveAt(at);
                bar.Zones.Remove(BarItem.FreeRam);
                if (!hasRam)
                {
                    bar.Items.Insert(at, BarItem.Ram);
                    SetZone(bar, BarItem.Ram, zone);
                    hasRam = true;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Lo de siempre (antes de poder elegirlo): Inicio y las apps en las barras con ventanas; el
    /// área de estado en las del monitor principal, en las dedicadas a ella o en todas si así se
    /// eligió; la bandeja en la que la llevaba.
    /// </summary>
    public static List<BarItem> Defaults(BarRole role, StrataSettings s)
    {
        var items = new List<BarItem>();
        if (role.ShowsApps)
        {
            items.Add(BarItem.Start);
            items.Add(BarItem.Apps);
        }
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
        items.Add(BarItem.Notifications);
        return items;
    }

    /// <summary>Diseño de un monitor que nunca se configuró: una barra abajo con lo de siempre.</summary>
    public static ScreenLayout DefaultLayout(bool isMainScreen, StrataSettings s)
    {
        var bar = new BarConfig { Edge = TaskbarPosition.Bottom, Items = Defaults(new BarRole(isMainScreen, isMainScreen), s) };
        ZoneLikeAlignment(bar, s.DefaultScreen());
        return new ScreenLayout { Bars = [bar] };
    }

    /// <summary>
    /// Inicio y las apps donde los ponía la alineación de antes (o la de Windows): juntos a la
    /// izquierda o al centro, o Inicio a la izquierda y las apps al centro ("centrar las apps").
    /// Quedan junto a lo que ya había en esa zona, como se veían.
    /// </summary>
    internal static void ZoneLikeAlignment(BarConfig bar, ScreenSettings screen)
    {
        bool left = WindowsTaskbarPrefs.Alignment(screen) == TaskbarAlignment.Left;
        var startZone = left ? BarZone.Start : BarZone.Center;
        var appsZone = left && !screen.TaskbarCenterApps ? BarZone.Start : BarZone.Center;
        foreach (var (item, zone) in new[] { (BarItem.Start, startZone), (BarItem.Apps, appsZone) })
        {
            if (!bar.Items.Remove(item))
                continue;
            SetZone(bar, item, zone);
            // Al principio iban después de lo de esa zona; al centro, antes.
            int at = zone == BarZone.Start
                ? bar.Items.FindLastIndex(i => ZoneOf(bar, i) == BarZone.Start && i is not (BarItem.Start or BarItem.Apps)) + 1
                : bar.Items.FindIndex(i => ZoneOf(bar, i) == BarZone.Center && i is not (BarItem.Start or BarItem.Apps));
            if (item == BarItem.Apps && bar.Items.IndexOf(BarItem.Start) is var start and >= 0 && startZone == appsZone)
                at = start + 1;
            bar.Items.Insert(at < 0 ? bar.Items.Count : at, item);
        }
    }

    /// <summary>
    /// Hasta la 3.0 Inicio y las apps no tenían zona: iban juntos donde decía la alineación. Se les
    /// da la zona en que se veían, una vez. True si cambió algo.
    /// </summary>
    public static bool ZoneStartAndApps(StrataSettings s)
    {
        if (s.StartAppsZoned)
            return false;
        s.StartAppsZoned = true;
        foreach (var (device, layout) in s.Layouts)
            foreach (var bar in layout.Bars)
                ZoneLikeAlignment(bar, s.For(device));
        return true;
    }

    /// <summary>La barra con ese identificador (null si su monitor no la tiene).</summary>
    public static BarConfig? Find(StrataSettings s, string barId) =>
        s.Layouts.TryGetValue(DeviceOf(barId), out var layout)
            ? layout.Bars.FirstOrDefault(b => b.Edge == EdgeOf(barId))
            : null;

    /// <summary>Elementos de una barra, en orden y sin repetir.</summary>
    public static List<BarItem> ItemsFor(string barId, StrataSettings s) =>
        Find(s, barId)?.Items.Distinct().ToList() ?? [];

    /// <summary>Cambia los elementos de una barra (sin guardar); las zonas de los que salieron se olvidan.</summary>
    public static void SetItems(StrataSettings s, string barId, List<BarItem> items)
    {
        if (Find(s, barId) is not { } bar)
            return;
        bar.Items = items.Distinct().ToList();
        foreach (var gone in bar.Zones.Keys.Where(k => !bar.Items.Contains(k)).ToList())
            bar.Zones.Remove(gone);
    }

    /// <summary>Zona de un elemento en una barra (al final si no se eligió otra).</summary>
    public static BarZone ZoneOf(string barId, BarItem item, StrataSettings s) =>
        Find(s, barId) is { } bar ? ZoneOf(bar, item) : BarZone.End;

    public static BarZone ZoneOf(BarConfig bar, BarItem item) =>
        bar.Zones.TryGetValue(item, out var zone) ? zone : BarZone.End;

    /// <summary>Cambia la zona de un elemento (sin guardar).</summary>
    public static void SetZone(string barId, BarItem item, BarZone zone, StrataSettings s)
    {
        if (Find(s, barId) is { } bar)
            SetZone(bar, item, zone);
    }

    public static void SetZone(BarConfig bar, BarItem item, BarZone zone)
    {
        if (zone == BarZone.End)
            bar.Zones.Remove(item);
        else
            bar.Zones[item] = zone;
    }

    /// <summary>
    /// Lugar de un elemento nuevo en la lista: Inicio al principio, las apps detrás de Inicio y lo
    /// demás antes del reloj (o al final).
    /// </summary>
    public static void Insert(List<BarItem> items, BarItem item)
    {
        items.Remove(item);
        int start = items.IndexOf(BarItem.Start), clock = items.IndexOf(BarItem.Clock);
        int index = item switch
        {
            BarItem.Start => 0,
            BarItem.Apps => start + 1,
            _ => clock >= 0 ? clock : items.Count,
        };
        items.Insert(Math.Clamp(index, 0, items.Count), item);
    }

    /// <summary>
    /// Deja un diseño válido: una barra por borde y cada elemento una sola vez en el monitor (se
    /// queda la primera barra que lo lleva, en el orden de creación); sin zonas de lo que no está.
    /// </summary>
    public static void Normalize(ScreenLayout layout)
    {
        var seen = new HashSet<BarItem>();
        var edges = new HashSet<TaskbarPosition>();
        layout.Bars = InCreationOrder(layout).Where(b => edges.Add(b.Edge)).ToList();
        foreach (var bar in layout.Bars)
        {
            bar.Items = bar.Items.Where(seen.Add).ToList();
            foreach (var key in bar.Zones.Keys.Where(k => !bar.Items.Contains(k)).ToList())
                bar.Zones.Remove(key);
        }
    }

    /// <summary>
    /// Orden en que se crean las barras de un monitor: arriba y abajo primero, así Windows les deja
    /// las esquinas (ocupan todo el ancho) y las de los lados van entre ellas.
    /// </summary>
    public static IEnumerable<BarConfig> InCreationOrder(ScreenLayout layout) =>
        layout.Bars.OrderBy(b => IsVertical(b.Edge)).ThenBy(b => b.Edge);

    /// <summary>
    /// Lo que obliga a recrear las barras: qué bordes tiene cada monitor y cuáles llevan la bandeja
    /// o las apps abiertas (la bandeja se asigna al crearlas y de las apps depende qué ventanas
    /// muestra cada una). Lo demás se aplica en vivo.
    /// </summary>
    public static string StructureKey(StrataSettings s) =>
        string.Join(";", s.Layouts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + string.Join(",",
            InCreationOrder(p.Value).Select(b => b.Edge + (b.Items.Contains(BarItem.Tray) ? "+T" : "") + (b.Items.Contains(BarItem.Apps) ? "+A" : "")))));

    /// <summary>Nombre de una zona según la orientación de la barra.</summary>
    public static string ZoneName(BarZone zone, bool vertical) => zone switch
    {
        BarZone.Start => vertical ? Loc.T("Arriba") : Loc.T("Izquierda"),
        BarZone.Center => Loc.T("Centro"),
        _ => vertical ? Loc.T("Abajo") : Loc.T("Derecha"),
    };

    /// <summary>Nombre de un borde de la pantalla.</summary>
    public static string EdgeName(TaskbarPosition edge) => edge switch
    {
        TaskbarPosition.Top => Loc.T("Arriba"),
        TaskbarPosition.Left => Loc.T("Izquierda"),
        TaskbarPosition.Right => Loc.T("Derecha"),
        _ => Loc.T("Abajo"),
    };

    /// <summary>Nombre de la barra de un borde ("Barra de abajo").</summary>
    public static string BarName(TaskbarPosition edge) => edge switch
    {
        TaskbarPosition.Top => Loc.T("Barra de arriba"),
        TaskbarPosition.Left => Loc.T("Barra de la izquierda"),
        TaskbarPosition.Right => Loc.T("Barra de la derecha"),
        _ => Loc.T("Barra de abajo"),
    };

    /// <summary>
    /// La campana de notificaciones llegó después que el reloj (que antes abría las notificaciones):
    /// las barras con reloj elegidas a mano la reciben a su lado, una sola vez. True si cambió algo.
    /// </summary>
    public static bool AddNotificationBell(StrataSettings s)
    {
        if (s.NotificationBellAdded)
            return false;
        s.NotificationBellAdded = true;
        var lists = (s.BarItems?.Values ?? Enumerable.Empty<List<BarItem>>())
            .Concat(s.Layouts.Values.SelectMany(l => l.Bars).Select(b => b.Items));
        foreach (var items in lists)
            if (items.IndexOf(BarItem.Clock) is var clock and >= 0 && !items.Contains(BarItem.Notifications))
                items.Insert(clock + 1, BarItem.Notifications);
        return true;
    }

    /// <summary>Nombre del elemento en el Hub y en el modo edición.</summary>
    public static string Name(BarItem item) => item switch
    {
        BarItem.Start => Loc.T("Menú Inicio"),
        BarItem.Apps => Loc.T("Apps abiertas"),
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
        BarItem.NetSpeed => Loc.T("Velocidad de internet"),
        BarItem.Clock => Loc.T("Reloj y calendario"),
        BarItem.Notifications => Loc.T("Notificaciones"),
        BarItem.Screenshot => Loc.T("Captura de pantalla"),
        BarItem.Power => Loc.T("Energía"),
        _ => item.ToString(),
    };

    /// <summary>Ícono del elemento en el Hub y en el modo edición.</summary>
    public static Wpf.Ui.Controls.SymbolRegular Icon(BarItem item) => item switch
    {
        BarItem.Start => Wpf.Ui.Controls.SymbolRegular.Grid24,
        BarItem.Apps => Wpf.Ui.Controls.SymbolRegular.WindowMultiple20,
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
        BarItem.NetSpeed => Wpf.Ui.Controls.SymbolRegular.ArrowBidirectionalUpDown24,
        BarItem.Clock => Wpf.Ui.Controls.SymbolRegular.Clock24,
        BarItem.Notifications => Wpf.Ui.Controls.SymbolRegular.Alert24,
        BarItem.Screenshot => Wpf.Ui.Controls.SymbolRegular.Camera24,
        BarItem.Power => Wpf.Ui.Controls.SymbolRegular.Power24,
        _ => Wpf.Ui.Controls.SymbolRegular.Circle24,
    };
}
