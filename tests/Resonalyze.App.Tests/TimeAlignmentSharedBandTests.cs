using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// A Main-only band made the delta depend on load order: a field mid pair read 32.7-7671 vs 75.5-4695 Hz, moving the split 0.3 ms.
public sealed class TimeAlignmentSharedBandTests
{
    [Fact]
    public void SharedBand_IsTheOverlapOfBothRecordsOwnBands()
    {
        var main = new DominantBand(32.7, 7671.3, 174.5);
        var compare = new DominantBand(75.5, 4695.1, 261.4);

        (DominantBand band, bool shared) =
            TimeAlignmentPanelController.SharedBand(main, compare);

        Assert.True(shared);
        Assert.Equal(75.5, band.LowHz, precision: 6);
        Assert.Equal(4695.1, band.HighHz, precision: 6);
    }

    [Fact]
    public void SharedBand_ReadsTheSameEitherWayRound()
    {
        var main = new DominantBand(32.7, 7671.3, 174.5);
        var compare = new DominantBand(75.5, 4695.1, 261.4);

        (DominantBand forward, _) =
            TimeAlignmentPanelController.SharedBand(main, compare);
        (DominantBand reversed, _) =
            TimeAlignmentPanelController.SharedBand(compare, main);

        Assert.Equal(forward.LowHz, reversed.LowHz, precision: 9);
        Assert.Equal(forward.HighHz, reversed.HighHz, precision: 9);
    }

    [Fact]
    public void SharedBand_KeepsThePeakInsideTheBandItReturns()
    {
        var main = new DominantBand(30.0, 900.0, 45.0);
        var compare = new DominantBand(200.0, 4000.0, 800.0);

        (DominantBand band, bool shared) =
            TimeAlignmentPanelController.SharedBand(main, compare);

        Assert.True(shared);
        Assert.InRange(band.PeakHz, band.LowHz, band.HighHz);
    }

    [Fact]
    public void TryDetectDominantBand_ReportsFailureInsteadOfThrowing()
    {
        // The detector throws on an incoherent record; Compare's failure must not take Main's analysis down.
        TimeAlignmentAnalysisSource source = Source(coherent: false);

        bool detected = TimeAlignmentPanelController.TryDetectDominantBand(
            source, out DominantBand band);

        Assert.False(detected);
        Assert.Equal(default, band);
    }

    [Fact]
    public void TryDetectDominantBand_ReturnsTheBandOfAMeasurableRecord()
    {
        TimeAlignmentAnalysisSource source = Source(coherent: true);

        bool detected = TimeAlignmentPanelController.TryDetectDominantBand(
            source, out DominantBand band);

        Assert.True(detected);
        Assert.True(band.HighHz > band.LowHz);
    }

    private static TimeAlignmentAnalysisSource Source(bool coherent)
    {
        const int sampleRate = 48_000;
        var impulseResponse = new double[8_192];
        impulseResponse[100] = 1.0;
        double[] coherence =
            Enumerable.Repeat(coherent ? 1.0 : 0.0, sampleRate / 2).ToArray();
        return new TimeAlignmentAnalysisSource(
            "Compare",
            "compare",
            sampleRate,
            24,
            1.0,
            PlaybackChannel.Mono,
            SweepMeasurementMode.LoopbackTransfer,
            impulseResponse,
            coherence,
            default);
    }

    [Fact]
    public void SharedBand_KeepsMainsBandWhenTheTwoRecordsBarelyOverlap()
    {
        var main = new DominantBand(25.0, 160.0, 60.0);
        var compare = new DominantBand(150.0, 18_000.0, 3_000.0);

        (DominantBand band, bool shared) =
            TimeAlignmentPanelController.SharedBand(main, compare);

        Assert.False(shared);
        Assert.Equal(main, band);
    }
}
