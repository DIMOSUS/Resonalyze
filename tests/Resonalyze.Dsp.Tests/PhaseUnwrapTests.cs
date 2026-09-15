using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>The unwrap is anchored to reliable bins; spectra are crafted so a nearest-to-previous unwrap provably adds +2π to the tail.</summary>
public sealed class PhaseUnwrapTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4096;
    private const int DelaySamples = 24;
    // 6 kHz, where the 24-sample delay's wrapped phase is exactly 0 (3 full turns).
    private const int CorruptedBin = 512;
    private const double GarbagePhase1 = -3.12;
    private const double GarbagePhase2 = -1.5;

    [Fact]
    public void Unwrap_BridgesANoisyNull_WithoutShiftingTheTail()
    {
        SyntheticMeasurement measurement = CreateDelayedImpulseWithCorruptedBins(
            corruptedMagnitude: 1e-4);

        List<SignalPoint> phase = GetUnwrappedPhase(measurement, coherence: null);

        AssertTailOnDelayLine(phase);
    }

    [Fact]
    public void Unwrap_UsesCoherence_WhenTheGarbageBinsHaveFullMagnitude()
    {
        // Full-magnitude garbage: only the coherence floor excludes it. Coherence on a coarser grid (1025 bins) exercises interpolation.
        SyntheticMeasurement measurement = CreateDelayedImpulseWithCorruptedBins(
            corruptedMagnitude: 1.0);
        double[] coherence = new double[1025];
        Array.Fill(coherence, 0.95);
        int coherenceFftLength = (coherence.Length - 1) * 2;
        for (int bin = 0; bin < coherence.Length; bin++)
        {
            double f = bin * (double)SampleRate / coherenceFftLength;
            if (f is >= 5_800 and <= 6_200)
            {
                coherence[bin] = 0.1;
            }
        }

        List<SignalPoint> phase = GetUnwrappedPhase(measurement, coherence);

        AssertTailOnDelayLine(phase);
    }

    [Fact]
    public void Unwrap_DoesNotAnchorOnAGarbageBinBelowTheFloor()
    {
        // A garbage null just below the 100 Hz floor must not seed the anchor.
        var spectrum = new Complex[TransformLength];
        for (int k = 0; k < TransformLength; k++)
        {
            double phase = -Math.Tau * k * DelaySamples / TransformLength;
            spectrum[k] = Complex.FromPolarCoordinates(1.0, phase);
        }
        SetBinWithMirror(spectrum, bin: 8, magnitude: 1e-4, phase: 3.0);
        MathNet.Numerics.IntegralTransforms.Fourier.Inverse(
            spectrum,
            MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        var measurement = new SyntheticMeasurement(
            spectrum,
            SampleRate,
            maxMagnitudeIndex: 0);

        List<SignalPoint> phaseData = GetUnwrappedPhase(measurement, coherence: null);

        AssertTailOnDelayLine(phaseData);
    }

    [Fact]
    public void Unwrap_WithUniformlyHighCoherence_MatchesTheUnweightedResult()
    {
        var response = new Complex[TransformLength];
        response[DelaySamples] = Complex.One;
        var measurement = new SyntheticMeasurement(
            response,
            SampleRate,
            maxMagnitudeIndex: 0);
        double[] coherence = new double[1025];
        Array.Fill(coherence, 1.0);

        List<SignalPoint> unweighted = GetUnwrappedPhase(measurement, coherence: null);
        List<SignalPoint> weighted = GetUnwrappedPhase(measurement, coherence);

        Assert.Equal(unweighted.Count, weighted.Count);
        for (int i = 0; i < unweighted.Count; i++)
        {
            Assert.Equal(unweighted[i].X, weighted[i].X);
            Assert.Equal(unweighted[i].Y, weighted[i].Y);
        }
    }

    [Fact]
    public void Unwrap_UnwrapsBelowOneHundredHertzToo()
    {
        // A 20 ms delay is already −720° at 100 Hz: unwrap from the first reliable bin, not a wrapped-below-100 Hz floor.
        const int LongDelaySamples = 960;
        var spectrum = new Complex[TransformLength];
        for (int k = 0; k < TransformLength; k++)
        {
            double binPhase = -Math.Tau * k * LongDelaySamples / TransformLength;
            spectrum[k] = Complex.FromPolarCoordinates(1.0, binPhase);
        }
        MathNet.Numerics.IntegralTransforms.Fourier.Inverse(
            spectrum,
            MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        var measurement = new SyntheticMeasurement(
            spectrum,
            SampleRate,
            maxMagnitudeIndex: 0);

        List<SignalPoint> phase = GetUnwrappedPhase(measurement, coherence: null);

        List<SignalPoint> lowBins = phase
            .Where(point => point.X >= 30 && point.X <= 99)
            .ToList();
        Assert.NotEmpty(lowBins);
        foreach (SignalPoint point in lowBins.Concat(
            phase.Where(item => item.X >= 100 && item.X <= 18_000)))
        {
            double expected = -Math.Tau * point.X * LongDelaySamples / SampleRate;
            Assert.InRange(point.Y, expected - 1.0, expected + 1.0);
        }
    }

    [Fact]
    public void Unwrap_BlanksAGapTooLongToBridge()
    {
        // A dead band's turn count is unknowable: the bridge reads NaN and a fresh segment starts after it.
        var spectrum = new Complex[TransformLength];
        for (int k = 0; k < TransformLength; k++)
        {
            double f = Math.Min(k, TransformLength - k)
                * (double)SampleRate / TransformLength;
            double magnitude = f is > 1_000 and < 8_000 ? 1e-4 : 1.0;
            double binPhase = -Math.Tau * k * DelaySamples / TransformLength;
            spectrum[k] = Complex.FromPolarCoordinates(magnitude, binPhase);
        }
        MathNet.Numerics.IntegralTransforms.Fourier.Inverse(
            spectrum,
            MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        var measurement = new SyntheticMeasurement(
            spectrum,
            SampleRate,
            maxMagnitudeIndex: 0);

        List<SignalPoint> phase = GetUnwrappedPhase(measurement, coherence: null);

        foreach (SignalPoint point in phase.Where(item => item.X is >= 100 and <= 950))
        {
            double expected = -Math.Tau * point.X * DelaySamples / SampleRate;
            Assert.InRange(point.Y, expected - 1.0, expected + 1.0);
        }
        List<SignalPoint> gap = phase
            .Where(point => point.X is >= 2_000 and <= 7_000)
            .ToList();
        Assert.NotEmpty(gap);
        Assert.All(gap, point => Assert.True(double.IsNaN(point.Y)));
        List<SignalPoint> tail = phase
            .Where(point => point.X is >= 9_000 and <= 18_000)
            .ToList();
        Assert.NotEmpty(tail);
        Assert.All(tail, point => Assert.True(double.IsFinite(point.Y)));
    }

    [Fact]
    public void Unwrap_AQuietBandStaysAnchoredNextToATallResonance()
    {
        // A band 34 dB under an LF resonance: the gate reads a local octave-smoothed envelope, not a global −30 dB.
        const int LongDelaySamples = 960;
        var spectrum = new Complex[TransformLength];
        for (int k = 0; k < TransformLength; k++)
        {
            double f = Math.Min(k, TransformLength - k)
                * (double)SampleRate / TransformLength;
            double magnitude = f < 100 ? 50.0 : 1.0;
            double binPhase = -Math.Tau * k * LongDelaySamples / TransformLength;
            spectrum[k] = Complex.FromPolarCoordinates(magnitude, binPhase);
        }
        MathNet.Numerics.IntegralTransforms.Fourier.Inverse(
            spectrum,
            MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        var measurement = new SyntheticMeasurement(
            spectrum,
            SampleRate,
            maxMagnitudeIndex: 0);

        List<SignalPoint> phase = GetUnwrappedPhase(measurement, coherence: null);

        List<SignalPoint> tail = phase
            .Where(point => point.X >= 1_000 && point.X <= 18_000)
            .ToList();
        Assert.NotEmpty(tail);
        foreach (SignalPoint point in tail)
        {
            double expected = -Math.Tau * point.X * LongDelaySamples / SampleRate;
            Assert.InRange(point.Y, expected - 1.0, expected + 1.0);
        }
    }

    private static List<SignalPoint> GetUnwrappedPhase(
        SyntheticMeasurement measurement,
        IReadOnlyList<double>? coherence)
    {
        double[] rectangularWindow =
            Enumerable.Repeat(1.0, TransformLength).ToArray();
        return DataHelper.GetPhaseData(
            measurement,
            offset: 0,
            length: TransformLength,
            window: rectangularWindow,
            unwrap: true,
            coherence);
    }

    // H_k = e^{-i·2πk·d/n} with two corrupted bins, mirrored so the time signal stays real.
    private static SyntheticMeasurement CreateDelayedImpulseWithCorruptedBins(
        double corruptedMagnitude)
    {
        var spectrum = new Complex[TransformLength];
        for (int k = 0; k < TransformLength; k++)
        {
            double phase = -Math.Tau * k * DelaySamples / TransformLength;
            spectrum[k] = Complex.FromPolarCoordinates(1.0, phase);
        }

        SetBinWithMirror(spectrum, CorruptedBin, corruptedMagnitude, GarbagePhase1);
        SetBinWithMirror(spectrum, CorruptedBin + 1, corruptedMagnitude, GarbagePhase2);

        MathNet.Numerics.IntegralTransforms.Fourier.Inverse(
            spectrum,
            MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        return new SyntheticMeasurement(spectrum, SampleRate, maxMagnitudeIndex: 0);
    }

    private static void SetBinWithMirror(
        Complex[] spectrum,
        int bin,
        double magnitude,
        double phase)
    {
        spectrum[bin] = Complex.FromPolarCoordinates(magnitude, phase);
        spectrum[spectrum.Length - bin] = Complex.Conjugate(spectrum[bin]);
    }

    private static void AssertTailOnDelayLine(List<SignalPoint> phase)
    {
        List<SignalPoint> tail = phase
            .Where(point => point.X >= 7_000 && point.X <= 18_000)
            .ToList();
        Assert.NotEmpty(tail);
        foreach (SignalPoint point in tail)
        {
            double expected = -Math.Tau * point.X * DelaySamples / SampleRate;
            Assert.InRange(point.Y, expected - 1.0, expected + 1.0);
        }

        List<SignalPoint> head = phase
            .Where(point => point.X >= 1_000 && point.X <= 5_500)
            .ToList();
        Assert.NotEmpty(head);
        foreach (SignalPoint point in head)
        {
            double expected = -Math.Tau * point.X * DelaySamples / SampleRate;
            Assert.InRange(point.Y, expected - 1.0, expected + 1.0);
        }
    }
}
