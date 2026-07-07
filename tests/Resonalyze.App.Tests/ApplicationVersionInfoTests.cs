namespace Resonalyze.App.Tests;

public sealed class ApplicationVersionInfoTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.1")]
    [InlineData("1.2.0-rc.1", "1.2.0-rc.2")]
    [InlineData("1.2.0-rc.1", "1.2.0")]
    [InlineData("1.2.0-alpha", "1.2.0-beta")]
    // Numeric identifiers compare numerically, not as text.
    [InlineData("1.2.0-alpha.9", "1.2.0-alpha.10")]
    // A shorter identifier list sorts before a longer one with the same prefix.
    [InlineData("1.2.0-rc", "1.2.0-rc.1")]
    // Numeric identifiers sort before alphanumeric ones.
    [InlineData("1.2.0-1", "1.2.0-alpha")]
    [InlineData("v1.2.0-rc.1", "v1.2.0-rc.2")]
    // Build metadata is ignored.
    [InlineData("1.2.0-rc.2+build.5", "1.2.0-rc.3")]
    public void IsOlderThan_DetectsAnAvailableUpdate(string current, string other)
    {
        Assert.True(ApplicationVersionInfo.IsOlderThan(current, other));
    }

    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2.1", "1.2.0")]
    [InlineData("1.2.0-rc.1", "1.2.0-rc.1")]
    [InlineData("1.2.0-rc.2", "1.2.0-rc.1")]
    // A release is never older than any prerelease of the same core.
    [InlineData("1.2.0", "1.2.0-rc.9")]
    [InlineData("1.2.1-rc.1", "1.2.0")]
    public void IsOlderThan_DoesNotPromptForSameOrOlder(string current, string other)
    {
        Assert.False(ApplicationVersionInfo.IsOlderThan(current, other));
    }

    [Fact]
    public void IsOlderThan_KeepsDevelopmentBuildsQuietOnTheSameCore()
    {
        Assert.False(ApplicationVersionInfo.IsOlderThan("1.2.0-dev.5", "1.2.0"));
        Assert.False(ApplicationVersionInfo.IsOlderThan("1.2.0-dev.5", "1.2.0-rc.1"));
        // A genuinely newer core still prompts.
        Assert.True(ApplicationVersionInfo.IsOlderThan("1.2.0-dev.5", "1.2.1"));
    }

    [Theory]
    [InlineData("garbage", "1.2.0")]
    [InlineData("1.2.0", "garbage")]
    [InlineData("", "1.2.0")]
    [InlineData("1.2.0", "")]
    public void IsOlderThan_TreatsUnparsableVersionsAsUpToDate(string current, string other)
    {
        Assert.False(ApplicationVersionInfo.IsOlderThan(current, other));
    }
}
