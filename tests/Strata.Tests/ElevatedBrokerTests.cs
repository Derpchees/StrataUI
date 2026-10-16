using Microsoft.Win32;
using Strata.Shell.Services;
using Strata.Shell.Tweaks;
using Xunit;

namespace Strata.Tests;

public class ElevatedBrokerTests
{
    private static readonly string BackupPath = TweakEngine.Backup.FilePath;

    [Fact]
    public void Applies_a_catalog_tweak_into_the_users_backup()
    {
        var tweak = TweakCatalog.All.First(t => t.RequiresAdmin);
        Assert.True(ElevatedBroker.IsAllowed(["--apply-tweak", tweak.Id, "0", BackupPath]));
    }

    [Theory]
    [InlineData("--apply-tweak", "no-existe", "0")]
    [InlineData("--apply-tweak", null, "99")]
    [InlineData("--apply-tweak", null, "-1")]
    public void Rejects_unknown_tweaks_and_choices(string arg, string? id, string state)
    {
        id ??= TweakCatalog.All.First().Id;
        Assert.False(ElevatedBroker.IsAllowed([arg, id, state, BackupPath]));
    }

    [Fact]
    public void Never_writes_the_backup_somewhere_else()
    {
        var tweak = TweakCatalog.All.First();
        Assert.False(ElevatedBroker.IsAllowed(["--apply-tweak", tweak.Id, "0", @"C:\Windows\System32\drivers\etc\hosts"]));
        Assert.False(ElevatedBroker.IsAllowed(["--restore-tweaks", @"C:\Users\Public\backup.json"]));
        Assert.True(ElevatedBroker.IsAllowed(["--restore-tweaks", BackupPath]));
    }

    [Theory]
    [InlineData("Microsoft.BingNews")]
    [InlineData("Clipchamp.Clipchamp")]
    public void Removes_store_apps_by_package_name(string name) =>
        Assert.True(ElevatedBroker.IsAllowed([ElevatedBroker.RemoveAppxArg, name]));

    [Theory]
    [InlineData("x'); Remove-Item C:\\ -Recurse; ('")]
    [InlineData("$(calc)")]
    [InlineData("")]
    public void Rejects_anything_that_is_not_a_package_name(string name) =>
        Assert.False(ElevatedBroker.IsAllowed([ElevatedBroker.RemoveAppxArg, name]));

    [Theory]
    [InlineData("powershell.exe")]
    [InlineData("--install-sensors-task")]
    [InlineData("--restore-taskbar")]
    public void Rejects_anything_outside_the_list(string arg) =>
        Assert.False(ElevatedBroker.IsAllowed([arg]));

    [Fact]
    public void Restores_only_catalog_keys()
    {
        var setting = TweakCatalog.All.SelectMany(t => t.Settings).First(s => s.Hive == Hive.LocalMachine);
        Assert.True(TweakBackup.InCatalog(new TweakBackup.Entry(setting.Hive, setting.Path, setting.Name, true,
            RegistryValueKind.DWord, "1", setting.OwnerKey)));
        Assert.False(TweakBackup.InCatalog(new TweakBackup.Entry(Hive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sethc.exe", "Debugger", true,
            RegistryValueKind.String, "cmd.exe", null)));
        Assert.False(TweakBackup.InCatalog(new TweakBackup.Entry(setting.Hive, setting.Path, setting.Name, false,
            RegistryValueKind.DWord, null, @"SOFTWARE\Microsoft\Windows Defender")));
    }
}
