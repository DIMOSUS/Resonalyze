using System;
using System.Numerics;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Pins the pure ESS decomposition: the harmonic-packet geometry (positions,
// ordering, neighbour boundaries, Nyquist/order reach) and the shared spectral
// normalization that makes H1 and every Hn directly comparable regardless of
// window length, zero-padding, sign or level. These invariants are what let a
// later stage compute HDn = |Hn|/|H1| as an honest ratio.
public sealed class EssHarmonicAnalysisTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const int SweepSamples = 200_000;
    private const int PeakIndex = 150_000;

    private static EssSweepMetadata Sweep() =>
        EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);

    [Fact]
    public void FromExponentialSweep_EndsAtNyquistAndSpansTheOctavesDownward()
    {
        EssSweepMetadata sweep = Sweep();

        Assert.Equal(24_000.0, sweep.EndFrequencyHz, 6);
        Assert.Equal(24_000.0 / 1024.0, sweep.StartFrequencyHz, 6);
        Assert.Equal(1024.0, sweep.FrequencyRatio, 6);
        Assert.Equal(SweepSamples / (double)SampleRate, sweep.DurationSeconds, 9);
    }

    [Fact]
    public void HarmonicTimeOffset_MatchesTheLogSweepLaw()
    {
        EssSweepMetadata sweep = Sweep();

        // Δt(n) = L · ln(n) / ln(f2/f1). For a 10-octave sweep ln(f2/f1)=10·ln2,
        // so H2 advances by exactly L/10.
        double duration = sweep.DurationSeconds;
        Assert.Equal(duration / 10.0, EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 2), 9);
        Assert.Equal(0.0, EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 1), 12);

        // H3 sits farther back than H2, H4 farther still (monotone in order).
        double h2 = EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 2);
        double h3 = EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 3);
        double h4 = EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 4);
        Assert.True(h3 > h2 && h4 > h3);
    }

    [Fact]
    public void HarmonicTimeOffset_ScalesWithSweepDurationNotLevel()
    {
        EssSweepMetadata shortSweep =
            EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);
        EssSweepMetadata longSweep =
            EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples * 2, PeakIndex);

        // Doubling the sweep length doubles the harmonic advance (same octave span).
        Assert.Equal(
            2.0 * EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(shortSweep, 3),
            EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(longSweep, 3),
            9);
    }

    [Fact]
    public void BuildWindow_PlacesPacketsBeforeThePeakInHarmonicOrder()
    {
        EssSweepMetadata sweep = Sweep();

        HarmonicWindowDefinition h1 = EssHarmonicAnalysis.BuildWindow(sweep, 1, 0.5);
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);
        HarmonicWindowDefinition h3 = EssHarmonicAnalysis.BuildWindow(sweep, 3, 0.5);

        Assert.Equal(PeakIndex, h1.PeakSample);
        Assert.True(h2.PeakSample < h1.PeakSample, "H2 sits before the linear peak.");
        Assert.True(h3.PeakSample < h2.PeakSample, "H3 sits before H2.");

        // Each window brackets its own peak.
        Assert.InRange(h2.PeakSample, h2.StartSample, h2.EndSample);
        Assert.InRange(h3.PeakSample, h3.StartSample, h3.EndSample);
    }

    [Fact]
    public void BuildWindow_LinearPacketIsSymmetricAroundThePeak()
    {
        HarmonicWindowDefinition h1 = EssHarmonicAnalysis.BuildWindow(Sweep(), 1, 0.5);

        int before = h1.PeakSample - h1.StartSample;
        int after = h1.EndSample - h1.PeakSample;
        Assert.True(Math.Abs(before - after) <= 1, "H1 window should be symmetric about the peak.");
    }

    [Fact]
    public void BuildWindow_AdjacentPacketsMeetAtTheirSharedBoundaryWithoutOverlap()
    {
        EssSweepMetadata sweep = Sweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);
        HarmonicWindowDefinition h3 = EssHarmonicAnalysis.BuildWindow(sweep, 3, 0.5);

        // H3's earlier edge and H2's later edge are both defined by the SAME
        // geometric-mean boundary, so H3.End (larger index) meets H2.Start (the
        // shared √6 boundary) within a sample of rounding — no packet leaks into
        // the other's nominal window.
        int sharedBoundary = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, Math.Sqrt(6.0));
        Assert.True(Math.Abs(h2.StartSample - sharedBoundary) <= 1);
        Assert.True(Math.Abs(h3.EndSample - sharedBoundary) <= 1);
        Assert.True(h3.EndSample <= h2.StartSample + 1, "H2 and H3 windows must not overlap.");
    }

    [Fact]
    public void MaxExcitationHz_StopsEachOrderAtNyquistOverOrder()
    {
        EssSweepMetadata sweep = Sweep();

        Assert.Equal(24_000.0, sweep.MaxExcitationHz(1), 6); // min(end, Nyq/1) = 24k
        Assert.Equal(12_000.0, sweep.MaxExcitationHz(2), 6); // Nyq/2
        Assert.Equal(8_000.0, sweep.MaxExcitationHz(3), 6);  // Nyq/3
    }

    [Fact]
    public void AnalyzeEssHarmonics_SeparatesLinearAndHarmonicPackets()
    {
        double[] impulse = new double[SweepSamples];
        impulse[PeakIndex] = 1.0;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse,
            Sweep(),
            new HarmonicAnalysisOptions(MaxHarmonic: 5));

        Assert.Equal(1, decomposition.Linear.Order);
        Assert.Equal(4, decomposition.Harmonics.Count);
        Assert.Equal(new[] { 2, 3, 4, 5 }, decomposition.Harmonics.Select(h => h.Order).ToArray());

        // All packets share one FFT grid (shared sample rate and length), so their
        // bins line up for later cross-order combination.
        int fft = decomposition.Linear.Spectrum.FftLength;
        Assert.All(decomposition.Harmonics, h => Assert.Equal(fft, h.Spectrum.FftLength));
    }

    // ----- Shared spectral normalization invariants -----
    //
    // A packet is a contained impulse response under a unity plateau, so the FFT
    // magnitude IS the packet's transfer magnitude. These pin the invariant that
    // makes |Hn|/|H1| honest: the magnitude is independent of window length,
    // zero-padding, sign and placement, so a ratio of two packets recovers their
    // amplitude ratio exactly regardless of the two windows' lengths.

    private const int Field = 20_000;

    private static HarmonicWindowDefinition RectWindow(int start, int length) =>
        new(Order: 1, PeakSample: start + length / 2, StartSample: start, EndSample: start + length - 1,
            FadeInSamples: 0, FadeOutSamples: 0);

    private static double ImpulseMagnitude(double height, int start, int length, int fftLength, int bin)
    {
        // A single impulse sitting under the (rectangular, plateau=1) window.
        double[] field = new double[Field];
        field[start + length / 2] = height;
        WindowedSpectrum spectrum = EssHarmonicAnalysis.ComputeWindowedSpectrum(
            field, RectWindow(start, length), fftLength, SampleRate);
        return spectrum.AmplitudeAt(bin);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ReadsAContainedImpulseAsItsHeight()
    {
        // The impulse has a flat spectrum, so every bin equals its height.
        Assert.Equal(0.5, ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40), 9);
        Assert.Equal(0.5, ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 137), 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_RatioIsIndependentOfTheTwoWindowLengths()
    {
        // The load-bearing invariant for HDn: a "harmonic" impulse of 0.02 read
        // through a 4096-sample window and a "linear" impulse of 1.0 read through a
        // 1024-sample window still yield a ratio of exactly 0.02.
        double harmonic = ImpulseMagnitude(0.02, 3_000, 4_096, 8_192, 200);
        double linear = ImpulseMagnitude(1.0, 3_000, 1_024, 8_192, 200);
        Assert.Equal(0.02, harmonic / linear, 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToTimeShift()
    {
        double a = ImpulseMagnitude(0.5, 1_000, 1_024, 1_024, 40);
        double b = ImpulseMagnitude(0.5, 6_000, 1_024, 1_024, 40);
        Assert.Equal(a, b, 12);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ZeroPaddingChangesGridNotLevel()
    {
        double tight = ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40);
        double padded = ImpulseMagnitude(0.5, 2_000, 1_024, 2_048, 80);
        Assert.Equal(tight, padded, 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToSign()
    {
        double a = ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40);
        double b = ImpulseMagnitude(-0.5, 2_000, 1_024, 1_024, 40);
        Assert.Equal(a, b, 12);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ScalingByTwoRaisesLevelBy6Db()
    {
        double quiet = ImpulseMagnitude(0.25, 2_000, 1_024, 1_024, 40);
        double loud = ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40);
        Assert.Equal(6.0206, 20.0 * Math.Log10(loud / quiet), 3);
    }
}
