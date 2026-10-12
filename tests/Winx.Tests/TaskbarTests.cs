using System.Globalization;
using Winx.Shell;

namespace Winx.Tests;

public sealed class TaskbarTests
{
    [Theory]
    [InlineData("es-MX", "dd/MM")]
    [InlineData("en-US", "M/d")]
    [InlineData("de-DE", "dd.MM")]
    [InlineData("ja-JP", "MM/dd")]
    [InlineData("sv-SE", "MM-dd")]
    public void Vertical_bar_date_drops_the_year(string culture, string expected) =>
        Assert.Equal(expected, Taskbar.ShortDateWithoutYear(CultureInfo.GetCultureInfo(culture)));
}
