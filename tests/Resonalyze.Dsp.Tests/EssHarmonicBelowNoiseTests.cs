using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Pins the below-noise classification of harmonic packets. The overlap check reads
// the window edges RELATIVE to the packet's own peak, so a harmonic that fell below
// the measurement noise floor — a windowful of flat noise, edges roughly at the
// plateau peak — used to be misread as "packet overlaps its neighbour" and scolded
// the user's cleanest captures with an amber warning. Such an order must instead be
// classified below-noise: still dropped (its "curve" would be the noise floor), but
// with no warning, because an unresolvably small harmonic is good news.
public sealed class EssHarmonicBelowNoiseTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const int SweepSamples = 200_000;
    private const int PeakIndex = 150_000;
    private const int ImpulseLength = 200_000;
    private const double NoiseSigma = 1e-4;

    private static EssSweepMetadata Sweep() =>
        EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);

    // A linear delta over a deterministic white-noise floor, with no harmonic
    // content at all — the record every clean electrical capture approximates.
    private static double[] NoisyCleanImpulse(int seed = 12345)
    {
        var random = new Random(seed);
        double[] impulse = new double[ImpulseLength];
        for (int i = 0; i < ImpulseLength; i++)
        {
            // Sum of 12 uniforms minus 6: zero-mean, unit-variance, deterministic.
            double gaussian = -6.0;
            for (int k = 0; k < 12; k++)
            {
                gaussian += random.NextDouble();
            }
            impulse[i] = NoiseSigma * gaussian;
        }

        impulse[PeakIndex] = 1.0;
        return impulse;
    }

    [Fact]
    public void HarmonicsBelowTheNoiseFloor_AreClassifiedBelowNoise_WithNoWarnings()
    {
        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            NoisyCleanImpulse(), Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));

        Assert.All(decomposition.Validity.Packets, packet =>
        {
            Assert.True(packet.IsBelowNoiseFloor);
            Assert.False(packet.IsReliable);
            Assert.Null(packet.Warning);
        });
        // No warnings: nothing here is a fault, so IsValid stays true.
        Assert.Empty(decomposition.Validity.Warnings);
        Assert.True(decomposition.Validity.IsValid);
    }

    [Fact]
    public void HarmonicsBelowTheNoiseFloor_AreDroppedFromCurves_WithoutAWarning()
    {
        EssDistortion.DistortionCurveResult result = EssDistortion.ComputeDistortionCurvesResult(
            NoisyCleanImpulse(),
            Sweep(),
            new DistortionOptions(MaxHarmonic: 4),
            calibration: null,
            SpectrumCurves.Harmonics);

        Assert.DoesNotContain(result.Curves, c => c.Kind == AnalysisCurveKind.SecondHarmonic);
        Assert.DoesNotContain(result.Curves, c => c.Kind == AnalysisCurveKind.ThirdHarmonic);
        Assert.DoesNotContain(result.Curves, c => c.Kind == AnalysisCurveKind.FourthHarmonic);
        Assert.Empty(result.Warnings);
        Assert.All(result.PacketValidity, packet => Assert.True(packet.IsBelowNoiseFloor));
    }

    [Fact]
    public void AGenuineOverlap_StaysFlaggedAsOverlap_WhenANoiseFloorIsPresent()
    {
        var sweep = Sweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);

        double[] impulse = NoisyCleanImpulse();
        // HD2 content far above the noise that persists to the window edge — the
        // real leak the overlap warning exists for.
        for (int i = h2.PeakSample; i <= h2.EndSample && i < ImpulseLength; i++)
        {
            impulse[i] = 0.3 * Math.Cos(0.3 * (i - h2.PeakSample));
        }

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4));

        HarmonicPacketValidity h2Validity =
            decomposition.Validity.Packets.Single(p => p.Order == 2);
        Assert.False(h2Validity.IsBelowNoiseFloor);
        Assert.False(h2Validity.IsReliable);
        Assert.NotNull(h2Validity.Warning);
        Assert.Contains(decomposition.Validity.Warnings, w => w.Contains("HD2"));
    }

    [Fact]
    public void AHarmonicWellAboveTheNoiseFloor_IsNotReclassified()
    {
        var sweep = Sweep();
        double[] impulse = NoisyCleanImpulse();
        // A contained HD2 delta far above the noise floor stays a reliable packet.
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2)] = 0.02;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4));

        HarmonicPacketValidity h2Validity =
            decomposition.Validity.Packets.Single(p => p.Order == 2);
        Assert.False(h2Validity.IsBelowNoiseFloor);
        Assert.True(h2Validity.IsReliable);
    }

    [Fact]
    public void WithoutAUsableTail_TheOldOverlapVerdictIsKept()
    {
        // The noise stops right after the linear window, leaving a silent tail
        // whose RMS is zero — no usable floor estimate, so the below-noise test
        // must stand down and the noise-filled harmonic windows fall back to the
        // edge-based isolation verdict.
        var sweep = Sweep();
        HarmonicWindowDefinition linear = EssHarmonicAnalysis.BuildWindow(sweep, 1, 0.5);
        double[] impulse = NoisyCleanImpulse();
        for (int i = Math.Max(0, linear.EndSample); i < ImpulseLength; i++)
        {
            impulse[i] = 0.0;
        }
        impulse[PeakIndex] = 1.0;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4));

        // Whether each noise-filled window lands on the marginal or the invalid
        // side of the edge margin, it must be warned about the OLD way — never
        // silently blessed as below-noise without a floor to judge against.
        Assert.All(decomposition.Validity.Packets, packet =>
        {
            Assert.False(packet.IsBelowNoiseFloor);
            Assert.NotNull(packet.Warning);
        });
    }
}
