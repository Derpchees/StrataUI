using Strata.Shell.Services.Brightness;

namespace Strata.Tests;

/// <summary>El valor de CloudStore de la luz nocturna, con el de una PC real (encendida).</summary>
public sealed class NightLightTests
{
    private static readonly byte[] Enabled = Convert.FromHexString(
        "434201000A0201002A069BD48FD6062A2B0E15434201001000D00A02C6149DC4C38FE09ED5EE0100000000");

    [Fact]
    public void Reads_the_state()
    {
        Assert.True(NightLight.IsEnabled(Enabled));
        Assert.False(NightLight.IsEnabled(NightLight.With(Enabled, on: false, 1_791_224_347)!));
        Assert.Null(NightLight.IsEnabled([0x43, 0x42, 0x01]));
    }

    [Fact]
    public void Turning_off_and_on_again_gives_back_the_same_value()
    {
        var off = NightLight.With(Enabled, on: false, 1_791_224_347)!;
        Assert.Equal(Enabled.Length - 2, off.Length);
        Assert.Equal(0x13, off[18]);
        // Volver a encenderla deja el valor original, salvo la hora (bytes 10 a 14), que siempre avanza.
        var again = NightLight.With(off, on: true, 1_791_224_347)!;
        static byte[] WithoutTime(byte[] data) => [.. data[..10], .. data[15..]];
        Assert.Equal(WithoutTime(Enabled), WithoutTime(again));
    }

    [Fact]
    public void Two_changes_in_the_same_second_get_increasing_times()
    {
        // La hora del valor es 1 791 224 347: un cambio "antes" o en el mismo segundo va después.
        Assert.Equal(1_791_224_347, NightLight.ReadVarint(Enabled, 10));
        var off = NightLight.With(Enabled, on: false, 1_791_224_347)!;
        var on = NightLight.With(off, on: true, 1_791_224_347)!;
        Assert.Equal(1_791_224_348, NightLight.ReadVarint(off, 10));
        Assert.Equal(1_791_224_349, NightLight.ReadVarint(on, 10));
    }

    [Fact]
    public void Creates_the_value_on_a_pc_that_never_used_night_light()
    {
        var initial = NightLight.Initial(DateTimeOffset.FromUnixTimeSeconds(1_791_224_347));
        Assert.Equal(41, initial.Length);
        Assert.False(NightLight.IsEnabled(initial));
        Assert.Equal(1_791_224_347, NightLight.ReadVarint(initial, 10));

        // Encendida queda igual que la de un equipo real, salvo las dos horas.
        var on = NightLight.With(initial, on: true, 1_791_224_347)!;
        static byte[] WithoutTimes(byte[] data) => [.. data[..10], .. data[15..30], .. data[39..]];
        Assert.Equal(WithoutTimes(Enabled), WithoutTimes(on));
    }

    [Fact]
    public void Writes_the_time_as_a_varint()
    {
        Assert.Equal(Convert.FromHexString("9BD48FD606"), NightLight.Varint(1_791_224_347));
        var later = NightLight.With(Enabled, on: true, 1_791_224_348)!;
        Assert.Equal(0x9C, later[10]);
        Assert.Equal(Enabled.Length, later.Length);
    }
}
