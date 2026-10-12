using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winx.Shell.Services;

public enum TaskbarAlignment { Center, Left }

public enum WinxTheme { System, Light, Dark, Transparent, LiquidGlass, LiquidGlassDark, Material }

public enum StartMenuStyle { Windows11, List, Fullscreen, Compact, Spotlight, Icons }

public enum StartIconKind { Winx, Windows, WindowsMono, Circle, Dot, Grid, Search }

/// <summary>Preferencias de WINX, guardadas en %LOCALAPPDATA%\WINX\settings.json.</summary>
public sealed class WinxSettings
{
    // Barra de tareas
    public TaskbarAlignment TaskbarAlignment { get; set; } = TaskbarAlignment.Center;
    public int TaskbarHeight { get; set; } = 48;
    public bool ShowSeconds { get; set; }
    public bool ShowDate { get; set; } = true;

    // Menú Inicio
    public StartMenuStyle StartStyle { get; set; } = StartMenuStyle.Windows11;
    public bool StartShowRecent { get; set; } = true;
    public bool StartShowPinned { get; set; } = true;
    public bool StartShowAllApps { get; set; } = true;
    public bool StartShowUser { get; set; } = true;
    public bool StartShowUserPicture { get; set; } = true;
    public StartIconKind StartIcon { get; set; } = StartIconKind.Winx;
    /// <summary>Botones del pie del menú, en orden (IDs de StartFooterCatalog).</summary>
    public List<string> StartFooterButtons { get; set; } = ["Explorer", "WinxSettings", "Power"];
    public bool StartUseWinKey { get; set; } = true;
    /// <summary>IDs de apps ancladas (AppUserModelID / nombre de análisis); null = primera vez.</summary>
    public List<string>? PinnedStartApps { get; set; }

    // Módulos del área de estado
    public bool ShowNetworkModule { get; set; } = true;
    public bool ShowBluetoothModule { get; set; } = true;
    public bool ShowVolumeModule { get; set; } = true;
    public bool ShowBatteryModule { get; set; } = true;

    // Apariencia
    public WinxTheme Theme { get; set; } = WinxTheme.System;
    /// <summary>Color "#RRGGBB"; null = usar el acento de Windows.</summary>
    public string? AccentColor { get; set; }
    /// <summary>Liquid Glass con refracción real (captura del fondo + shader). Oculta la barra y
    /// los paneles de las capturas de pantalla; desactivado usa el desenfoque de Windows.</summary>
    public bool LiveGlass { get; set; } = true;
    /// <summary>Llevar el tema a Windows: modo claro/oscuro y fondo de las ventanas del Explorador.</summary>
    public bool ApplyThemeToWindows { get; set; }
    /// <summary>Fondo que había antes de que WINX lo cambiara por primera vez.</summary>
    public string? OriginalWallpaper { get; set; }

    // Bandeja: null = primera ejecución (se importan los íconos promovidos en Windows).
    public string[]? PinnedTrayIcons { get; set; }
    public List<string> KnownTrayIcons { get; set; } = new();
}

public static class Settings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINX", "settings.json");

    public static WinxSettings Current { get; private set; } = Load();

    /// <summary>Se dispara tras cada <see cref="Update"/>; la UI se sincroniza escuchándolo.</summary>
    public static event Action? Changed;

    public static void Update(Action<WinxSettings> change)
    {
        change(Current);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Guarda sin notificar (para datos internos como los íconos conocidos).</summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Error("No se pudo guardar la configuración", ex);
        }
    }

    private static WinxSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WinxSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("Configuración inválida; se usan valores por defecto", ex);
        }
        return new();
    }
}
