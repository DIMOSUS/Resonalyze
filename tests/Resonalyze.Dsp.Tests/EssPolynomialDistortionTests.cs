using System;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// The mandatory end-to-end test: a real exponential sine sweep is driven through a
// known memoryless nonlinearity y = x + a2 x^2 + a3 x^3, deconvolved with the
// production inverse filter, and run through the production harmonic decomposition.
// It pins that HD2/HD3 land in the right packets, are drawn against the excitation
// frequency, stop at Nyquist/order, and match the levels the trig expansion of the
// polynomial predicts — the check the old one-window THD could never pass.
public sealed class EssPolynomialDistortionTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const double DurationSeconds = 3.0;

    // sin^2 = 1/2 - 1/2 cos2φ and sin^3 = 3/4 sinφ - 1/4 sin3φ, so for
    // y = x + a2 x^2 + a3 x^3 with x = sinφ the steady-state amplitudes are:
    //   fundamental g1 = 1 + 3 a3 / 4,   H2 = a2 / 2,   H3 = a3 / 4.
    private const double A2 = 0.05;
    private const double A3 = 0.02;
    private static double G1 => 1.0 + 3.0 * A3 / 4.0;
    private static double ExpectedHd2Db => 20.0 * Math.Log10((A2 / 2.0) / G1);
    private static double ExpectedHd3Db => 20.0 * Math.Log10((A3 / 4.0) / G1);

    private sealed record Sweep(float[] Signal, float[] Inverse, int SampleCount);

    // Reproduces ExponentialSineSweep's generation (kept in the app project) so the
    // DSP test can exercise the real deconvolution against a genuine sweep.
    private static Sweep GenerateSweep()
    {
        double frequencyRatio = Math.Pow(2.0, Octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;
        double targetLength = SampleRate * DurationSeconds;
        double cycleCount = Math.Max(1, Math.Round(phaseFactor * targetLength / (2.0 * Math.PI)));
        double exactLength = cycleCount * 2.0 * Math.PI / phaseFactor;
        int sampleCount = Math.Max(1, (int)Math.Round(exactLength));

        float[] signal = new float[sampleCount];
        float[] inverse = new float[sampleCount];
        double octaveLength = sampleCount / (double)Octaves;
        for (int i = 0; i < sampleCount; i++)
        {
            double exponentialPosition = Math.Exp(i / (double)sampleCount * logarithmicRatio);
            signal[i] = (float)Math.Sin(phaseFactor * exactLength * exponentialPosition) *
                (float)Math.Min(i / octaveLength, 1.0);
        }

        double inverseScale = Octaves * Math.Log(2.0) / (1.0 - Math.Pow(2.0, -Octaves));
        double perSampleDecay = Math.Pow(2.0, Octaves / (double)sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            inverse[i] = (float)(signal[sampleCount - i - 1] * Math.Pow(perSampleDecay, -i) * inverseScale);
        }

        return new Sweep(signal, inverse, sampleCount);
    }

    private static DistortionSpectrum RunPolynomial(int leadingSilence = 0)
    {
        Sweep sweep = GenerateSweep();

        float[] recorded = new float[leadingSilence + sweep.SampleCount];
        for (int i = 0; i < sweep.SampleCount; i++)
        {
            double x = sweep.Signal[i];
            recorded[leadingSilence + i] = (float)(x + A2 * x * x + A3 * x * x * x);
        }

        SweepDeconvolutionResult deconvolution = SweepAnalysis.DeconvolveWithInverseFilter(
            recorded, sweep.Inverse, 2.0 / sweep.Inverse.Length);

        double[] impulse = deconvolution.ImpulseResponse;
        var metadata = EssSweepMetadata.FromExponentialSweep(
            SampleRate, Octaves, sweep.SampleCount, deconvolution.PeakIndex);

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, metadata, new HarmonicAnalysisOptions(MaxHarmonic: 4));
        return EssDistortion.ComputeDistortion(
            decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 4));
    }

    private static double MedianDbInBand(
        DistortionSpectrum spectrum, double[] ratio, double lowHz, double highHz)
    {
        var values = new List<double>();
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            double f = spectrum.Frequencies[i];
            if (f >= lowHz && f <= highHz && double.IsFinite(ratio[i]) && ratio[i] > 0.0)
            {
                values.Add(20.0 * Math.Log10(ratio[i]));
            }
        }

        Assert.NotEmpty(values);
        values.Sort();
        return values[values.Count / 2];
    }

    [Fact]
    public void Polynomial_ProducesHd2AndHd3AtTheExpectedLevels()
    {
        DistortionSpectrum spectrum = RunPolynomial();

        // Memoryless distortion is flat with frequency; check a clean mid band well
        // away from the sweep edges. Tolerances allow for deconvolution ripple and
        // the finite window.
        double hd2 = MedianDbInBand(spectrum, spectrum.HarmonicDistortionRatio[2], 200, 2_000);
        double hd3 = MedianDbInBand(spectrum, spectrum.HarmonicDistortionRatio[3], 200, 2_000);

        Assert.Equal(ExpectedHd2Db, hd2, 1.5);
        Assert.Equal(ExpectedHd3Db, hd3, 2.0);
    }

    [Fact]
    public void Polynomial_Hd3RidesBelowHd2ByTheExpectedSpacing()
    {
        DistortionSpectrum spectrum = RunPolynomial();
        double hd2 = MedianDbInBand(spectrum, spectrum.HarmonicDistortionRatio[2], 200, 2_000);
        double hd3 = MedianDbInBand(spectrum, spectrum.HarmonicDistortionRatio[3], 200, 2_000);

        // Expected spacing HD2 - HD3 = 20 log10((a2/2)/(a3/4)).
        double expectedSpacing = ExpectedHd2Db - ExpectedHd3Db;
        Assert.Equal(expectedSpacing, hd2 - hd3, 1.5);
    }

    [Fact]
    public void Polynomial_Hd2StopsAtNyquistOverTwo()
    {
        DistortionSpectrum spectrum = RunPolynomial();
        double[] hd2 = spectrum.HarmonicDistortionRatio[2];

        // Above Nyquist/2 the second-harmonic product passes Nyquist, so HD2 must be
        // masked (NaN), while it carries real values below.
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            if (spectrum.Frequencies[i] > 13_000)
            {
                Assert.True(double.IsNaN(hd2[i]),
                    $"HD2 must be masked above Nyquist/2, was finite at {spectrum.Frequencies[i]:0} Hz.");
            }
        }

        Assert.Contains(hd2, v => double.IsFinite(v));
    }

    [Fact]
    public void Polynomial_LevelsAreInvariantToARecordingTimeShift()
    {
        // Shifting the whole capture moves the deconvolution peak but not the
        // relative packet geometry, so HDn is unchanged — the energy/magnitude
        // pipeline does not depend on absolute placement or on packet phase.
        double aligned = MedianDbInBand(
            RunPolynomial(0), RunPolynomial(0).HarmonicDistortionRatio[2], 200, 2_000);
        double shifted = MedianDbInBand(
            RunPolynomial(1_234), RunPolynomial(1_234).HarmonicDistortionRatio[2], 200, 2_000);
        Assert.Equal(aligned, shifted, 1);
    }
}
