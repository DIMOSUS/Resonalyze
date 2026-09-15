using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class VirtualCrossoverEdgeTests
{
    private const int SampleRate = 48_000;

    private static Complex[] UnitImpulse(int length, int position)
    {
        var ir = new Complex[length];
        ir[position] = Complex.One;
        return ir;
    }

    [Fact]
    public void DegenerateWindowAboveNyquist_YieldsNoAlignment()
    {
        // 25-30 kHz is above the 24 kHz Nyquist: no bins, neutral result.
        Complex[] variable = UnitImpulse(4_096, 100);
        Complex[] fixedIr = UnitImpulse(4_096, 100);

        Assert.Empty(VirtualCrossoverAnalysis.FindAlignmentCandidates(
            variable, [fixedIr], SampleRate, 25_000, 30_000, -1, 1));
        Assert.Equal(0.0, VirtualCrossoverAnalysis.FindBestDelayMs(
            variable, [fixedIr], SampleRate, 25_000, 30_000, -1, 1));

        AlignmentResult best = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedIr], SampleRate, 25_000, 30_000, -1, 1);
        Assert.Equal(0.0, best.DelayMs);
        Assert.False(best.InvertPolarity);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ClampsAWidePassBandBelowNyquist()
    {
        // The upper edge clamps to 0.95*Nyquist.
        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                UnitImpulse(8_192, 2_000),
                UnitImpulse(8_192, 1_952),
                SampleRate,
                centerFrequencyHz: 1_000,
                passOctaves: 8.0,
                searchRangeMs: 3.0);

        Assert.True(result.BandLowHz < result.BandHighHz, "The clamped band must stay ordered.");
        Assert.True(result.BandHighHz <= SampleRate / 2.0 * 0.95 + 1e-6, "Upper edge must sit below Nyquist.");
        Assert.True(result.BandLowHz >= 20.0);
    }

    [Fact]
    public void FindAlignmentCandidates_LossDiagnosticsReflectSummationQuality()
    {
        Complex[] aligned = UnitImpulse(4_096, 100);
        IReadOnlyList<AlignmentCandidate> alignedCandidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                aligned, [UnitImpulse(4_096, 100)], SampleRate, 500, 2_000, -1, 1);

        AlignmentCandidate best = alignedCandidates[0];
        Assert.InRange(best.DelayMs, -0.05, 0.05);
        Assert.False(best.InvertPolarity);
        Assert.InRange(best.LossDb, -0.5, 0.5);
        Assert.InRange(best.DipDb, -0.5, 0.5);

        // Half a period at the 1.25 kHz centre (0.4 ms) makes the diagnostics non-vacuous.
        Complex[] offsetFixed = UnitImpulse(4_096, 100);
        offsetFixed[100] = Complex.Zero;
        offsetFixed[119] = Complex.One; // ~0.4 ms later
        IReadOnlyList<AlignmentCandidate> cancelling =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                aligned, [offsetFixed], SampleRate, 900, 1_600, -0.05, 0.05);

        Assert.True(cancelling[0].LossDb < best.LossDb - 0.2,
            $"Offset pair should show more loss ({cancelling[0].LossDb:0.00}) than the aligned pair ({best.LossDb:0.00}).");
        Assert.True(cancelling[0].DipDb <= cancelling[0].LossDb + 1e-9, "The dip cannot sit above the average.");
    }
}
