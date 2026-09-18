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
    public async Task TestedChoicePinsItsFileHashSoADifferentPayloadIsRefused()
    {
        Assert.Equal("614741858a7def3fb21da385312fe7b114dd2873f6d79fb3d980a979af560ba0", Compatibility.TestedChoice.Sha256);
        using var client = new HttpClient(new FakeHandler(_ => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[512]) }));
        var target = Path.Combine(Path.GetTempPath(), "BlockTheSpot.Tests-" + Guid.NewGuid().ToString("N") + ".exe");
        await Assert.ThrowsAsync<InvalidDataException>(() => new Downloads(client).FileAsync(Compatibility.TestedChoice.Url, target, 0, false, null, CancellationToken.None, Compatibility.TestedChoice.Sha256));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void VersionHashComparisonIgnoresCase()
    {
        Compatibility.ValidateChoice(Compatibility.TestedChoice with { FullVersion = "1.2.93.667.G7B5cc0ce" }, false);
    }
}
