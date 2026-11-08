using Microsoft.Win32;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Opciones de Configuración › Pantalla › Gráficos que Windows guarda juntas en un solo texto
/// ("SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;"). Se cambia una clave sin tocar las demás.
/// Solo del usuario actual: no pide permiso de administrador. Los juegos las leen al abrirse.
/// </summary>
public static class DirectXSettings
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ValueName = "DirectXUserGlobalSettings";

    /// <summary>Optimizaciones para juegos en ventana (modelo de presentación moderno).</summary>
    public const string WindowedGames = "SwapEffectUpgradeEnable";

    /// <summary>Frecuencia variable también en juegos que no la admiten.</summary>
    public const string VariableRefresh = "VRROptimizeEnable";

    /// <summary>Si la opción está activa; sin escribir, lo que Windows usa por defecto.</summary>
    public static bool Get(string name)
    {
        var parts = Parse(Read());
        return parts.TryGetValue(name, out var value) ? value == "1" : DefaultOf(name);
    }

    // Desde Windows 11 24H2 (compilación 26100) las optimizaciones para juegos en ventana vienen activadas.
    private static bool DefaultOf(string name) =>
        name == WindowedGames && Environment.OSVersion.Version.Build >= 26100;

    public static void Set(string name, bool on)
    {
        var parts = Parse(Read());
        parts[name] = on ? "1" : "0";
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, string.Concat(parts.Select(p => $"{p.Key}={p.Value};")), RegistryValueKind.String);
    }

    private static string Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string ?? "";
    }

    // Conserva el orden y las claves que no conocemos.
    internal static Dictionary<string, string> Parse(string text)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = item.IndexOf('=');
            if (eq > 0)
                parts[item[..eq].Trim()] = item[(eq + 1)..].Trim();
        }
        return parts;
    }
}
