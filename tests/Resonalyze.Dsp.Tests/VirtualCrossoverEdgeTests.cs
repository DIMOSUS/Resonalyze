using System.Numerics;

namespace Resonalyze.Dsp.Tests;

// Edge/guard coverage for VirtualCrossoverAnalysis: a degenerate (empty) frequency
// window, the Nyquist band clamp, and the meaning of the reported loss diagnostics.
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
        // 25-30 kHz sits entirely above the 24 kHz Nyquist, so no FFT bins fall in
        // the window (lastBin < firstBin). The search must return the neutral result
        // rather than indexing an empty bin list.
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
        // A very wide pass band would run past Nyquist; the search must clamp the
        // upper edge (to 0.95*Nyquist) while keeping the band ordered and usable.
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
        // Two identical, already-aligned channels sum constructively across the whole
        // band, so the best candidate's average loss and worst dip are both ~0 dB.
        Complex[] aligned = UnitImpulse(4_096, 100);
        IReadOnlyList<AlignmentCandidate> alignedCandidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                aligned, [UnitImpulse(4_096, 100)], SampleRate, 500, 2_000, -1, 1);

        AlignmentCandidate best = alignedCandidates[0];
        Assert.InRange(best.DelayMs, -0.05, 0.05);
        Assert.False(best.InvertPolarity);
        Assert.InRange(best.LossDb, -0.5, 0.5);
        Assert.InRange(best.DipDb, -0.5, 0.5);

        // A second channel offset by half a period at the band centre (1.25 kHz ->
        // 0.4 ms) partially cancels: the same aligned pick now carries a real average
        // loss and a still-deeper dip, so the diagnostics are not vacuous.
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
