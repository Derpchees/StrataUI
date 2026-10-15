using Strata.Shell.Services;
using Xunit;

namespace Strata.Tests;

public class TrayPromotionTests
{
    private const string Key = @"C:\Program Files\Mozilla Thunderbird\thunderbird.exe:2";

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Thunderbird\thunderbird.exe:2")]
    [InlineData("C:\\Program Files\\Mozilla Thunderbird\\thunderbird.exe:2:Thunderbird\n1 mensaje no leído")]
    [InlineData(@"c:\program files\mozilla thunderbird\THUNDERBIRD.EXE:2:Thunderbird")]
    public void ReconoceElIconoAunqueCambieSuTexto(string stored) =>
        Assert.True(TrayPromotion.Matches(stored, Key));

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Thunderbird\thunderbird.exe:21:Thunderbird")]
    [InlineData(@"C:\Program Files\Mozilla Thunderbird\thunderbird.exe:3")]
    [InlineData("7820ae75-23e3-4229-82c1-e41cb67d5b9c")]
    public void NoConfundeOtrosIconos(string stored) =>
        Assert.False(TrayPromotion.Matches(stored, Key));
}
