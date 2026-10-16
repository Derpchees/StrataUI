using Microsoft.Win32;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Mientras Strata UI muestra sus propios avisos, apaga los avisos emergentes de Windows para no verlos
/// dos veces. Es lo mismo que desactivar "Mostrar avisos de notificación" de cada app en
/// Configuración › Notificaciones: la notificación sigue llegando al centro (y por eso Strata UI la
/// sigue leyendo), solo que Windows no la muestra en pantalla. Con "Solo el sonido de Strata UI"
/// también apaga "Reproducir un sonido" de cada app (SoundFile vacío), para que suene solo el tono
/// de Strata UI.
/// </summary>
/// <remarks>
/// El valor original de cada app se guarda en la configuración antes de tocarlo y se devuelve al
/// salir de Strata UI, al desactivar su centro de notificaciones o con --restore-taskbar (si Strata UI
/// terminó mal, el respaldo sigue en settings.json). Una app que nunca había notificado no tiene
/// clave aún: se apaga en cuanto llega su primera notificación.
/// </remarks>
public static class WindowsBanners
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string ShowBanner = "ShowBanner";
    private const string SoundFile = "SoundFile";

    private static bool _active;
    private static bool _soundsMuted;

    /// <summary>Apaga o devuelve los avisos (y sonidos) de Windows según esté activo el centro de Strata UI.</summary>
    public static void Update()
    {
        var s = Settings.Current;
        if (s.NotificationsEnabled)
        {
            if (!s.MuteWindowsNotificationSounds && (_soundsMuted || s.WindowsSoundBackup.Count > 0))
                RestoreSounds();
            if (!_active || s.MuteWindowsNotificationSounds && !_soundsMuted)
                SuppressAll();
        }
        else if (_active || s.WindowsBannerBackup.Count > 0 || s.WindowsSoundBackup.Count > 0)
        {
            Restore();
        }
    }

    /// <summary>Apaga los avisos (y sonidos) de todas las apps que Windows conoce.</summary>
    public static void SuppressAll()
    {
        _active = true;
        _soundsMuted = Settings.Current.MuteWindowsNotificationSounds;
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
        var s = Settings.Current;
        if (!string.IsNullOrEmpty(appId)
            && (!s.WindowsBannerBackup.ContainsKey(appId) || _soundsMuted && !s.WindowsSoundBackup.ContainsKey(appId)))
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
        using var key = Registry.CurrentUser.CreateSubKey($@"{Root}\{app}", writable: true);
        bool changed = false;

        var banners = Settings.Current.WindowsBannerBackup;
        var banner = key.GetValue(ShowBanner) as int?;
        if (!banners.ContainsKey(app))
        {
            banners[app] = banner;
            changed = true;
        }
        if (banner != 0)
        {
            key.SetValue(ShowBanner, 0, RegistryValueKind.DWord);
            changed = true;
        }

        if (_soundsMuted)
        {
            var sounds = Settings.Current.WindowsSoundBackup;
            var sound = key.GetValue(SoundFile) as string;
            if (!sounds.ContainsKey(app))
            {
                sounds[app] = sound;
                changed = true;
            }
            if (sound != "")
            {
                key.SetValue(SoundFile, "", RegistryValueKind.String);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Devuelve a cada app los avisos y sonidos que tenía antes de Strata UI.</summary>
    public static void Restore()
    {
        _active = false;
        RestoreSounds();
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

    /// <summary>Devuelve a cada app el sonido de notificación que tenía antes de Strata UI.</summary>
    private static void RestoreSounds()
    {
        _soundsMuted = false;
        var backup = Settings.Current.WindowsSoundBackup;
        if (backup.Count == 0)
            return;
        foreach (var (app, original) in backup)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{app}", writable: true);
                if (key is null)
                    continue;
                if (original is not null)
                    key.SetValue(SoundFile, original, RegistryValueKind.String);
                else
                    key.DeleteValue(SoundFile, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo devolver el sonido de notificación de {app}", ex);
            }
        }
        Log.Info($"Sonidos de notificación de Windows devueltos ({backup.Count} apps)");
        backup.Clear();
        Settings.Save();
    }
}
