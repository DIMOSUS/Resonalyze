using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The unwrap in BuildMeasuredPhase is anchored to reliable bins (relative
/// magnitude, optionally squared coherence): a noisy null or a low-coherence
/// band contributes its phase to the output but can no longer shift the whole
/// tail by 2π. The corrupted spectra here are crafted so the classic
/// nearest-to-previous-bin unwrap provably mis-accumulates (+2π into the
/// tail): the first garbage bin sits just past −π from its neighbor and the
/// second one steps back in-range, so nothing ever compensates.
/// </summary>
public sealed class PhaseUnwrapTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4096;
    private const int DelaySamples = 24;
    // Bin 512 of 4096 at 48 kHz = 6 kHz, where the true wrapped phase of the
    // 24-sample delay is exactly 0 (512·24/4096 = 3 full turns) — the garbage
    // phases below are chosen relative to that.
    private const int CorruptedBin = 512;
    private const double GarbagePhase1 = -3.12;
    private const double GarbagePhase2 = -1.5;

    [Fact]
    public void Unwrap_BridgesANoisyNull_WithoutShiftingTheTail()
    {
        // A deep null (−80 dB) with garbage phase: the magnitude gate alone
        // must keep the tail on the true delay line.
        SyntheticMeasurement measurement = CreateDelayedImpulseWithCorruptedBins(
            corruptedMagnitude: 1e-4);

        List<SignalPoint> phase = GetUnwrappedPhase(measurement, coherence: null);

        AssertTailOnDelayLine(phase);
    }

    [Fact]
    public void Unwrap_UsesCoherence_WhenTheGarbageBinsHaveFullMagnitude()
    {
        // Full-magnitude garbage (e.g. a masked band in a noisy room): the
        // magnitude gate cannot see it, so only the coherence floor keeps the
        // bins from anchoring the unwrap. The coherence array deliberately
        // uses a coarser grid than the phase FFT (1025 bins = fftLength 2048)
        // to exercise the frequency-based interpolation.
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
        // A garbage null just below the 100 Hz unwrap floor (bin 8 = 93.75 Hz)
        // must stay display-only: if it seeded the anchor, the first reliable
        // bin above the floor would branch against its phase (3.33 rad away
        // from the true value here) and carry a spurious +2π into everything
        // that follows.
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
        // A 20 ms delay accumulates −720° already by 100 Hz. The old unwrap
        // floor returned WRAPPED phase for every bin below 100 Hz (±180°
        // wiggles that contradicted GetExcessPhase's "always unwrapped"
        // contract and stepped at the boundary); the unwrap must start at the
        // first reliable bin instead and keep the whole curve on the delay line.
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
        // The band from 1 to 8 kHz is dead (−80 dB): the turn count inside it
        // is genuinely unknowable (an all-pass or a crossover transition could
        // hide whole turns), so the bridged points must read NaN and a fresh
        // segment must start after the gap — not one confident continuous line
        // through guessed branches.
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

        // The head stays on the absolute delay line.
        foreach (SignalPoint point in phase.Where(item => item.X is >= 100 and <= 950))
        {
            double expected = -Math.Tau * point.X * DelaySamples / SampleRate;
            Assert.InRange(point.Y, expected - 1.0, expected + 1.0);
        }
        // Deep inside the gap the guessed bridge is blanked.
        List<SignalPoint> gap = phase
            .Where(point => point.X is >= 2_000 and <= 7_000)
            .ToList();
        Assert.NotEmpty(gap);
        Assert.All(gap, point => Assert.True(double.IsNaN(point.Y)));
        // After the gap a fresh finite segment continues.
        List<SignalPoint> tail = phase
            .Where(point => point.X is >= 9_000 and <= 18_000)
            .ToList();
        Assert.NotEmpty(tail);
        Assert.All(tail, point => Assert.True(double.IsFinite(point.Y)));
    }

    [Fact]
    public void Unwrap_AQuietBandStaysAnchoredNextToATallResonance()
    {
        // The whole audible band sits 34 dB below a low-frequency resonance
        // (a subwoofer's cabin peak): against the old GLOBAL −30 dB gate that
        // disqualified every bin above the resonance from anchoring, and the
        // slope-frozen bridge lost turns of this 20 ms delay. The gate reads a
        // local octave-smoothed envelope now, so a quiet but locally consistent
        // band anchors normally and the tail stays on the absolute delay line.
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

    // The delayed impulse in the frequency domain (H_k = e^{-i·2πk·d/n}) with
    // two corrupted bins: garbage phases at the given magnitude, mirrored so
    // the time-domain signal stays real.
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

    // Beyond the corrupted band the unwrapped phase must sit on the analytic
    // delay line −2πf·d/sr; a 2π tail shift would miss by ~6.28 rad.
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

        // And the clean stretch before the corruption stays correct too.
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
