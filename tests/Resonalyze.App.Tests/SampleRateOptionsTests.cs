using Resonalyze.Options;

namespace Resonalyze.App.Tests;

// The difference between a driver that says "not that rate" and a driver that says
// nothing. Some ASIO drivers refuse a second open moments after the first — closing
// and reopening the settings window is exactly that — and answer the rate query with
// an empty list. Read as an answer, that silently replaced a configured 96 kHz with
// 44.1 and the next Apply persisted it.
public sealed class SampleRateOptionsTests
{
    [Fact]
    public void AFailedProbeChangesNothing()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 96_000, hasExistingList: true, probeFailed: true);

        Assert.True(resolution.ProbeFailed);
        Assert.Empty(resolution.Rates);
        Assert.Equal(96_000, resolution.Selected);
        Assert.Null(resolution.FellBackFrom);
    }

    // The regression this round is about: an empty list is a REAL answer everywhere
    // except ASIO. WASAPI Shared endpoints whose mix rates differ produce it, and so
    // does an Exclusive or Wave pair with no rate in common. Keeping the previous
    // device's list there would offer — and on the next Apply persist — a rate that
    // belongs to another configuration entirely.
    [Fact]
    public void AnEmptyAnswerIsStillAnAnswerWhenTheProbeDidNotFail()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 48_000, hasExistingList: true, probeFailed: false);

        Assert.False(resolution.ProbeFailed);
        Assert.Equal([48_000], resolution.Rates);
        Assert.Equal(48_000, resolution.Selected);
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
        Assert.Equal([44_100, 48_000, 96_000], resolution.Rates);
    }

    [Fact]
    public void ADriverThatDoesNotOfferItSaysSo()
    {
        // A real fallback: the driver answered, and what it offers does not include the
        // configured rate. The rate changes and the caller is told which one was lost,
        // so the panel can show it rather than swap the number quietly.
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [44_100, 48_000], 96_000, hasExistingList: true, probeFailed: false);

        Assert.False(resolution.ProbeFailed);
        Assert.Equal(96_000, resolution.FellBackFrom);
        Assert.Equal(44_100, resolution.Selected);
    }

    [Fact]
    public void TheFirstPopulationHasNoListToKeep()
    {
        // Startup, before anything is in the combo: an empty probe cannot be resolved by
        // keeping what is there, because nothing is. The configured rate stands as the
        // single option — still not the 44.1 constant, which is only for having nothing
        // at all to go on.
        SampleRateResolution kept = SampleRateOptions.Resolve(
            [], 96_000, hasExistingList: false, probeFailed: false);
        Assert.False(kept.ProbeFailed);
        Assert.Equal([96_000], kept.Rates);
        Assert.Equal(96_000, kept.Selected);
        Assert.Null(kept.FellBackFrom);

        SampleRateResolution nothing = SampleRateOptions.Resolve(
            [], 0, hasExistingList: false, probeFailed: false);
        Assert.Equal([SampleRateOptions.FallbackSampleRate], nothing.Rates);
    }

    // A failed probe with nothing on screen to keep still failed. The list it produces
    // is the configured rate standing alone — not something a driver offered — so the
    // status line must say the driver did not report rather than call the rate
    // supported on the strength of a probe that never answered.
    [Fact]
    public void AFailedProbeWithNothingToKeepIsStillReportedAsFailed()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 96_000, hasExistingList: false, probeFailed: true);

        Assert.True(resolution.ProbeFailed);
        Assert.Equal([96_000], resolution.Rates);
        Assert.Equal(96_000, resolution.Selected);
        Assert.Null(resolution.FellBackFrom);
    }

    // Which probes can fall silent at all. This is the scoping the review asked for:
    // the keep-the-list behaviour must not be reachable from a backend whose empty
    // answer is a real one.
    [Theory]
    // ASIO with a named driver that reported nothing — silence, the #92 case.
    [InlineData(true, "Focusrite USB ASIO", 0, true)]
    // The same driver, answering. Not a failure however few rates it names.
    [InlineData(true, "Focusrite USB ASIO", 1, false)]
    // ASIO with no driver selected: nothing was asked, so nothing is preserved.
    [InlineData(true, "", 0, false)]
    [InlineData(true, null, 0, false)]
    // Every non-ASIO backend, whose empty list is an answer and must rebuild.
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
}
