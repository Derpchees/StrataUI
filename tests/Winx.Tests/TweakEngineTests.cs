using System.IO;
using Microsoft.Win32;
using Winx.Shell.Tweaks;

namespace Winx.Tests;

/// <summary>
/// Prueba el motor de ajustes contra una clave aislada (HKCU\Software\WINX-Tests),
/// sin tocar ninguna configuración real de Windows.
/// </summary>
public sealed class TweakEngineTests : IDisposable
{
    private const string Root = @"Software\WINX-Tests";
    private readonly string _backupPath = Path.Combine(Path.GetTempPath(), $"winx-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Root, throwOnMissingSubKey: false);
        File.Delete(_backupPath);
    }

    private static Tweak MakeTweak(params RegSetting[] settings) =>
        new("test", "test", "Prueba", "", settings);

    private static object? Read(string path, string name) =>
        Registry.CurrentUser.OpenSubKey(path)?.GetValue(name);

    [Fact]
    public void Missing_value_uses_default_to_compute_state()
    {
        var tweak = MakeTweak(new RegSetting(Hive.CurrentUser, Root, "Flag", [1, 0], 1));
        Assert.Equal(0, TweakEngine.CurrentState(tweak));
    }

    [Fact]
    public void Apply_writes_value_and_state_follows()
    {
        var tweak = MakeTweak(new RegSetting(Hive.CurrentUser, Root, "Flag", [1, 0], 1));
        TweakEngine.ApplyLocal(tweak, 1, new TweakBackup(_backupPath));

        Assert.Equal(0, Read(Root, "Flag"));
        Assert.Equal(1, TweakEngine.CurrentState(tweak));
    }

    [Fact]
    public void String_values_compare_with_numbers_in_states()
    {
        var tweak = MakeTweak(new RegSetting(Hive.CurrentUser, Root, "Speed", ["1", "0"], "1", RegistryValueKind.String));
        TweakEngine.ApplyLocal(tweak, 1, new TweakBackup(_backupPath));

        Assert.Equal("0", Read(Root, "Speed"));
        Assert.Equal(1, TweakEngine.CurrentState(tweak));
    }

    [Fact]
    public void Restore_brings_back_original_value_and_deletes_created_ones()
    {
        Registry.CurrentUser.CreateSubKey(Root).SetValue("Existing", 7, RegistryValueKind.DWord);
        var tweak = MakeTweak(
            new RegSetting(Hive.CurrentUser, Root, "Existing", [7, 0], 7),
            new RegSetting(Hive.CurrentUser, Root, "Policy", [null, 1]));

        var backup = new TweakBackup(_backupPath);
        TweakEngine.ApplyLocal(tweak, 1, backup);
        Assert.Equal(0, Read(Root, "Existing"));
        Assert.Equal(1, Read(Root, "Policy"));

        var failed = new TweakBackup(_backupPath).Restore(Hive.CurrentUser);

        Assert.Empty(failed);
        Assert.Equal(7, Read(Root, "Existing"));
        Assert.Null(Read(Root, "Policy"));
    }

    [Fact]
    public void Backup_keeps_the_first_original_across_repeated_changes()
    {
        Registry.CurrentUser.CreateSubKey(Root).SetValue("Mode", 2, RegistryValueKind.DWord);
        var tweak = MakeTweak(new RegSetting(Hive.CurrentUser, Root, "Mode", [2, 1, 3], 2));
        var backup = new TweakBackup(_backupPath);

        TweakEngine.ApplyLocal(tweak, 1, backup);
        TweakEngine.ApplyLocal(tweak, 2, backup);
        new TweakBackup(_backupPath).Restore(Hive.CurrentUser);

        Assert.Equal(2, Read(Root, "Mode"));
    }

    [Fact]
    public void Null_state_with_owner_key_removes_the_whole_key()
    {
        string owner = Root + @"\Owner";
        var tweak = MakeTweak(new RegSetting(Hive.CurrentUser, owner + @"\Inproc", "", [null, ""], null,
                                             RegistryValueKind.String, owner));
        var backup = new TweakBackup(_backupPath);

        TweakEngine.ApplyLocal(tweak, 1, backup);
        Assert.NotNull(Registry.CurrentUser.OpenSubKey(owner + @"\Inproc"));
        Assert.Equal(1, TweakEngine.CurrentState(tweak));

        TweakEngine.ApplyLocal(tweak, 0, backup);
        Assert.Null(Registry.CurrentUser.OpenSubKey(owner));
        Assert.Equal(0, TweakEngine.CurrentState(tweak));
    }
}

public sealed class TweakCatalogTests
{
    [Fact]
    public void Ids_are_unique() =>
        Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct().Count());

    [Fact]
    public void Every_setting_has_one_state_per_option()
    {
        foreach (var tweak in TweakCatalog.All)
            foreach (var setting in tweak.Settings)
                Assert.True(setting.States.Length == tweak.Choices.Length,
                    $"{tweak.Id}: {setting.Name} tiene {setting.States.Length} estados para {tweak.Choices.Length} opciones");
    }

    [Fact]
    public void Every_category_has_a_page()
    {
        var pageCategories = new[]
        {
            TweakCatalog.Privacy, TweakCatalog.AI, TweakCatalog.System, TweakCatalog.Update, TweakCatalog.Appearance,
            TweakCatalog.Explorer, TweakCatalog.Multitasking, TweakCatalog.Gaming, TweakCatalog.Browsers,
        };
        Assert.All(TweakCatalog.All, t => Assert.Contains(t.Category, pageCategories));
    }

    [Fact]
    public void Owner_keys_contain_their_setting()
    {
        foreach (var setting in TweakCatalog.All.SelectMany(t => t.Settings).Where(s => s.OwnerKey != null))
            Assert.StartsWith(setting.OwnerKey!, setting.Path, StringComparison.OrdinalIgnoreCase);
    }
}
