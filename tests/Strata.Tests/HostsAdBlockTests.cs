using Strata.Shell.Tweaks;
using Xunit;

namespace Strata.Tests;

public class HostsAdBlockTests
{
    private const string Original = "# hosts de Windows\r\n127.0.0.1 mi-servidor\r\n";

    [Fact]
    public void Adds_and_removes_only_its_block()
    {
        var on = HostsAdBlock.Apply(Original, true);
        Assert.StartsWith(Original, on);
        Assert.Contains("0.0.0.0 doubleclick.net\r\n", on);
        Assert.Equal(on, HostsAdBlock.Apply(on, true));
        Assert.Equal(Original, HostsAdBlock.Apply(on, false));
    }

    [Fact]
    public void Broker_accepts_only_on_or_off()
    {
        Assert.True(ElevatedBroker.IsAllowed([HostsAdBlock.Arg, "on"]));
        Assert.True(ElevatedBroker.IsAllowed([HostsAdBlock.Arg, "off"]));
        Assert.False(ElevatedBroker.IsAllowed([HostsAdBlock.Arg, "evil.com"]));
        Assert.False(ElevatedBroker.IsAllowed([HostsAdBlock.Arg]));
    }
}
