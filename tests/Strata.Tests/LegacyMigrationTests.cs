using Strata.Shell.Services;

namespace Strata.Tests;

/// <summary>Configuración guardada cuando la app se llamaba WINX.</summary>
public sealed class LegacyMigrationTests
{
    [Fact]
    public void Renamed_values_and_data_paths_are_fixed()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace(@"\", @"\\");
        string json = $$"""
        {
          "StartIcon": "Winx",
          "StartFooterButtons": [ "Explorer", "WinxSettings", "Power" ],
          "StartCustomIcons": { "app": "{{local}}\\WINX\\start-icons\\a.png" },
          "OriginalWallpaper": "C:\\Fotos\\WINX\\fondo.jpg"
        }
        """;
        string fixedJson = LegacyMigration.FixSettingsText(json);
        Assert.Contains("\"StartIcon\": \"Strata\"", fixedJson);
        Assert.Contains("\"StrataSettings\"", fixedJson);
        Assert.Contains(local + @"\\StrataUI\\start-icons", fixedJson);
        // Otra carpeta que se llame WINX no es la de datos.
        Assert.Contains(@"C:\\Fotos\\WINX\\fondo.jpg", fixedJson);
    }
}
