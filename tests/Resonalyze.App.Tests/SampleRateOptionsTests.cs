using Resonalyze.Options;

namespace Resonalyze.App.Tests;

// Some ASIO drivers refuse a quick second open and answer the rate query with an empty list: that is silence, not an answer.
public sealed class SampleRateOptionsTests
{
    [Fact]
    public void AFailedProbeLeavesTheListOnScreenAlone()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 96_000, hasExistingList: true, probeFailed: true);

        Assert.True(resolution.ProbeFailed);
        Assert.Null(resolution.Rates);
        Assert.Equal(96_000, resolution.Selected);
        Assert.Null(resolution.FellBackFrom);
    }

    [Fact]
    public void AFailedProbeWithNothingToKeepOffersTheConfiguredRateAndStillSaysItFailed()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 96_000, hasExistingList: false, probeFailed: true);

        Assert.NotNull(resolution.Rates);
        Assert.Equal([96_000], resolution.Rates);
        Assert.Equal(96_000, resolution.Selected);
        Assert.True(resolution.ProbeFailed);
        Assert.Null(resolution.FellBackFrom);

        SampleRateResolution nothing = SampleRateOptions.Resolve(
            [], 0, hasExistingList: false, probeFailed: true);
        Assert.NotNull(nothing.Rates);
        Assert.Equal([SampleRateOptions.FallbackSampleRate], nothing.Rates);
        Assert.Equal(SampleRateOptions.FallbackSampleRate, nothing.Selected);
    }

    // Outside ASIO an empty list is a real answer (mismatched WASAPI mix rates, no common Exclusive/Wave rate).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGenuineEmptyAnswerOffersNothingAtAll(bool hasExistingList)
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 48_000, hasExistingList, probeFailed: false);

        Assert.NotNull(resolution.Rates);
        Assert.Empty(resolution.Rates);
        Assert.False(resolution.ProbeFailed);
        Assert.Null(resolution.FellBackFrom);
    }

    [Fact]
    public void ADriverThatOffersTheConfiguredRateKeepsIt()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [44_100, 48_000, 96_000], 96_000, hasExistingList: true, probeFailed: false);

        Assert.False(resolution.ProbeFailed);
        Assert.Null(resolution.FellBackFrom);
        Assert.Equal(96_000, resolution.Selected);
        Assert.NotNull(resolution.Rates);
        Assert.Equal([44_100, 48_000, 96_000], resolution.Rates);
    }

    [Fact]
    public void ADriverThatDoesNotOfferItSaysSo()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [44_100, 48_000], 96_000, hasExistingList: true, probeFailed: false);

        Assert.False(resolution.ProbeFailed);
        Assert.Equal(96_000, resolution.FellBackFrom);
        Assert.Equal(44_100, resolution.Selected);
    }

    [Theory]
    [InlineData(true, "Focusrite USB ASIO", 0, true)]
    [InlineData(true, "Focusrite USB ASIO", 1, false)]
    [InlineData(true, "", 0, false)]
    [InlineData(true, null, 0, false)]
    [InlineData(false, null, 0, false)]
    [InlineData(false, "Focusrite USB ASIO", 0, false)]
    public void OnlyANamedAsioDriverCanFallSilent(
        bool isAsio,
        string? driverName,
        int reportedRateCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            SampleRateOptions.IsProbeFailure(isAsio, driverName, reportedRateCount));
    }

    // An empty combo makes the panel fall back to 44.1 kHz, so Apply must refuse it.
    [Fact]
    public void AnEmptyListIsRefusedOnTheWayToConfiguration()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => SampleRateOptions.ValidateSelectedRate(
                [],
                SampleRateOptions.FallbackSampleRate,
                "The WASAPI Exclusive endpoints"));

        // Not the bit depth: that control is disabled.
        Assert.Contains("no sample rate in common", error.Message);
        Assert.Contains("The WASAPI Exclusive endpoints", error.Message);
        Assert.Contains("playback channel", error.Message);
        Assert.Contains("microphone and loopback channels", error.Message);
        Assert.DoesNotContain("bit depth", error.Message);
    }

    [Fact]
    public void ARateNobodyReportedIsRefusedAndNamed()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => SampleRateOptions.ValidateSelectedRate(
                [44_100, 48_000],
                96_000,
                "Wave devices"));

        Assert.Contains("96000 Hz", error.Message);
        Assert.Contains("Wave devices", error.Message);
    }

    // Exclusive + Mono: stereo-only endpoints refuse every rate, and selecting entry 0 of the empty list threw.
    [Fact]
    public void AnEmptyListHasNothingToSelect()
    {
        Assert.Equal(-1, SampleRateOptions.FindRateIndex([], 48_000));
    }

    [Fact]
    public void TheSelectedRateIsFoundWhereItSits()
    {
        Assert.Equal(0, SampleRateOptions.FindRateIndex([44_100, 48_000, 96_000], 44_100));
        Assert.Equal(2, SampleRateOptions.FindRateIndex([44_100, 48_000, 96_000], 96_000));
        Assert.Equal(0, SampleRateOptions.FindRateIndex([44_100, 48_000], 192_000));
    }

    [Fact]
    public void AReportedRatePasses()
    {
        SampleRateOptions.ValidateSelectedRate(
            [44_100, 48_000, 96_000],
            96_000,
            "Wave devices");
    }
}
