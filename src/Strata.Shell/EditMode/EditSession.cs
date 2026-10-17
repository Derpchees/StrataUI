using System.Windows;
using Strata.Shell.Services;
using Strata.Shell.Services.Widgets;
using Forms = System.Windows.Forms;

namespace Strata.Shell.EditMode;

/// <summary>
/// Modo edición: una capa sobre cada monitor (<see cref="EditOverlay"/>) con sus barras, sus
/// elementos y sus widgets como se van a ver. Todo se cambia en un borrador (copia del diseño y de
/// los widgets): las barras y el escritorio reales no se tocan hasta <see cref="Apply"/>; <see cref="Cancel"/>
/// (o Esc) lo descarta.
/// </summary>
public static class EditSession
{
    private static readonly List<EditOverlay> Overlays = new();
    private static Window? _hubOnTop;

    /// <summary>Borrador del diseño de cada monitor conectado.</summary>
    internal static Dictionary<string, ScreenLayout> Layouts { get; private set; } = new();

    /// <summary>Borrador de los widgets (copias).</summary>
    internal static List<WidgetConfig> Widgets { get; private set; } = new();

    /// <summary>Borrador del monitor principal de Strata UI (null: el de Windows).</summary>
    internal static string? MainScreen { get; set; }

    /// <summary>Borrador de los ajustes de cada monitor (grosor, estilo y alineación de sus barras, reloj…).</summary>
    internal static Dictionary<string, ScreenSettings> Screens { get; private set; } = new();

    /// <summary>Borrador del monitor que mide el contador de FPS (null: la app activa).</summary>
    internal static string? FpsScreen { get; set; }

    /// <summary>Borrador del monitor de los avisos de notificación (null: el principal de Strata UI).</summary>
    internal static string? NotificationScreen { get; set; }

    /// <summary>Ajustes del borrador de un monitor.</summary>
    internal static ScreenSettings ScreenOf(string device)
    {
        if (!Screens.TryGetValue(device, out var screen))
            Screens[device] = screen = Settings.Current.For(device).Clone();
        return screen;
    }

    /// <summary>Monitor donde saldrían los avisos con el borrador.</summary>
    internal static string? BannerDevice
    {
        get
        {
            var screens = Forms.Screen.AllScreens;
            return NotificationScreen is { } chosen && screens.Any(s => s.DeviceName == chosen)
                ? chosen
                : MainScreen is { } main && screens.Any(s => s.DeviceName == main) ? main
                : screens.FirstOrDefault(s => s.Primary)?.DeviceName;
        }
    }

    /// <summary>Hay cambios sin aplicar.</summary>
    internal static bool Dirty { get; private set; }

    public static bool IsActive => Overlays.Count > 0;

    /// <summary>Cambió el borrador: cada capa se vuelve a dibujar.</summary>
    internal static event Action? Changed;

    /// <summary>Empezó o terminó el modo edición (el Hub muestra o esconde su biblioteca).</summary>
    public static event Action? ActiveChanged;

    public static void Start()
    {
        if (IsActive)
        {
            Overlays[0].Activate();
            return;
        }
        Flyouts.FlyoutManager.CloseAll();
        Controls.TaskPreviewWindow.CloseAll();
        var s = Settings.Current;
        var screens = Forms.Screen.AllScreens;
        string main = App.MainScreenDevice(screens.Select(sc => (sc.DeviceName, sc.Primary)));
        Layouts = screens.ToDictionary(sc => sc.DeviceName,
            sc => s.Layouts.TryGetValue(sc.DeviceName, out var layout) ? layout.Clone() : BarLayout.DefaultLayout(sc.DeviceName == main, s));
        Widgets = s.Widgets.Select(w => w.Clone()).ToList();
        MainScreen = s.TaskbarMainScreen;
        Screens = screens.ToDictionary(sc => sc.DeviceName, sc => s.For(sc.DeviceName).Clone());
        FpsScreen = s.FpsScreen;
        NotificationScreen = s.NotificationScreen;
        Dirty = false;

        Log.Info("Modo edición: abierto");
        foreach (var screen in screens.OrderByDescending(sc => sc.DeviceName == main))
        {
            var overlay = new EditOverlay(screen);
            Overlays.Add(overlay);
            overlay.Show();
        }
        // El Hub, si está abierto, queda encima de las capas (también se arrastra desde él): como
        // ventana de la capa de su monitor, ninguna capa lo tapa aunque se active.
        if (Application.Current.Windows.OfType<Hub.HubWindow>().FirstOrDefault(w => w.IsVisible) is { } hub)
        {
            if (hub.WindowState == WindowState.Minimized)
                hub.WindowState = WindowState.Normal;
            var center = hub.PointToScreen(new Point(hub.ActualWidth / 2, hub.ActualHeight / 2));
            hub.Owner = OverlayAt(center) ?? Overlays[0];
            hub.Activate();
            _hubOnTop = hub;
        }
        else
        {
            Overlays[0].Activate();
        }
        ActiveChanged?.Invoke();
    }

    /// <summary>Cambió el borrador (lo llaman las operaciones de abajo).</summary>
    internal static void Touch()
    {
        Dirty = true;
        Changed?.Invoke();
    }

    /// <summary>Guarda el borrador: las barras se recrean si cambiaron y los widgets se acomodan.</summary>
    public static void Apply()
    {
        if (!IsActive)
            return;
        if (Layouts.Values.All(l => l.Bars.Count == 0))
        {
            foreach (var overlay in Overlays)
                overlay.ShowMessage(Services.Localization.Loc.T("Deja al menos una barra en algún monitor."));
            return;
        }
        foreach (var layout in Layouts.Values)
            BarLayout.Normalize(layout);
        var layouts = Layouts;
        var widgets = Widgets;
        var main = MainScreen;
        var screens = Screens;
        var fps = FpsScreen;
        var notifications = NotificationScreen;
        Close();
        Settings.Update(s =>
        {
            foreach (var (device, layout) in layouts)
                s.Layouts[device] = layout.Clone();
            s.Widgets = widgets.Select(w => w.Clone()).ToList();
            s.TaskbarMainScreen = main;
            s.FpsScreen = fps;
            s.NotificationScreen = notifications;
            // Solo los monitores que cambiaron: uno sin ajustes propios sigue usando los generales.
            foreach (var (device, screen) in screens)
                if (System.Text.Json.JsonSerializer.Serialize(screen) != System.Text.Json.JsonSerializer.Serialize(s.For(device)))
                    s.Screens[device] = screen.Clone();
        });
        WidgetManager.Sync();
        Log.Info("Modo edición: cambios aplicados");
    }

    public static void Cancel()
    {
        if (!IsActive)
            return;
        Close();
        Log.Info("Modo edición: cancelado");
    }

    private static void Close()
    {
        EditDrag.Abort();
        // Antes de cerrar las capas: una ventana se cierra junto con su dueña.
        if (_hubOnTop != null)
        {
            _hubOnTop.Owner = null;
            _hubOnTop = null;
        }
        foreach (var overlay in Overlays.ToList())
            overlay.CloseOverlay();
        Overlays.Clear();
        Layouts = new();
        Widgets = new();
        Screens = new();
        ActiveChanged?.Invoke();
        MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
    }

    // ── Capas ───────────────────────────────────────────────────────────────

    /// <summary>La capa del monitor que contiene ese punto (en píxeles de pantalla).</summary>
    internal static EditOverlay? OverlayAt(Point screen) =>
        Overlays.FirstOrDefault(o => o.ScreenBounds.Contains(screen));

    /// <summary>Mientras se arrastra: las zonas vacías y los bordes sin barra se ven como lugares donde soltar.</summary>
    internal static void ShowDropZones(bool show)
    {
        foreach (var overlay in Overlays)
            overlay.ShowDropZones(show);
    }

    /// <summary>Marca dónde caería lo arrastrado (solo en la capa bajo el puntero).</summary>
    internal static void UpdateMarker(Point screen, DragPayload payload)
    {
        var target = OverlayAt(screen);
        foreach (var overlay in Overlays)
            overlay.ShowMarker(ReferenceEquals(overlay, target) ? overlay.TargetAt(screen, payload) : null);
    }

    /// <summary>Suelta lo arrastrado en ese punto.</summary>
    internal static void Drop(Point screen, DragPayload payload)
    {
        foreach (var each in Overlays)
            each.ShowMarker(null);
        if (OverlayAt(screen) is not { } overlay || overlay.TargetAt(screen, payload) is not { } target)
            return;
        switch (payload, target)
        {
            case (ItemPayload item, BarTarget bar):
                if (item.FromDevice != null && item.FromDevice != overlay.Device)
                    RemoveItem(item.FromDevice, item.Item, touch: false);
                PlaceItem(overlay.Device, bar.Edge, item.Item, bar.Index, bar.Zone);
                break;
            case (ItemPayload item, NewBarTarget edge):
                if (item.FromDevice != null && item.FromDevice != overlay.Device)
                    RemoveItem(item.FromDevice, item.Item, touch: false);
                PlaceItem(overlay.Device, edge.Edge, item.Item, null, BarZone.End);
                break;
            case (ItemPayload { FromDevice: { } from } item, RemoveTarget):
                RemoveItem(from, item.Item);
                break;
            case (WidgetPayload widget, DesktopTarget desktop):
                PlaceWidget(widget.Widget, overlay.Device, desktop.X, desktop.Y);
                break;
            case (BannerPayload, CornerTarget corner):
                PlaceBanner(overlay.Device, corner.Corner);
                break;
        }
    }

    // ── Operaciones sobre el borrador ──────────────────────────────────────

    internal static ScreenLayout LayoutOf(string device)
    {
        if (!Layouts.TryGetValue(device, out var layout))
            Layouts[device] = layout = new ScreenLayout();
        return layout;
    }

    /// <summary>
    /// Pone el elemento en esa barra del monitor (la crea si no está), en esa posición de la lista
    /// (null: su lugar de siempre) y zona. Sale de donde estuviera en ese monitor.
    /// </summary>
    internal static void PlaceItem(string device, TaskbarPosition edge, BarItem item, int? index, BarZone zone)
    {
        var layout = LayoutOf(device);
        var target = layout.Bars.FirstOrDefault(b => b.Edge == edge);
        if (target is null)
            layout.Bars.Add(target = new BarConfig { Edge = edge });
        foreach (var bar in layout.Bars)
        {
            int at = bar.Items.IndexOf(item);
            if (at < 0)
                continue;
            bar.Items.RemoveAt(at);
            bar.Zones.Remove(item);
            if (ReferenceEquals(bar, target) && index > at)
                index--;
        }
        if (index is { } i)
            target.Items.Insert(Math.Clamp(i, 0, target.Items.Count), item);
        else
            BarLayout.Insert(target.Items, item);
        BarLayout.SetZone(target, item, zone);
        Touch();
    }

    internal static void RemoveItem(string device, BarItem item, bool touch = true)
    {
        foreach (var bar in LayoutOf(device).Bars)
        {
            bar.Items.Remove(item);
            bar.Zones.Remove(item);
        }
        if (touch)
            Touch();
    }

    internal static void SetZone(string device, TaskbarPosition edge, BarItem item, BarZone zone)
    {
        if (LayoutOf(device).Bars.FirstOrDefault(b => b.Edge == edge) is { } bar)
        {
            BarLayout.SetZone(bar, item, zone);
            Touch();
        }
    }

    /// <summary>
    /// Pone Inicio y las apps abiertas de esa barra en esas zonas (atajo del clic derecho de la
    /// barra, lo que antes era la alineación). Si quedan juntos, Inicio va justo delante de las apps.
    /// </summary>
    internal static void SetStartAndApps(string device, TaskbarPosition edge, BarZone start, BarZone apps)
    {
        if (LayoutOf(device).Bars.FirstOrDefault(b => b.Edge == edge) is not { } bar)
            return;
        BarLayout.SetZone(bar, BarItem.Start, start);
        BarLayout.SetZone(bar, BarItem.Apps, apps);
        if (start == apps && bar.Items.Contains(BarItem.Start) && bar.Items.Contains(BarItem.Apps))
        {
            bar.Items.Remove(BarItem.Start);
            bar.Items.Insert(bar.Items.IndexOf(BarItem.Apps), BarItem.Start);
        }
        Touch();
    }

    internal static void AddBar(string device, TaskbarPosition edge)
    {
        var layout = LayoutOf(device);
        if (layout.Bars.Any(b => b.Edge == edge))
            return;
        layout.Bars.Add(new BarConfig { Edge = edge });
        Touch();
    }

    internal static void RemoveBar(string device, TaskbarPosition edge)
    {
        LayoutOf(device).Bars.RemoveAll(b => b.Edge == edge);
        Touch();
    }

    /// <summary>Vuelve a lo de siempre en ese monitor (una barra abajo).</summary>
    internal static void ResetScreen(string device)
    {
        bool isMain = device == (MainScreen ?? Forms.Screen.PrimaryScreen?.DeviceName);
        Layouts[device] = BarLayout.DefaultLayout(isMain, Settings.Current);
        Touch();
    }

    internal static void SetMainScreen(string device)
    {
        MainScreen = device;
        Touch();
    }

    /// <summary>Cambia un ajuste del borrador de un monitor (grosor, estilo, alineación, reloj…).</summary>
    internal static void ChangeScreen(string device, Action<ScreenSettings> change)
    {
        change(ScreenOf(device));
        Touch();
    }

    internal static void SetFpsScreen(string? device)
    {
        FpsScreen = device;
        Touch();
    }

    /// <summary>Los avisos salen en ese monitor y esa esquina.</summary>
    internal static void PlaceBanner(string device, NotificationCorner corner)
    {
        NotificationScreen = device;
        ScreenOf(device).NotificationCorner = corner;
        Touch();
    }

    /// <summary>Lleva un widget (nuevo o del borrador) a ese monitor y lugar (fracción del escritorio).</summary>
    internal static void PlaceWidget(WidgetConfig widget, string device, double x, double y)
    {
        var existing = Widgets.FirstOrDefault(w => w.Id == widget.Id);
        if (existing is null)
            Widgets.Add(existing = widget);
        existing.Device = device;
        existing.X = Math.Clamp(x, 0, 1);
        existing.Y = Math.Clamp(y, 0, 1);
        Touch();
    }

    internal static void ChangeWidget(string id, Action<WidgetConfig> change)
    {
        if (Widgets.FirstOrDefault(w => w.Id == id) is { } widget)
        {
            change(widget);
            Touch();
        }
    }

    internal static void RemoveWidget(string id)
    {
        Widgets.RemoveAll(w => w.Id == id);
        Touch();
    }
}
