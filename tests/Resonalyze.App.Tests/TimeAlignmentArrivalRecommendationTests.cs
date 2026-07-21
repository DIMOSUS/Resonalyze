using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// The "Use First Arrival for alignment" hint and the disqualifying verdicts
// (modal latch, near-noise, contaminated full-band) are independent states;
// this contract pins that the recommendation is suppressed whenever any
// verdict has just disqualified the arrival, so the status box can never
// give two opposite instructions at once.
public sealed class TimeAlignmentArrivalRecommendationTests
{
    private static TimeAlignmentAnalysisResult Result(
        double snrDb,
        bool strongestIsSeparateArrival = true) =>
        new(
            EnvelopeSamples: [],
            EnvelopePeakIndex: 0,
            EnvelopePeak: 1.0,
            StrongestEnvelopePeakIndex: 0,
            StrongestEnvelopePeak: 1.0,
            SignalToNoiseDecibels: snrDb,
            FirstArrivalProminenceDecibels: -10.0,
            FirstArrivalPeakSample: 100.0,
            FirstArrivalDelayMilliseconds: 2.0,
            StrongestPeakSample: 700.0,
            StrongestDelayMilliseconds: 14.0,
            StrongestPeakSeparationMilliseconds: 12.0,
            StrongestPeakIsSeparateArrival: strongestIsSeparateArrival,
            FirstArrivalConfidence: 0.5,
            FirstArrivalRefinedByPhat: true,
            StrongestConfidence: 0.5,
            StrongestRefinedByPhat: true);

    private static TimeAlignmentArrivalProbe Probe(
        AutoAlignmentEngine.ArrivalCertificate certificate) =>
        new(certificate, Result(snrDb: 40), 1000, 1414, 1.0);

    [Fact]
    public void ModalLatchSuppressesTheFirstArrivalRecommendation()
    {
        // The exact contradiction from review: Latched + a separate late
        // strongest peak — "do not align from this arrival" must not be
        // followed by "Use First Arrival for alignment".
        Assert.False(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 40, strongestIsSeparateArrival: true),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Latched),
            TimeAlignmentBandMode.ManualBand,
            crosstalkDetected: false));
    }

    [Fact]
    public void NearNoiseSuppressesTheFirstArrivalRecommendation()
    {
        Assert.False(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 8),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: false));
    }

    [Fact]
    public void ContaminatedFullBandSuppressesTheFirstArrivalRecommendation()
    {
        // Bypass analyzes the raw record, so with detected crosstalk the
        // First Arrival may be timing the click.
        Assert.False(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 40),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: true));
    }

    [Fact]
    public void CleanedBandedModeWithCrosstalkKeepsTheRecommendation()
    {
        // In the banded modes the analysis ran on the CLEANED record — the
        // detected crosstalk is no longer in the figures.
        Assert.True(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 40),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Verified),
            TimeAlignmentBandMode.AutoBand,
            crosstalkDetected: true));
    }

    [Fact]
    public void AHealthyReadKeepsTheRecommendation()
    {
        Assert.True(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 40),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Verified),
            TimeAlignmentBandMode.ManualBand,
            crosstalkDetected: false));
        Assert.True(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 40),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: false));
    }

    [Fact]
    public void AnUnverifiedProbeAloneDoesNotSuppressTheRecommendation()
    {
        // Unverified = no certificate either way; the arrival stays usable
        // (mirrors the engine: unverified reads keep working, just without
        // a tight lock).
        Assert.True(TimeAlignmentPanelController.IsArrivalRecommendable(
            Result(snrDb: 40),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Unverified),
            TimeAlignmentBandMode.AutoBand,
            crosstalkDetected: false));
    }
}
