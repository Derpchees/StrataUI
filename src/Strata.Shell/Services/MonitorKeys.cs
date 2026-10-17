using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Strata.Shell.Services;

/// <summary>Lo que un monitor tiene guardado (para llevarlo a otro nombre o guardarlo mientras no está).</summary>
public sealed class MonitorSnapshot
{
    /// <summary>Sus barras y lo que lleva cada una (null: nunca se configuró).</summary>
    public ScreenLayout? Layout { get; set; }
    public bool Main { get; set; }
    public bool Fps { get; set; }
    public bool Notifications { get; set; }
    public ScreenSettings? Screen { get; set; }
    /// <summary>Widgets que estaban en ese monitor.</summary>
    public List<string> Widgets { get; set; } = new();

    // Formato anterior a la 2.6 (ver LegacyLayouts): se lee y se pasa a Layout.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TaskbarPosition? Position { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecondBarContent? Second { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hidden { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<BarItem>>? Items { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, Dictionary<BarItem, BarZone>>? Zones { get; set; }
}

/// <summary>
/// Los ajustes por monitor se guardan con el nombre que Windows le da ("\\.\DISPLAY1"), y ese nombre
/// no es fijo: al reiniciar, conectar o quitar monitores, Windows los vuelve a repartir y la pantalla
/// de la laptop sola recibe el mismo nombre que el monitor externo principal. Cada monitor tomaba
/// así los ajustes (posición, elementos, tema) de otro.
/// Aquí se sigue a cada monitor por su identificador de hardware (fijo para ese monitor y ese
/// puerto): si cambió de nombre, sus ajustes pasan al nombre nuevo, y si no está, se guardan hasta
/// que vuelva. El resto del programa sigue usando el nombre.
/// </summary>
public static class MonitorKeys
{
    /// <summary>
    /// Pone los ajustes de cada monitor presente bajo su nombre actual. Devuelve true si cambió algo.
    /// La primera vez solo anota quién es quién (los ajustes que hay se toman como de los monitores presentes).
    /// </summary>
    public static bool Reconcile(StrataSettings s, IReadOnlyList<(string Device, string Id)> present)
    {
        var known = present.Where(p => !string.IsNullOrEmpty(p.Id)).ToList();
        var wasPresent = s.MonitorsPresent.ToHashSet();
        var nowPresent = known.Select(p => p.Id).ToHashSet();
        bool changed = false;

        // Se toma todo antes de cambiar nada: dos monitores pueden intercambiar sus nombres.
        var taken = new Dictionary<string, MonitorSnapshot>();
        MonitorSnapshot Snap(string device) => taken.TryGetValue(device, out var snap) ? snap : taken[device] = Take(s, device);

        // Los que ya no están: sus ajustes se guardan hasta que vuelvan.
        foreach (var id in wasPresent.Where(id => !nowPresent.Contains(id)))
            if (s.MonitorNames.TryGetValue(id, out var last))
            {
                s.ParkedMonitors[id] = Snap(last);
                changed = true;
            }

        // Los presentes: los que cambiaron de nombre traen lo suyo; los que vuelven, lo guardado.
        // Uno nuevo (o la primera vez) se queda con lo que haya bajo su nombre, como antes.
        var assign = new List<(string Device, MonitorSnapshot Snapshot)>();
        foreach (var (device, id) in known)
        {
            if (wasPresent.Contains(id) && s.MonitorNames.TryGetValue(id, out var last))
            {
                if (last != device)
                    assign.Add((device, Snap(last)));
            }
            else if (s.ParkedMonitors.Remove(id, out var parked))
                assign.Add((device, parked));
        }
        foreach (var (device, _) in assign)
            Clear(s, device);
        foreach (var (device, snapshot) in assign)
            Put(s, device, snapshot);
        changed |= assign.Count > 0;

        foreach (var (device, id) in known)
        {
            changed |= !s.MonitorNames.TryGetValue(id, out var last) || last != device;
            s.MonitorNames[id] = device;
        }
        if (!nowPresent.SetEquals(wasPresent))
        {
            s.MonitorsPresent = nowPresent.Order().ToList();
            changed = true;
        }
        return changed;
    }

    internal static MonitorSnapshot Take(StrataSettings s, string device)
    {
        return new MonitorSnapshot
        {
            Layout = s.Layouts.TryGetValue(device, out var layout) ? layout.Clone() : null,
            Main = s.TaskbarMainScreen == device,
            Fps = s.FpsScreen == device,
            Notifications = s.NotificationScreen == device,
            Screen = s.Screens.TryGetValue(device, out var screen) ? screen.Clone() : null,
            Widgets = s.Widgets.Where(w => w.Device == device).Select(w => w.Id).ToList(),
        };
    }

    private static void Clear(StrataSettings s, string device)
    {
        s.Layouts.Remove(device);
        s.Screens.Remove(device);
        if (s.TaskbarMainScreen == device)
            s.TaskbarMainScreen = null;
        if (s.FpsScreen == device)
            s.FpsScreen = null;
        if (s.NotificationScreen == device)
            s.NotificationScreen = null;
    }

    private static void Put(StrataSettings s, string device, MonitorSnapshot snap)
    {
        if (snap.Layout is { } layout)
            s.Layouts[device] = layout.Clone();
        if (snap.Screen is { } screen)
            s.Screens[device] = screen.Clone();
        if (snap.Main)
            s.TaskbarMainScreen = device;
        if (snap.Fps)
            s.FpsScreen = device;
        if (snap.Notifications)
            s.NotificationScreen = device;
        foreach (var widget in s.Widgets.Where(w => snap.Widgets.Contains(w.Id)))
            widget.Device = device;
    }

    /// <summary>Monitores conectados con su identificador de hardware ("" si Windows no lo da).</summary>
    public static List<(string Device, string Id)> Present() =>
        System.Windows.Forms.Screen.AllScreens.Select(sc => (sc.DeviceName, HardwareId(sc.DeviceName))).ToList();

    /// <summary>
    /// Ruta del monitor conectado a esa salida ("DISPLAY#BOE0A1C#4&amp;…&amp;UID8388688"): la misma
    /// cada vez para ese monitor en ese puerto, aunque cambie el nombre "\\.\DISPLAYn".
    /// </summary>
    private static string HardwareId(string device)
    {
        var info = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        if (!EnumDisplayDevices(device, 0, ref info, GetDeviceInterfaceName) || string.IsNullOrEmpty(info.DeviceId))
            return "";
        string id = info.DeviceId.TrimStart('\\', '?');
        int guid = id.LastIndexOf("#{", StringComparison.Ordinal);
        return guid > 0 ? id[..guid] : id;
    }

    private const uint GetDeviceInterfaceName = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
}
