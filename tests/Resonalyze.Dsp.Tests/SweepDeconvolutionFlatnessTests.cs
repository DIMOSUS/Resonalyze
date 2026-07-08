using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Round-trip flatness of <see cref="SweepAnalysis.DeconvolveWithInverseFilter"/>.
/// An exponential sine sweep played through a unity ("wire") system and
/// deconvolved with its own amplitude-compensated inverse filter must collapse
/// to a clean impulse whose in-band magnitude response is flat. This pins two
/// numbers that are easy to break silently: the <c>2/N</c> normalization the
/// measurement layer applies (<c>ExpSweepMeasurement</c> passes
/// <c>2.0 / InverseFilter.Length</c>) and the exponential envelope compensation
/// baked into the inverse filter — a drift in either would tilt or ripple the
/// response instead of leaving it flat.
///
/// The sweep and inverse filter are regenerated here rather than reused from
/// <c>ExponentialSineSweep</c>, which lives in the Windows-only app project;
/// the math mirrors it exactly so the test still guards the real signal chain.
/// </summary>
public sealed class SweepDeconvolutionFlatnessTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const double RequestedDuration = 1.0;

    [Fact]
    public void UnityRoundTrip_IsFlatInBand()
    {
        GeneratedSweep sweep = GenerateSweep(Octaves, RequestedDuration, SampleRate);

        // Recording the sweep through a unity system is just the sweep itself.
        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            sweep.Sweep,
            sweep.InverseFilter,
            2.0 / sweep.InverseFilter.Length);

        double[] magnitudeDb = MagnitudeSpectrumDb(result.ImpulseResponse, out int spectrumLength);

        // Restrict to the reliable interior of the swept band: skip the first
        // octave (the sweep fades in there) and stay below the top of the range.
        double lowHz = sweep.StartFrequency * 4.0;
        double highHz = sweep.EndFrequency * 0.5;
        double binToHz = SampleRate / (double)spectrumLength;

        var inBand = new List<double>();
        for (int bin = 1; bin < magnitudeDb.Length; bin++)
        {
            double f = bin * binToHz;
            if (f >= lowHz && f <= highHz)
            {
                inBand.Add(magnitudeDb[bin]);
            }
        }

        Assert.NotEmpty(inBand);
        double mean = inBand.Average();
        double maxDeviation = inBand.Max(db => Math.Abs(db - mean));

        // In-band ripple is a fraction of a dB; a broken normalization or a
        // dropped envelope term would blow this well past 1 dB.
        Assert.True(
            maxDeviation < 0.25,
            $"In-band ripple {maxDeviation:F4} dB around mean {mean:F4} dB exceeded 0.25 dB.");

        // The 2/N scaling puts the flat band near unity gain (0 dB); pin it so a
        // scaling regression that stays flat but shifts level is still caught.
        Assert.InRange(mean, -1.0, 1.0);
    }

    private static double[] MagnitudeSpectrumDb(double[] impulseResponse, out int spectrumLength)
    {
        int fftLength = DspMath.NextPowerOfTwo(impulseResponse.Length);
        spectrumLength = fftLength;
        var spectrum = new Complex[fftLength];
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            spectrum[i] = new Complex(impulseResponse[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        int half = fftLength / 2 + 1;
        var magnitudeDb = new double[half];
        for (int bin = 0; bin < half; bin++)
        {
            double magnitude = spectrum[bin].Magnitude;
            magnitudeDb[bin] = 20.0 * Math.Log10(Math.Max(magnitude, 1e-12));
        }

        return magnitudeDb;
    }

    private readonly record struct GeneratedSweep(
        float[] Sweep,
        float[] InverseFilter,
        double StartFrequency,
        double EndFrequency);

    // Mirrors ExponentialSineSweep: quantized cycle length, amplitude-ramped
    // sweep and envelope-compensated, time-reversed inverse filter.
    private static GeneratedSweep GenerateSweep(int octaves, double requestedDuration, int sampleRate)
    {
        double frequencyRatio = Math.Pow(2.0, octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;

        double targetLength = sampleRate * requestedDuration;
        double cycleCount = Math.Max(
            1,
            Math.Round(phaseFactor * targetLength / (2.0 * Math.PI)));
        double exactLength = cycleCount * 2.0 * Math.PI / phaseFactor;
        int sampleCount = Math.Max(1, (int)Math.Round(exactLength));

        var sweep = new float[sampleCount];
        var inverseFilter = new float[sampleCount];
        double octaveLength = sampleCount / (double)octaves;
        for (int i = 0; i < sampleCount; i++)
        {
            double exponentialPosition = Math.Exp(i / (double)sampleCount * logarithmicRatio);
            sweep[i] =
                (float)Math.Sin(phaseFactor * exactLength * exponentialPosition) *
                (float)Math.Min(i / octaveLength, 1.0);
        }

        double inverseScale = octaves * Math.Log(2.0) / (1.0 - Math.Pow(2.0, -octaves));
        double perSampleDecay = Math.Pow(2.0, octaves / (double)sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            inverseFilter[i] =
                (float)(sweep[sampleCount - i - 1] * Math.Pow(perSampleDecay, -i) * inverseScale);
        }

        // Instantaneous angular frequency (rad/sample) of the sweep phase
        // phaseFactor·exactLength·exp(i/N·L) is its derivative in i.
        double startOmega = phaseFactor * exactLength * logarithmicRatio / sampleCount;
        double startFrequency = startOmega / (2.0 * Math.PI) * sampleRate;
        double endFrequency = startFrequency * frequencyRatio;

        return new GeneratedSweep(sweep, inverseFilter, startFrequency, endFrequency);
    }
}
