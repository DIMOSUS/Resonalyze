using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// The recommendation is suppressed whenever a verdict disqualified the arrival, so the status never gives opposite instructions.
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
        Assert.False(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 40, strongestIsSeparateArrival: true),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Latched),
            TimeAlignmentBandMode.ManualBand,
            crosstalkDetected: false));
    }

    [Fact]
    public void NearNoiseSuppressesTheFirstArrivalRecommendation()
    {
        Assert.False(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 8),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: false));
    }

    [Fact]
    public void ContaminatedFullBandSuppressesTheFirstArrivalRecommendation()
    {
        // Bypass analyzes the raw record, so detected crosstalk may be what First Arrival times.
        Assert.False(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 40),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: true));
    }

    [Fact]
    public void CleanedBandedModeWithCrosstalkKeepsTheRecommendation()
    {
        Assert.True(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 40),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Verified),
            TimeAlignmentBandMode.AutoBand,
            crosstalkDetected: true));
    }

    [Fact]
    public void AHealthyReadKeepsTheRecommendation()
    {
        Assert.True(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 40),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Verified),
            TimeAlignmentBandMode.ManualBand,
            crosstalkDetected: false));
        Assert.True(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 40),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: false));
    }

    [Fact]
    public void AnUnverifiedProbeAloneDoesNotSuppressTheRecommendation()
    {
        Assert.True(TimeAlignmentRecommendation.IsArrivalRecommendable(
            Result(snrDb: 40),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Unverified),
            TimeAlignmentBandMode.AutoBand,
            crosstalkDetected: false));
    }

    // The energy onset is never recommended here: its bias cancels only between two sides of one driver pair.
    [Fact]
    public void RecommendedRow_IsTheFirstArrivalWhereItStands()
    {
        Assert.Equal(
            TimeAlignmentDelayRow.FirstArrival,
            TimeAlignmentRecommendation.RecommendedRow(
                Result(snrDb: 60),
                Probe(AutoAlignmentEngine.ArrivalCertificate.Verified),
                TimeAlignmentBandMode.ManualBand,
                crosstalkDetected: false));
        Assert.Equal(
            TimeAlignmentDelayRow.FirstArrival,
            TimeAlignmentRecommendation.RecommendedRow(
                Result(snrDb: 60),
                honestyProbe: null,
                TimeAlignmentBandMode.FullBand,
                crosstalkDetected: false));
    }

    [Fact]
    public void RecommendedRow_NeverPicksTheEnergyOnsetOrTheStrongestPeak()
    {
        Assert.Equal(
            TimeAlignmentDelayRow.FirstArrival,
            TimeAlignmentRecommendation.RecommendedRow(
                Result(snrDb: 70),
                Probe(AutoAlignmentEngine.ArrivalCertificate.Verified),
                TimeAlignmentBandMode.AutoBand,
                crosstalkDetected: false));
    }

    [Fact]
    public void RecommendedRow_IsNoneWhereAVerdictDisqualifiedTheRead()
    {
        Assert.Null(TimeAlignmentRecommendation.RecommendedRow(
            Result(snrDb: 8),
            honestyProbe: null,
            TimeAlignmentBandMode.ManualBand,
            crosstalkDetected: false));
        Assert.Null(TimeAlignmentRecommendation.RecommendedRow(
            Result(snrDb: 60),
            Probe(AutoAlignmentEngine.ArrivalCertificate.Latched),
            TimeAlignmentBandMode.ManualBand,
            crosstalkDetected: false));
        Assert.Null(TimeAlignmentRecommendation.RecommendedRow(
            Result(snrDb: 60),
            honestyProbe: null,
            TimeAlignmentBandMode.FullBand,
            crosstalkDetected: true));
    }
}
