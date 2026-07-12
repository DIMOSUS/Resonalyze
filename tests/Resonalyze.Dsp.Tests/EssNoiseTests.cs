using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// The synthetic gate the noise/THD+N feature must pass before it is shown as THD+N:
// with no noise THD+N collapses to THD; adding white noise raises the floor
// predictably (doubling the noise doubles the noise term); and the result is
// invariant to the noise analysis FFT length after ENBW compensation.
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
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.02;
        return impulse;
    }

    private static void AddNoise(double[] impulse, double sigma, int seed)
    {
        var random = new Random(seed);
        for (int i = NoiseFrom; i < impulse.Length; i++)
        {
            // Box-Muller Gaussian.
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

    private static int ProbeIndex(DistortionSpectrum spectrum, double targetHz)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            double distance = Math.Abs(spectrum.Frequencies[i] - targetHz);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    [Fact]
    public void WithNoNoise_ThdPlusNoiseEqualsThd()
    {
        DistortionSpectrum spectrum = Run(BasePackets(), NoiseOptions);

        Assert.NotNull(spectrum.ThdPlusNoiseRatio);
        Assert.NotNull(spectrum.Noise);
        int probe = ProbeIndex(spectrum, 1_000.0);
        Assert.True(spectrum.Reliable[probe]);
        // The post-peak region is silent, so the noise term is zero.
        Assert.Equal(spectrum.ThdRatio[probe], spectrum.ThdPlusNoiseRatio![probe], 9);
    }

    [Fact]
    public void AddingWhiteNoise_RaisesTheFloorAboveThd()
    {
        double[] impulse = BasePackets();
        AddNoise(impulse, 0.001, seed: 12345);
        DistortionSpectrum spectrum = Run(impulse, NoiseOptions);

        int probe = ProbeIndex(spectrum, 1_000.0);
        Assert.True(spectrum.ThdPlusNoiseRatio![probe] > spectrum.ThdRatio[probe],
            "THD+N must exceed THD once broadband noise is present.");
    }

    [Fact]
    public void DoublingTheNoise_DoublesTheNoiseTerm()
    {
        // The noise term is sqrt(THD+N^2 - THD^2). Doubling sigma should double it.
        double[] quiet = BasePackets();
        AddNoise(quiet, 0.001, seed: 7);
        double[] loud = BasePackets();
        AddNoise(loud, 0.002, seed: 7);

        double quietTerm = BandNoiseTerm(Run(quiet, NoiseOptions), 500, 2_000);
        double loudTerm = BandNoiseTerm(Run(loud, NoiseOptions), 500, 2_000);
        Assert.Equal(2.0, loudTerm / quietTerm, 1); // within ~10%
    }

    [Fact]
    public void NoiseTermIsInvariantToTheAnalysisFftLength()
    {
        double[] impulse = BasePackets();
        AddNoise(impulse, 0.001, seed: 99);

        double longWindow = BandNoiseTerm(
            Run(impulse, NoiseOptions with { NoiseWindowLength = 8_192 }), 500, 2_000);
        double shortWindow = BandNoiseTerm(
            Run(impulse, NoiseOptions with { NoiseWindowLength = 4_096 }), 500, 2_000);

        // ENBW compensation makes the noise level independent of the window length.
        double ratioDb = 20.0 * Math.Log10(longWindow / shortWindow);
        Assert.True(Math.Abs(ratioDb) < 1.5, $"Noise level moved {ratioDb:0.00} dB with FFT length.");
    }

    // Averages the noise term over a band to tame the statistical variance of a
    // per-bin estimate from a handful of noise windows.
    private static double BandNoiseTerm(DistortionSpectrum spectrum, double lowHz, double highHz)
    {
        double sum = 0.0;
        int count = 0;
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            double f = spectrum.Frequencies[i];
            if (f < lowHz || f > highHz)
            {
                continue;
            }

            double thd = spectrum.ThdRatio[i];
            double tpn = spectrum.ThdPlusNoiseRatio![i];
            if (double.IsFinite(thd) && double.IsFinite(tpn))
            {
                sum += Math.Sqrt(Math.Max(0.0, tpn * tpn - thd * thd));
                count++;
            }
        }

        Assert.True(count > 0);
        return sum / count;
    }

    [Fact]
    public void LowConfidenceEstimate_DoesNotProduceThdPlusNoise()
    {
        // Too few noise windows fit, so the estimate is not confident enough and the
        // caller keeps THD instead of a shaky THD+N.
        DistortionSpectrum spectrum = Run(
            BasePackets(),
            NoiseOptions with { NoiseWindowCount = 40 });
        Assert.Null(spectrum.ThdPlusNoiseRatio);
    }
}
