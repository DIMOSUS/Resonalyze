using Resonalyze.Options;

namespace Resonalyze.App.Tests;

// The difference between a driver that says "not that rate", a driver that says
// nothing, and a configuration for which no rate exists. Some ASIO drivers refuse a
// second open moments after the first — closing and reopening the settings window is
// exactly that — and answer the rate query with an empty list. Read as an answer, that
// silently replaced a configured 96 kHz with 44.1 and the next Apply persisted it.
// Read as silence everywhere, it manufactures support nobody reported.
public sealed class SampleRateOptionsTests
{
    [Fact]
    public void AFailedProbeLeavesTheListOnScreenAlone()
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 96_000, hasExistingList: true, probeFailed: true);

        Assert.True(resolution.ProbeFailed);
        // Null, not empty: nothing to rebuild, as opposed to nothing to offer.
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
        // The rate stands alone because nothing answered, not because anything offered
        // it — so the status line must not call it supported.
        Assert.True(resolution.ProbeFailed);
        Assert.Null(resolution.FellBackFrom);

        // With no configured rate either, the constant is all that is left.
        SampleRateResolution nothing = SampleRateOptions.Resolve(
            [], 0, hasExistingList: false, probeFailed: true);
        Assert.NotNull(nothing.Rates);
        Assert.Equal([SampleRateOptions.FallbackSampleRate], nothing.Rates);
        Assert.Equal(SampleRateOptions.FallbackSampleRate, nothing.Selected);
    }

    // The regression from this round. An empty list is a REAL answer everywhere except
    // ASIO: WASAPI Shared endpoints whose mix rates differ produce it, and so does an
    // Exclusive or Wave pair with no rate in common. Offering the configured rate there
    // manufactures support nobody reported, and Apply would go on to accept it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGenuineEmptyAnswerOffersNothingAtAll(bool hasExistingList)
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [], 48_000, hasExistingList, probeFailed: false);

        // Empty, not null: there IS an answer, and it is that nothing works here.
        Assert.NotNull(resolution.Rates);
        Assert.Empty(resolution.Rates);
        Assert.False(resolution.ProbeFailed);
        // Nothing was taken away from the user; there was never anything to take.
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
        // A real fallback: the driver answered, and what it offers does not include the
        // configured rate. The rate changes and the caller is told which one was lost,
        // so the panel can show it rather than swap the number quietly.
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            [44_100, 48_000], 96_000, hasExistingList: true, probeFailed: false);

        Assert.False(resolution.ProbeFailed);
        Assert.Equal(96_000, resolution.FellBackFrom);
        Assert.Equal(44_100, resolution.Selected);
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
