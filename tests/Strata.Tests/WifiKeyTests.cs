using Strata.Shell.Services.Network;
using Xunit;

namespace Strata.Tests;

public class WifiKeyTests
{
    private static string Profile(string security) =>
        $"""
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>Casa</name>
          <MSM><security>{security}</security></MSM>
        </WLANProfile>
        """;

    [Fact]
    public void Clave_en_claro()
    {
        var read = WifiKey.ParseProfile(Profile(
            "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>secreta123</keyMaterial></sharedKey>"));
        Assert.Equal(WifiKey.ReadStatus.Ok, read.Status);
        Assert.Equal("secreta123", read.Key);
    }

    [Fact]
    public void Clave_protegida_pide_administrador()
    {
        var read = WifiKey.ParseProfile(Profile(
            "<sharedKey><keyType>passPhrase</keyType><protected>true</protected><keyMaterial>01000000D08C9DDF</keyMaterial></sharedKey>"));
        Assert.Equal(WifiKey.ReadStatus.Protected, read.Status);
    }

    [Fact]
    public void Red_abierta_sin_clave()
    {
        var read = WifiKey.ParseProfile(Profile("<authEncryption><authentication>open</authentication></authEncryption>"));
        Assert.Equal(WifiKey.ReadStatus.Ok, read.Status);
        Assert.Equal("", read.Key);
    }

    [Fact]
    public void Qr_escapa_caracteres_especiales()
    {
        Assert.Equal(@"WIFI:T:WPA;S:Mi\;Red;P:a\:b\\c\,d\"";;", WifiQr.Payload("Mi;Red", "a:b\\c,d\""));
        Assert.Equal("WIFI:T:nopass;S:Libre;;", WifiQr.Payload("Libre", ""));
    }

    [Fact]
    public void Xml_roto()
    {
        Assert.Equal(WifiKey.ReadStatus.Failed, WifiKey.ParseProfile("<no").Status);
    }
}
