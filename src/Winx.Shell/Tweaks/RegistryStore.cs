using Microsoft.Win32;

namespace Winx.Shell.Tweaks;

/// <summary>Lectura/escritura del registro, siempre en la vista de 64 bits.</summary>
internal static class RegistryStore
{
    private static RegistryKey Base(Hive hive) => RegistryKey.OpenBaseKey(
        hive == Hive.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
        RegistryView.Registry64);

    public static (RegistryValueKind Kind, object Data)? Read(Hive hive, string path, string name)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path);
        if (key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } data)
            return null;
        return (key.GetValueKind(name), data);
    }

    public static void Write(Hive hive, string path, string name, RegistryValueKind kind, object data)
    {
        using var root = Base(hive);
        using var key = root.CreateSubKey(path, writable: true);
        key.SetValue(name, data, kind);
    }

    public static void DeleteValue(Hive hive, string path, string name)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public static void DeleteTree(Hive hive, string path)
    {
        using var root = Base(hive);
        root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }
}
