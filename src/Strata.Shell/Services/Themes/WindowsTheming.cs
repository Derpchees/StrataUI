using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Strata.Shell.Tweaks;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Lleva el tema de Strata UI a Windows sin inyectar código (nivel 1):
/// modo claro/oscuro y transparencia del sistema, y material de fondo + barra de título
/// oscura en las ventanas del Explorador y del Panel de control (clase CabinetWClass).
/// </summary>
public static class WindowsTheming
{
    private const string ExplorerClass = "CabinetWClass";
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0;

    private static IntPtr _hook;
    private static WinEventProc? _callback; // referencia viva para que el GC no recoja el delegado
    private static StrataTheme? _appliedSystemTheme;

    public static void Initialize()
    {
        ThemeManager.ThemeChanged += Update;
        Settings.Changed += Update; // el interruptor no cambia el tema, pero hay que reaccionar
        Update();
    }

    public static void Shutdown()
    {
        ThemeManager.ThemeChanged -= Update;
        Settings.Changed -= Update;
        StopWatching();
        ForEachExplorerWindow(ResetWindow);
    }

    private static void Update()
    {
        if (!Settings.Current.ApplyThemeToWindows)
        {
            if (_hook != IntPtr.Zero)
            {
                StopWatching();
                ForEachExplorerWindow(ResetWindow);
            }
            _appliedSystemTheme = null;
            RestoreAccent();
            return;
        }

        if (_hook == IntPtr.Zero)
            StartWatching();
        ForEachExplorerWindow(ApplyToWindow);
        _ = ApplySystemModeAsync();
        ApplyAccent();
    }

    // ── Color de acento de Windows ─────────────────────────────────────────

    /// <summary>
    /// Con un acento propio o del fondo, Windows usa el mismo (Inicio de Windows, Configuración,
    /// barras de título, apps). Se guarda el que tenía para devolverlo. No se toca mientras
    /// Wallpaper Engine ajusta el color de Windows: los dos se pisarían con cada fondo.
    /// </summary>
    private static void ApplyAccent()
    {
        if (ThemeManager.FollowsWindowsAccent || WallpaperEngine.AdjustingWindowsColor)
        {
            RestoreAccent();
            return;
        }
        var accent = ThemeManager.Accent;
        uint wanted = Abgr(accent);
        if (GetUserColorPreference(out var current, false) != 0)
            return;
        if (current.Accent == wanted)
            return;
        if (Settings.Current.WindowsAccentBackup is null)
        {
            Settings.Current.WindowsAccentBackup = [current.Start, current.Accent];
            Settings.Save();
        }
        // Inicio de Windows con un tono más oscuro del acento, como hace Windows.
        var start = Palette.Blend(accent, Colors.Black, 0.45);
        SetAccent(new ColorPreference { Start = Abgr(start), Accent = wanted });
    }

    private static void RestoreAccent()
    {
        if (Settings.Current.WindowsAccentBackup is not [var start, var accent])
            return;
        if (!WallpaperEngine.AdjustingWindowsColor)
            SetAccent(new ColorPreference { Start = start, Accent = accent });
        Settings.Current.WindowsAccentBackup = null;
        Settings.Save();
    }

    /// <summary>
    /// SetUserColorPreference (uxtheme, ordinal 122) es lo que usa Configuración de Windows: guarda
    /// el acento, calcula la paleta y avisa a todo el sistema (DWM, Inicio, apps).
    /// </summary>
    private static void SetAccent(ColorPreference preference)
    {
        try
        {
            int hr = SetUserColorPreference(ref preference, true);
            if (hr != 0)
                Log.Info($"Windows no aceptó el color de acento (0x{hr:X8})");
            else
                Log.Info($"Acento de Windows: {preference.Accent:X8}");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Error("Esta versión de Windows no permite cambiar el acento", ex);
        }
    }

    private static uint Abgr(Color color) => 0xFF000000u | (uint)color.B << 16 | (uint)color.G << 8 | color.R;

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorPreference
    {
        public uint Start;
        public uint Accent;
    }

    [DllImport("uxtheme.dll", EntryPoint = "#120")]
    private static extern int GetUserColorPreference(out ColorPreference preference, bool forceReload);

    [DllImport("uxtheme.dll", EntryPoint = "#122")]
    private static extern int SetUserColorPreference(ref ColorPreference preference, bool forceCommit);

    /// <summary>Modo claro/oscuro y transparencia del sistema (con respaldo, vía el motor de ajustes).</summary>
    private static async Task ApplySystemModeAsync()
    {
        var theme = ThemeManager.Theme;
        if (_appliedSystemTheme == theme.Kind)
            return;
        _appliedSystemTheme = theme.Kind;

        if (theme.Light is bool light)
        {
            await ApplyTweakAsync("dark-apps", light ? 0 : 1);
            await ApplyTweakAsync("dark-system", light ? 0 : 1);
        }
        if (theme.Backdrop == Backdrop.Acrylic)
            await ApplyTweakAsync("no-transparency", 0); // transparencia de Windows activada
    }

    private static async Task ApplyTweakAsync(string id, int state)
    {
        var tweak = TweakCatalog.All.Single(t => t.Id == id);
        if (TweakEngine.CurrentState(tweak) != state)
            await TweakEngine.ApplyAsync(tweak, state);
    }

    private static void ApplyToWindow(IntPtr hwnd)
    {
        var theme = ThemeManager.Theme;
        WindowEffects.SetDarkMode(hwnd, !ThemeManager.IsLight);
        WindowEffects.SetSystemBackdrop(hwnd, theme.Backdrop switch
        {
            Backdrop.Acrylic => WindowEffects.BackdropAcrylic,
            Backdrop.Mica => WindowEffects.BackdropMica,
            _ => WindowEffects.BackdropNone,
        });
    }

    private static void ResetWindow(IntPtr hwnd) =>
        WindowEffects.SetSystemBackdrop(hwnd, WindowEffects.BackdropAuto);

    private static void StartWatching()
    {
        _callback = OnWinEvent;
        _hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
    }

    private static void StopWatching()
    {
        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _callback = null;
    }

    private static void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == 0 && idChild == 0 && IsExplorerWindow(hwnd))
            ApplyToWindow(hwnd);
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        return GetClassName(hwnd, name, name.Capacity) > 0 && name.ToString() == ExplorerClass;
    }

    private static void ForEachExplorerWindow(Action<IntPtr> action) =>
        EnumWindows((hwnd, _) =>
        {
            if (IsExplorerWindow(hwnd))
                action(hwnd);
            return true;
        }, IntPtr.Zero);

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback,
                                                 uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
}
