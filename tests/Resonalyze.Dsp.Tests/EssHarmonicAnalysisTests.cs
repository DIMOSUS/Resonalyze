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

    private static readonly double[] EmptyField = new double[20_000];

    private static double[] WithTone(int start, int length, int cyclesPerWindow, double amplitude)
    {
        // A pure tone confined to [start, start+length) whose frequency is an
        // integer number of cycles across the window, so an aligned FFT bin
        // recovers the amplitude exactly.
        double[] field = new double[EmptyField.Length];
        for (int i = 0; i < length; i++)
        {
            field[start + i] = amplitude * Math.Cos(2.0 * Math.PI * cyclesPerWindow * i / length);
        }

        return field;
    }

    private static HarmonicWindowDefinition RectWindow(int start, int length) =>
        new(Order: 1, PeakSample: start, StartSample: start, EndSample: start + length - 1,
            FadeInSamples: 0, FadeOutSamples: 0);

    private static double RecoveredAmplitude(double[] field, int start, int length, int fftLength, int bin)
    {
        WindowedSpectrum spectrum = EssHarmonicAnalysis.ComputeWindowedSpectrum(
            field, RectWindow(start, length), fftLength, SampleRate);
        return spectrum.AmplitudeAt(bin);
    }

    [Fact]
    public void ComputeWindowedSpectrum_RecoversToneAmplitude()
    {
        const int length = 1024;
        const int cycles = 40;
        double[] field = WithTone(2_000, length, cycles, 0.5);

        double amplitude = RecoveredAmplitude(field, 2_000, length, length, cycles);
        Assert.Equal(0.5, amplitude, 6);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToTimeShift()
    {
        const int length = 1024;
        const int cycles = 40;
        double[] early = WithTone(1_000, length, cycles, 0.5);
        double[] late = WithTone(6_000, length, cycles, 0.5);

        double a = RecoveredAmplitude(early, 1_000, length, length, cycles);
        double b = RecoveredAmplitude(late, 6_000, length, length, cycles);
        Assert.Equal(a, b, 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToWindowLengthAfterCompensation()
    {
        // Same physical tone, two window lengths. The coherent-gain normalization
        // makes the recovered amplitude identical.
        const int cycles = 40;
        double[] shortField = WithTone(2_000, 1_024, cycles, 0.5);
        double[] longField = WithTone(2_000, 2_048, 2 * cycles, 0.5);

        double shortAmp = RecoveredAmplitude(shortField, 2_000, 1_024, 1_024, cycles);
        double longAmp = RecoveredAmplitude(longField, 2_000, 2_048, 2_048, 2 * cycles);
        Assert.Equal(shortAmp, longAmp, 6);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ZeroPaddingChangesGridNotLevel()
    {
        const int length = 1024;
        const int cycles = 40;
        double[] field = WithTone(2_000, length, cycles, 0.5);

        double tight = RecoveredAmplitude(field, 2_000, length, length, cycles);
        // Zero-pad to 2x: the aligned bin is now 2·cycles, same amplitude.
        double padded = RecoveredAmplitude(field, 2_000, length, 2 * length, 2 * cycles);
        Assert.Equal(tight, padded, 6);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToSign()
    {
        const int length = 1024;
        const int cycles = 40;
        double[] positive = WithTone(2_000, length, cycles, 0.5);
        double[] negative = WithTone(2_000, length, cycles, -0.5);

        double a = RecoveredAmplitude(positive, 2_000, length, length, cycles);
        double b = RecoveredAmplitude(negative, 2_000, length, length, cycles);
        Assert.Equal(a, b, 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ScalingByTwoRaisesLevelBy6Db()
    {
        const int length = 1024;
        const int cycles = 40;
        double[] quiet = WithTone(2_000, length, cycles, 0.25);
        double[] loud = WithTone(2_000, length, cycles, 0.5);

        double quietAmp = RecoveredAmplitude(quiet, 2_000, length, length, cycles);
        double loudAmp = RecoveredAmplitude(loud, 2_000, length, length, cycles);
        double deltaDb = 20.0 * Math.Log10(loudAmp / quietAmp);
        Assert.Equal(6.0206, deltaDb, 3);
    }
}
