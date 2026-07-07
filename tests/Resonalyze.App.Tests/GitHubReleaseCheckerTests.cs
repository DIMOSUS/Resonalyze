namespace Resonalyze.App.Tests;

public sealed class GitHubReleaseCheckerTests
{
    [Theory]
    [InlineData("https://github.com/DIMOSUS/Resonalyze/releases/tag/v0.2.0")]
    [InlineData("https://github.com/DIMOSUS/Resonalyze/releases")]
    [InlineData("HTTPS://GITHUB.COM/DIMOSUS/Resonalyze/releases")]
    public void IsTrustedReleaseUrl_AcceptsHttpsGitHubUrls(string url)
    {
        Assert.True(GitHubReleaseChecker.IsTrustedReleaseUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://github.com/DIMOSUS/Resonalyze/releases")]
    [InlineData("https://github.com.evil.example/DIMOSUS/Resonalyze")]
    [InlineData("https://evil.example/github.com")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("releases/tag/v0.2.0")]
    public void IsTrustedReleaseUrl_RejectsEverythingElse(string? url)
    {
        // The value is opened with ShellExecute; only an https github.com link
        // from the release API may pass through.
        Assert.False(GitHubReleaseChecker.IsTrustedReleaseUrl(url));
    }
}
