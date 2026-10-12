using Microsoft.Win32;

namespace Winx.Shell.Services.Notifications;

/// <summary>
/// Mientras WINX muestra sus propios avisos, apaga los avisos emergentes de Windows para no verlos
/// dos veces. Es lo mismo que desactivar "Mostrar avisos de notificación" de cada app en
/// Configuración › Notificaciones: la notificación sigue llegando al centro (y por eso WINX la
/// sigue leyendo), solo que Windows no la muestra en pantalla.
/// </summary>
/// <remarks>
/// El valor original de cada app se guarda en la configuración antes de tocarlo y se devuelve al
/// salir de WINX, al desactivar su centro de notificaciones o con --restore-taskbar (si WINX
/// terminó mal, el respaldo sigue en settings.json). Una app que nunca había notificado no tiene
/// clave aún: se apaga en cuanto llega su primera notificación.
/// </remarks>
public static class WindowsBanners
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string ShowBanner = "ShowBanner";

    private static bool _active;

    /// <summary>Apaga o devuelve los avisos de Windows según esté activo el centro de WINX.</summary>
    public static void Update()
    {
        if (Settings.Current.NotificationsEnabled)
        {
            if (!_active)
                SuppressAll();
        }
        else if (_active || Settings.Current.WindowsBannerBackup.Count > 0)
        {
            Restore();
        }
    }

    /// <summary>Apaga los avisos de todas las apps que Windows conoce.</summary>
    public static void SuppressAll()
    {
        _active = true;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root);
            if (root is null)
                return;
            bool changed = false;
            foreach (var app in root.GetSubKeyNames())
                changed |= SuppressCore(app);
            if (changed)
                Settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron apagar los avisos de Windows", ex);
        }
    }

    /// <summary>Una app nueva notificó: se apagan sus avisos (y los de cualquier otra que haya aparecido).</summary>
    public static void OnNotification(string? appId)
    {
        if (!_active)
            return;
        if (!string.IsNullOrEmpty(appId) && !Settings.Current.WindowsBannerBackup.ContainsKey(appId))
        {
            try
            {
                if (SuppressCore(appId))
                    Settings.Save();
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudieron apagar los avisos de Windows de {appId}", ex);
            }
        }
        SuppressAll();
    }

    /// <summary>True si se cambió algo (para guardar el respaldo).</summary>
    private static bool SuppressCore(string app)
    {
        var backup = Settings.Current.WindowsBannerBackup;
        using var key = Registry.CurrentUser.CreateSubKey($@"{Root}\{app}", writable: true);
        var current = key.GetValue(ShowBanner) as int?;
        if (backup.ContainsKey(app) && current == 0)
            return false;
        if (!backup.ContainsKey(app))
            backup[app] = current;
        if (current != 0)
            key.SetValue(ShowBanner, 0, RegistryValueKind.DWord);
        return true;
    }

    /// <summary>Devuelve a cada app el valor que tenía antes de WINX.</summary>
    public static void Restore()
    {
        _active = false;
        var backup = Settings.Current.WindowsBannerBackup;
        if (backup.Count == 0)
            return;
        foreach (var (app, original) in backup)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{app}", writable: true);
                if (key is null)
                    continue;
                if (original is int value)
                    key.SetValue(ShowBanner, value, RegistryValueKind.DWord);
                else
                    key.DeleteValue(ShowBanner, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudieron devolver los avisos de Windows de {app}", ex);
            }
        }
        Log.Info($"Avisos de Windows devueltos ({backup.Count} apps)");
        backup.Clear();
        Settings.Save();
    }
}
