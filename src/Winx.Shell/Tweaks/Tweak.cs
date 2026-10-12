using Microsoft.Win32;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Tweaks;

public enum Hive { CurrentUser, LocalMachine }

/// <summary>Cómo se hace efectivo un ajuste después de escribir el registro.</summary>
public enum TweakRefresh { Broadcast, Theme, Explorer, Mouse, Snap, StickyKeys }

/// <summary>
/// Un valor de registro controlado por un ajuste. <see cref="States"/> tiene una entrada por opción
/// del ajuste; null significa "borrar el valor" (y la clave <see cref="OwnerKey"/>, si hay).
/// <see cref="Default"/> es lo que Windows asume cuando el valor no existe.
/// </summary>
public sealed record RegSetting(
    Hive Hive,
    string Path,
    string Name,
    object?[] States,
    object? Default = null,
    RegistryValueKind Kind = RegistryValueKind.DWord,
    string? OwnerKey = null)
{
    public string Key => $"{(Hive == Hive.LocalMachine ? "HKLM" : "HKCU")}\\{Path}\\{Name}";
}

public sealed record Tweak(
    string Id,
    string Category,
    string Title,
    string Description,
    RegSetting[] Settings,
    string[]? Options = null,
    TweakRefresh Refresh = TweakRefresh.Broadcast)
{
    public static readonly string[] Toggle = [Loc.T("Desactivado"), Loc.T("Activado")];

    public string[] Choices => Options ?? Toggle;
    public bool IsToggle => Options is null;
    public bool RequiresAdmin => Settings.Any(s => s.Hive == Hive.LocalMachine);
}
