using System.Runtime.InteropServices;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Lo que anima Windows y no Strata UI (abrir, cerrar y minimizar las ventanas de las apps, y sus
/// menús) sigue a la animación elegida en el Hub: con "Sin animación" se apagan; con cualquier otra,
/// se encienden. No se puede cambiar cómo las anima Windows (no hay deslizar ni rebote para una
/// ventana ajena), solo si las anima.
/// </summary>
/// <remarks>
/// El valor original se guarda en la configuración antes de tocarlo y se devuelve al salir de Strata
/// UI o con --restore-taskbar (como los avisos de Windows en Notifications.WindowsBanners).
/// </remarks>
public static class WindowsAnimations
{
    private const uint GetClientAreaAnimation = 0x1042, SetClientAreaAnimation = 0x1043;
    private const uint GetMenuAnimation = 0x1002, SetMenuAnimation = 0x1003;
    private const uint GetAnimation = 0x0048, SetAnimation = 0x0049;
    private const uint UpdateIniFile = 0x01, SendChange = 0x02;

    /// <summary>Strata UI anima lo suyo: Windows también anima lo suyo.</summary>
    private static bool Wanted =>
        Settings.Current.PanelAnimation != PanelAnimation.None || Settings.Current.AnimationModule != null;

    /// <summary>Pone las animaciones de Windows como la elegida (al abrir y cuando cambia).</summary>
    public static void Update()
    {
        try
        {
            var current = Read();
            bool on = Wanted;
            var target = new[] { on, on, on };
            if (current.SequenceEqual(target))
                return;
            if (Settings.Current.WindowsAnimationBackup is null)
            {
                Settings.Current.WindowsAnimationBackup = current;
                Settings.Save();
            }
            Write(target);
            Log.Info($"Animaciones de Windows: {(on ? "activadas" : "desactivadas")}");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron cambiar las animaciones de Windows", ex);
        }
    }

    /// <summary>Devuelve las animaciones de Windows como estaban antes de Strata UI.</summary>
    public static void Restore()
    {
        if (Settings.Current.WindowsAnimationBackup is not { Length: 3 } backup)
            return;
        try
        {
            Write(backup);
            Log.Info("Animaciones de Windows devueltas");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron devolver las animaciones de Windows", ex);
        }
        Settings.Current.WindowsAnimationBackup = null;
        Settings.Save();
    }

    /// <summary>[efectos de animación (ventanas), menús, minimizar y maximizar].</summary>
    private static bool[] Read()
    {
        SystemParametersInfo(GetClientAreaAnimation, 0, out bool client, 0);
        SystemParametersInfo(GetMenuAnimation, 0, out bool menus, 0);
        var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
        SystemParametersInfo(GetAnimation, info.Size, ref info, 0);
        return [client, menus, info.MinAnimate != 0];
    }

    private static void Write(bool[] values)
    {
        SystemParametersInfo(SetClientAreaAnimation, 0, values[0] ? 1 : IntPtr.Zero, UpdateIniFile | SendChange);
        SystemParametersInfo(SetMenuAnimation, 0, values[1] ? 1 : IntPtr.Zero, UpdateIniFile | SendChange);
        var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = values[2] ? 1 : 0 };
        SystemParametersInfo(SetAnimation, info.Size, ref info, UpdateIniFile | SendChange);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo value, uint winIni);
}
