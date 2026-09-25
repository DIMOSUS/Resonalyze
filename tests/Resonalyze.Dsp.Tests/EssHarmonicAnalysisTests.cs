using System;
using System.Numerics;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Shared normalization makes HDn = |Hn|/|H1| an honest ratio across window length, padding, sign and level.
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

        // Δt(n) = L · ln(n) / ln(f2/f1); for 10 octaves H2 advances by exactly L/10.
        double duration = sweep.DurationSeconds;
        Assert.Equal(duration / 10.0, EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 2), 9);
        Assert.Equal(0.0, EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 1), 12);

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

        // H3.End and H2.Start share the √6 geometric-mean boundary.
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
    public void MaxExcitationHz_StopsEachHarmonicWhereItsProductEntersTheFadeOut()
    {
        // The app's 20 kHz sweep fades out to Nyquist, and the deconvolution's band gate tapers with it: an HD2 read
        // at 12 kHz sits at 24 kHz, deep in that taper.
        EssSweepMetadata sweep = Sweep() with { FullAmplitudeEndFrequencyHz = 20_000 };

        Assert.Equal(24_000.0, sweep.MaxExcitationHz(1), 6); // the fundamental is not a product
        Assert.Equal(10_000.0, sweep.MaxExcitationHz(2), 6);
        Assert.Equal(20_000.0 / 3, sweep.MaxExcitationHz(3), 6);
        // Unknown or nonsense edges read as flat to the end, as before.
        Assert.Equal(12_000.0, (Sweep() with { FullAmplitudeEndFrequencyHz = 0 }).MaxExcitationHz(2), 6);
        Assert.Equal(12_000.0, (Sweep() with { FullAmplitudeEndFrequencyHz = 30_000 }).MaxExcitationHz(2), 6);
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

        int fft = decomposition.Linear.Spectrum.FftLength;
        Assert.All(decomposition.Harmonics, h => Assert.Equal(fft, h.Spectrum.FftLength));
    }

    // A packet is a contained IR under a unity plateau, so its FFT magnitude is independent of window length and padding.

    private const int Field = 20_000;

    private static HarmonicWindowDefinition RectWindow(int start, int length) =>
        new(Order: 1, PeakSample: start + length / 2, StartSample: start, EndSample: start + length - 1,
            FadeInSamples: 0, FadeOutSamples: 0);

    private static double ImpulseMagnitude(double height, int start, int length, int fftLength, int bin)
    {
        double[] field = new double[Field];
        field[start + length / 2] = height;
        WindowedSpectrum spectrum = EssHarmonicAnalysis.ComputeWindowedSpectrum(
            field, RectWindow(start, length), fftLength, SampleRate);
        return spectrum.AmplitudeAt(bin);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ReadsAContainedImpulseAsItsHeight()
    {
        Assert.Equal(0.5, ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40), 9);
        Assert.Equal(0.5, ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 137), 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_RatioIsIndependentOfTheTwoWindowLengths()
    {
        // 0.02 through 4096 samples vs 1.0 through 1024 still reads exactly 0.02.
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

    // Zero floors: the ceiling equals the detection.
    [Theory]
    [InlineData(0.25, -12.04)]   // the field culprit's order of magnitude
    [InlineData(0.01, -40.0)]    // a healthy electrical path
    public void MeasureHarmonicEnergy_ReportsTheHarmonicAmplitudeRatio(
        double harmonicAmplitude,
        double expectedDb)
    {
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        impulseResponse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] =
            harmonicAmplitude;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        Assert.NotNull(measured);
        Assert.NotNull(measured!.Value.DetectedDb);
        Assert.Equal(expectedDb, measured.Value.DetectedDb!.Value, 1);
        Assert.Equal(measured.Value.DetectedDb.Value, measured.Value.CeilingDb, 6);
        Assert.True(measured.Value.CompleteCoverage);
    }

    // Only nothing detected AND zero floors certifies clean (ceiling -∞); a certified run counts toward the verdict.
    [Fact]
    public void MeasureHarmonicEnergy_CertifiesACleanRecord()
    {
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.Equal(double.NegativeInfinity, measured.Value.CeilingDb);
        Assert.True(measured.Value.CompleteCoverage);
    }

    // H3's upper flank falls off the record: the ceiling covers one order, so CompleteCoverage must be false.
    [Fact]
    public void MeasureHarmonicEnergy_FlagsACeilingThatCoversOnlySomeOrders()
    {
        EssSweepMetadata sweep = EssSweepMetadata.FromExponentialSweep(
            SampleRate, Octaves, SweepSamples, deconvolutionPeakIndex: 34_000);
        var impulseResponse = new double[1 << 19];
        impulseResponse[34_000] = 1.0;
        impulseResponse[34_000 - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 3)] = 0.1;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.Equal(double.NegativeInfinity, measured.Value.CeilingDb);
        Assert.False(measured.Value.CompleteCoverage);
    }

    // Pure noise is neither distortion (accusing the wrong thing) nor clean (its floor could hide anything).
    [Fact]
    public void MeasureHarmonicEnergy_NoiseIsNeitherDistortionNorClean()
    {
        var impulseResponse = new double[1 << 19];
        uint state = 12_345u;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            impulseResponse[i] = state / 4_294_967_296.0 - 0.5;
        }

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        if (measured is { } reading)
        {
            Assert.True(
                reading.DetectedDb is null or < -10.0,
                $"noise reported a detection of {reading.DetectedDb} dB");
            Assert.True(
                reading.CeilingDb > -10.0,
                $"noise certified cleanliness down to {reading.CeilingDb} dB");
        }
    }

    // A -19 dB H2 hidden under a high floor, inside the 6 dB detection margin: the ceiling must expose it.
    [Fact]
    public void MeasureHarmonicEnergy_DoesNotCertifyCleanOverAHighFloor()
    {
        var impulseResponse = new double[1 << 19];
        // Background 0.007: floor ~4.75e-3 per 97-sample probe, bar ~1.89e-2; the 0.11 H2 lands at ~1.69e-2, undetected.
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] = 0.007;
        }
        impulseResponse[PeakIndex] = 1.0;
        impulseResponse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] =
            0.11;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.True(
            measured.Value.CeilingDb > -16.0,
            $"a floor hiding a -19 dB harmonic certified down to {measured.Value.CeilingDb} dB");
    }

    // The ceiling must span the whole isolation window: a harmonic one sample past the probe (radius 48) is still inside.
    [Fact]
    public void MeasureHarmonicEnergy_CeilingCoversTheWholeIsolationWindow()
    {
        EssSweepMetadata sweep = Sweep();
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        int h2 = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2);
        impulseResponse[h2 + 49] = 0.1;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.True(measured.Value.CompleteCoverage);
        Assert.Equal(-20.0, measured.Value.CeilingDb, 1);
    }

    // Quiet packet interior beside a noisy flank: subtracting the flank floor under-reserved (-26.2 dB vs a -25.2 dB harmonic).
    [Fact]
    public void MeasureHarmonicEnergy_CeilingCoversAHarmonicOverAQuietInterior()
    {
        EssSweepMetadata sweep = Sweep();
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        // Only H5's upper flank (5.5 order) is raised: H5 floor ~8.0e-4, bar ~3.18e-3.
        int flank = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 5.5);
        for (int i = flank - 48; i <= flank + 48; i++)
        {
            impulseResponse[i] = 0.00287;
        }
        impulseResponse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 5)] =
            0.055;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.True(
            measured.Value.CeilingDb >= -25.3,
            $"a -25.2 dB harmonic hid under a ceiling of {measured.Value.CeilingDb} dB");
    }

    // One quiet stretch must not become a global floor; each order stands against its own two flanks.
    [Fact]
    public void MeasureHarmonicEnergy_IgnoresOneAnomalouslyQuietProbe()
    {
        EssSweepMetadata sweep = Sweep();
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        uint state = 4_242u;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            impulseResponse[i] += (state / 4_294_967_296.0 - 0.5) * 0.001;
        }
        int notch = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 4.5);
        for (int i = notch - 200; i <= notch + 200; i++)
        {
            impulseResponse[i] = 0.0;
        }

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.True(
            measured is null || measured.Value.DetectedDb is null or <= -10.0,
            $"a background notch was read as {measured?.DetectedDb} dB of harmonic content");
    }

    // Index 0 is the record start: geometry before it is unmeasurable, never read from the buffer end.
    [Fact]
    public void MeasureHarmonicEnergy_RefusesGeometryThatFallsOffTheRecord()
    {
        var early = new EssSweepMetadata(
            StartFrequencyHz: 20,
            EndFrequencyHz: 20_000,
            DurationSeconds: SweepSamples / (double)SampleRate,
            SampleRateHz: SampleRate,
            SweepSampleCount: SweepSamples,
            DeconvolutionPeakIndex: 100);
        var impulseResponse = new double[1 << 19];
        impulseResponse[100] = 1.0;
        impulseResponse[^2000] = 0.5;

        Assert.Null(EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, early));
    }

    // Inclusive convention: Start and End are both inside the window.
    [Theory]
    [InlineData(100, 199, 100)]
    [InlineData(100, 100, 1)]
    [InlineData(0, 4095, 4096)]
    public void HarmonicWindowDefinition_NominalLengthCountsBothEdges(
        int startSample,
        int endSample,
        int expected)
    {
        var window = new HarmonicWindowDefinition(
            Order: 2,
            PeakSample: (startSample + endSample) / 2,
            StartSample: startSample,
            EndSample: endSample,
            FadeInSamples: 8,
            FadeOutSamples: 8);

        Assert.Equal(expected, window.NominalLength);
    }
}
