using Strata.Shell.Services.StartMenu;

namespace Strata.Tests;

/// <summary>Un juego de Steam y su acceso directo (sin ":" en el nombre) son la misma app.</summary>
public sealed class ExtraAppsTests
{
    [Theory]
    [InlineData("Black Myth: Wukong", "Black Myth Wukong")]
    [InlineData("Warhammer 40,000: Space Marine 2", "Warhammer 40,000 Space Marine 2")]
    [InlineData("Marvel's Spider-Man 2", "Marvel’s Spider Man 2")]
    [InlineData("Pokémon", "POKEMON")]
    public void SameGame(string a, string b) => Assert.Equal(ExtraApps.NameKey(a), ExtraApps.NameKey(b));

    [Theory]
    [InlineData("Half-Life 2", "Half-Life")]
    [InlineData("DOOM", "DOOM Eternal")]
    public void DifferentGames(string a, string b) => Assert.NotEqual(ExtraApps.NameKey(a), ExtraApps.NameKey(b));
}
