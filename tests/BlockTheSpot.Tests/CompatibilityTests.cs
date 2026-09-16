using BlockTheSpot.Core;
using Xunit;

namespace BlockTheSpot.Tests;

public sealed class CompatibilityTests
{
    [Fact]
    public void TestedDefaultIsPinnedAndCannotFollowLatestAutomatically()
    {
        Assert.Equal("1.2.93.667.g7b5cc0ce", Compatibility.TestedVersion);
        Compatibility.ValidateChoice(Compatibility.TestedChoice, false);
        Assert.Throws<InvalidOperationException>(() => Compatibility.ValidateChoice(SpotifyChoice.Latest, false));
        Assert.Throws<InvalidOperationException>(() => Compatibility.ValidateInstalled("1.3.1.223", false));
        Compatibility.ValidateChoice(SpotifyChoice.Latest, true);
    }

    [Fact]
    public void VersionHashComparisonIgnoresCase()
    {
        Compatibility.ValidateChoice(Compatibility.TestedChoice with { FullVersion = "1.2.93.667.G7B5cc0ce" }, false);
    }
}
