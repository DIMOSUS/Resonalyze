using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// The noise floor is a separate trace (|N|/|H1|), not fused into THD. These pin the
// bias-corrected level: silent capture => no floor; adding white noise gives a floor
// at the level the bias-corrected estimator predicts; doubling the noise lifts it
// 6 dB. The level depends only on the noise region and the noise window (a fixed
// analysis resolution), never on the linear-packet window / sweep geometry, since
// EssNoise no longer reads the linear length.
public sealed class EssNoiseTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const int SweepSamples = 200_000;
    private const int PeakIndex = 150_000;
    private const int ImpulseLength = 300_000;
    private const int NoiseFrom = 162_000; // past the linear packet + guard

    private static EssSweepMetadata Sweep() =>
        EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);

    private static double[] BasePackets()
    {
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0; // |H1| = 1, so the noise-floor ratio equals |N|
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.02;
        return impulse;
    }

    private static void AddNoise(double[] impulse, double sigma, int seed)
    {
        var random = new Random(seed);
        for (int i = NoiseFrom; i < impulse.Length; i++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            impulse[i] += sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }

    private static DistortionSpectrum Run(double[] impulse, DistortionOptions options)
    {
        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));
        NoiseEstimate? noise = options.IncludeNoise
            ? EssNoise.EstimateNoise(impulse, decomposition, options)
            : null;
        return EssDistortion.ComputeDistortion(decomposition, calibration: null, options, noise);
    }

    private static DistortionOptions NoiseOptions => new(MaxHarmonic: 4, IncludeNoise: true);

    private static double BandNoiseFloorDb(DistortionSpectrum spectrum, double lowHz, double highHz)
    {
        double sum = 0.0;
        int count = 0;
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            double f = spectrum.Frequencies[i];
            double ratio = spectrum.NoiseFloorRatio![i];
            if (f >= lowHz && f <= highHz && double.IsFinite(ratio) && ratio > 0.0)
            {
                sum += 20.0 * Math.Log10(ratio);
                count++;
            }
        }

        Assert.True(count > 0);
        return sum / count;
    }

    [Fact]
    public void WithNoNoise_TheFloorIsEmptyAndThdIsUntouched()
    {
        DistortionSpectrum spectrum = Run(BasePackets(), NoiseOptions);

        Assert.NotNull(spectrum.NoiseFloorRatio);
        // Silent region => no noise level anywhere.
        Assert.All(spectrum.NoiseFloorRatio!, v => Assert.True(double.IsNaN(v)));
        // THD is harmonics only (HD2 = 0.02 = -34 dB), independent of the noise trace.
        int probe = Array.FindIndex(spectrum.Frequencies, f => f >= 1_000);
        Assert.True(spectrum.Reliable[probe]);
        Assert.Equal(-33.98, 20.0 * Math.Log10(spectrum.ThdRatio[probe]), 1);
    }

    [Fact]
    public void AddingWhiteNoise_MatchesTheBiasCorrectedLevel()
    {
        const double sigma = 0.002;
        double[] impulse = BasePackets();
        AddNoise(impulse, sigma, seed: 4242);
        DistortionSpectrum spectrum = Run(impulse, NoiseOptions);

        // For rectangular windows the per-bin noise power is sigma^2 * L, so the
        // bias-corrected magnitude is sigma*sqrt(L); with |H1| = 1 that is the floor.
        int noiseLength = (int)Math.Round(SampleRate / spectrum.Noise!.EquivalentNoiseBandwidthHz);
        double expectedDb = 20.0 * Math.Log10(sigma * Math.Sqrt(noiseLength));
        double measuredDb = BandNoiseFloorDb(spectrum, 500, 5_000);
        Assert.Equal(expectedDb, measuredDb, 1.5);
    }

    [Fact]
    public void DoublingTheNoise_RaisesTheFloorBy6Db()
    {
        double[] quiet = BasePackets();
        AddNoise(quiet, 0.001, seed: 7);
        double[] loud = BasePackets();
        AddNoise(loud, 0.002, seed: 7);

        double quietDb = BandNoiseFloorDb(Run(quiet, NoiseOptions), 500, 5_000);
        double loudDb = BandNoiseFloorDb(Run(loud, NoiseOptions), 500, 5_000);
        Assert.Equal(6.0206, loudDb - quietDb, 1);
    }

    [Fact]
    public void LowConfidenceEstimate_ProducesNoNoiseFloor()
    {
        DistortionSpectrum spectrum = Run(
            BasePackets(),
            NoiseOptions with { NoiseWindowCount = 40 });
        Assert.Null(spectrum.NoiseFloorRatio);
    }

    [Fact]
    public void NoiseLevelIsIndependentOfTheLinearPacketWindow()
    {
        // EssNoise reads only the noise region, so the estimate is identical no
        // matter how the linear packet is windowed (here via the fade fraction).
        double[] impulse = BasePackets();
        AddNoise(impulse, 0.001, seed: 11);

        NoiseEstimate WithFade(double fade)
        {
            EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
                impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4, FadeFraction: fade));
            return EssNoise.EstimateNoise(impulse, decomposition, NoiseOptions);
        }

        double[] a = WithFade(0.5).Magnitude;
        double[] b = WithFade(0.25).Magnitude;
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i], 12);
        }
    }
}
