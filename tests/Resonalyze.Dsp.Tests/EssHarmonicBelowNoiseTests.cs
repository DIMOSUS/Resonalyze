using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// A harmonic below the noise floor (edges near the plateau peak) was misread as overlap and warned;
// it is dropped silently as below-noise instead.
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

    private static double[] NoisyCleanImpulse(int seed = 12345)
    {
        var random = new Random(seed);
        double[] impulse = new double[ImpulseLength];
        for (int i = 0; i < ImpulseLength; i++)
        {
            // Sum of 12 uniforms minus 6: zero-mean, unit-variance.
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
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2)] = 0.02;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4));

        HarmonicPacketValidity h2Validity =
            decomposition.Validity.Packets.Single(p => p.Order == 2);
        Assert.False(h2Validity.IsBelowNoiseFloor);
        Assert.True(h2Validity.IsReliable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AContaminatedEdgeOverANoisePlateau_IsNotBlessedAsBelowNoise(bool leading)
    {
        // A neighbour's tail in one edge region is a polluted window: the overlap warning must still fire.
        var sweep = Sweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);
        double[] impulse = NoisyCleanImpulse();
        int windowLength = h2.EndSample - h2.StartSample + 1;
        int edgeLength = (int)(0.15 * windowLength);
        int edgeStart = leading ? h2.StartSample : h2.EndSample - edgeLength + 1;
        for (int i = edgeStart; i < edgeStart + edgeLength; i++)
        {
            impulse[i] = 0.01;
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
    public void ASignalInTheShoulder_BetweenPlateauAndEdge_IsNotBlessedAsBelowNoise()
    {
        // The verdict must survey the whole window: a shoulder burst leaves plateau max and edge RMS at the floor.
        var sweep = Sweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);
        double[] impulse = NoisyCleanImpulse();
        int length = h2.EndSample - h2.StartSample + 1;
        int plateauTo = Math.Min(h2.EndSample, h2.PeakSample + length / 8);
        int trailingEdgeStart =
            h2.EndSample - Math.Max(1, (int)Math.Round(0.15 * length)) + 1;
        const int burstLength = 500;
        int burstStart = plateauTo + (trailingEdgeStart - plateauTo - burstLength) / 2;
        Assert.True(burstStart > plateauTo);
        Assert.True(burstStart + burstLength < trailingEdgeStart);
        for (int i = burstStart; i < burstStart + burstLength; i++)
        {
            impulse[i] = 0.01;
        }

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4));

        HarmonicPacketValidity h2Validity =
            decomposition.Validity.Packets.Single(p => p.Order == 2);
        Assert.False(h2Validity.IsBelowNoiseFloor);
        Assert.NotNull(h2Validity.Warning);
    }

    [Fact]
    public void WithoutAUsableTail_TheOldOverlapVerdictIsKept()
    {
        // A silent tail gives no floor estimate: fall back to the edge-based isolation verdict.
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

        Assert.All(decomposition.Validity.Packets, packet =>
        {
            Assert.False(packet.IsBelowNoiseFloor);
            Assert.NotNull(packet.Warning);
        });
    }
}
