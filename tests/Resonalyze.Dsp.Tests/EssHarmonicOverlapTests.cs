using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Pins the packet-overlap validity: a well-separated (fast-decaying) system reports
// every harmonic reliable, while a packet that has not decayed by its window edge is
// flagged as overlapping a neighbour, warned about, and dropped from the curves and
// from THD instead of being drawn confidently.
public sealed class EssHarmonicOverlapTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const int SweepSamples = 200_000;
    private const int PeakIndex = 150_000;
    private const int ImpulseLength = 200_000;

    private static EssSweepMetadata Sweep() =>
        EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);

    [Fact]
    public void ContainedPackets_AreAllReliableWithNoWarnings()
    {
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.02;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 3)] = 0.01;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));

        Assert.True(decomposition.Validity.IsValid);
        Assert.Empty(decomposition.Validity.Warnings);
        Assert.All(decomposition.Validity.Packets, p => Assert.True(p.IsReliable));
    }

    [Fact]
    public void AnUndecayedPacket_IsFlaggedOverlappingAndDroppedFromCurvesAndThd()
    {
        var sweep = Sweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);

        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        // A well-behaved HD3 delta stays reliable.
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 3)] = 0.01;

        // HD2: a slow decay that has NOT died out by the (later) window edge, so it
        // leaks toward the linear packet — the overlap the check must catch.
        int peak2 = h2.PeakSample;
        double tau = h2.EndSample - peak2;
        for (int i = peak2; i <= h2.EndSample && i < ImpulseLength; i++)
        {
            impulse[i] = 0.3 * Math.Exp(-(i - peak2) / tau) * Math.Cos(0.3 * (i - peak2));
        }

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4));

        HarmonicPacketValidity h2Validity =
            decomposition.Validity.Packets.Single(p => p.Order == 2);
        Assert.False(h2Validity.IsReliable);
        Assert.False(decomposition.Validity.IsValid);
        Assert.Contains(decomposition.Validity.Warnings, w => w.Contains("HD2"));

        DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
            decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 4));

        // HD2 dropped everywhere; HD3 still carried where reliable; THD excludes HD2.
        Assert.All(spectrum.HarmonicDistortionRatio[2], v => Assert.True(double.IsNaN(v)));
        Assert.Contains(spectrum.HarmonicDistortionRatio[3], double.IsFinite);
        Assert.Contains(spectrum.Warnings, w => w.Contains("HD2"));

        // Where only HD3 contributes, THD equals HD3 (HD2 is not summed in).
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            double hd3 = spectrum.HarmonicDistortionRatio[3][i];
            if (double.IsFinite(hd3) && double.IsFinite(spectrum.ThdRatio[i]))
            {
                Assert.Equal(hd3, spectrum.ThdRatio[i], 9);
            }
        }
    }
}
