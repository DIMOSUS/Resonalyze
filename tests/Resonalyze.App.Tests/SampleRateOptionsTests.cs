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
        SampleRateResolution resolution = SampleRateOptions.Resolve([], 96_000, hasExistingList: true);

        Assert.True(resolution.ProbeFailed);
        Assert.Empty(resolution.Rates);
        Assert.Equal(96_000, resolution.Selected);
        Assert.Null(resolution.FellBackFrom);
    }

    [Fact]
    public void ADriverThatOffersTheConfiguredRateKeepsIt()
    {
        SampleRateResolution resolution =
            SampleRateOptions.Resolve([44_100, 48_000, 96_000], 96_000, hasExistingList: true);

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
        SampleRateResolution resolution =
            SampleRateOptions.Resolve([44_100, 48_000], 96_000, hasExistingList: true);

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
        SampleRateResolution kept =
            SampleRateOptions.Resolve([], 96_000, hasExistingList: false);
        Assert.False(kept.ProbeFailed);
        Assert.Equal([96_000], kept.Rates);
        Assert.Equal(96_000, kept.Selected);
        Assert.Null(kept.FellBackFrom);

        SampleRateResolution nothing =
            SampleRateOptions.Resolve([], 0, hasExistingList: false);
        Assert.Equal([SampleRateOptions.FallbackSampleRate], nothing.Rates);
    }
}
