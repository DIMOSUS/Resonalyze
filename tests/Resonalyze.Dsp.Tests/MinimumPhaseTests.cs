using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class MinimumPhaseTests
{
    private const int Length = 4096;

    [Theory]
    [InlineData(0.5)]
    [InlineData(-0.5)]
    [InlineData(0.9)]
    public void Reconstructs_PhaseOfMinimumPhaseFilter_FromMagnitudeAlone(double zero)
    {
        // H(z) = 1 - zero * z^-1 with |zero| < 1 is minimum phase, so the phase
        // recovered from its magnitude must match the filter's true phase.
        Complex[] spectrum = TransferFunction(impulse: [1.0, -zero]);
        double[] magnitude = spectrum.Select(value => value.Magnitude).ToArray();

        double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude);

        for (int bin = 1; bin < Length / 2; bin++)
        {
            double expected = spectrum[bin].Phase;
            double error = WrapToPi(minimumPhase[bin] - expected);
            Assert.InRange(error, -1e-6, 1e-6);
        }
    }

    [Fact]
    public void PureDelay_HasFlatMagnitude_AndZeroMinimumPhase()
    {
        // A pure delay is all-pass: its phase is entirely excess, so the
        // minimum-phase component derived from the (flat) magnitude is ~0.
        var impulse = new double[Length];
        impulse[37] = 1.0;
        Complex[] spectrum = TransferFunction(impulse);
        double[] magnitude = spectrum.Select(value => value.Magnitude).ToArray();

        double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude);

        Assert.All(minimumPhase, value => Assert.InRange(value, -1e-9, 1e-9));
    }

    [Fact]
    public void Reconstruct_PreservesMagnitude()
    {
        Complex[] spectrum = TransferFunction(impulse: [1.0, -0.7, 0.2]);
        double[] magnitude = spectrum.Select(value => value.Magnitude).ToArray();

        Complex[] reconstructed = MinimumPhase.Reconstruct(magnitude);

        for (int bin = 0; bin < Length; bin++)
        {
            Assert.InRange(
                reconstructed[bin].Magnitude - magnitude[bin],
                -1e-9,
                1e-9);
        }
    }

    [Fact]
    public void NonMinimumPhaseFilter_DiffersFromMeasuredPhase()
    {
        // H(z) = 1 - 1.5 z^-1 has a zero outside the unit circle, so it is NOT
        // minimum phase: the recovered minimum phase must differ from the measured
        // phase by a non-trivial (all-pass) excess component.
        Complex[] spectrum = TransferFunction(impulse: [1.0, -1.5]);
        double[] magnitude = spectrum.Select(value => value.Magnitude).ToArray();

        double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude);

        double maxExcess = 0;
        for (int bin = 1; bin < Length / 2; bin++)
        {
            double excess = Math.Abs(WrapToPi(spectrum[bin].Phase - minimumPhase[bin]));
            maxExcess = Math.Max(maxExcess, excess);
        }

        Assert.True(maxExcess > 0.5);
    }

    [Fact]
    public void FromMagnitude_RejectsEmptyInput()
    {
        Assert.Throws<ArgumentException>(() => MinimumPhase.FromMagnitude([]));
    }

    [Theory]
    [InlineData(1e-12)]
    [InlineData(1e+12)]
    public void FromMagnitude_IsInvariantToOverallGain(double gain)
    {
        // Minimum phase is determined by the SHAPE of log|H|: an overall gain
        // only moves the zeroth cepstral coefficient. With an absolute floor a
        // quiet measurement's spectrum sank into the clamp and its phase
        // changed with its level — the floor must be relative to the peak.
        double[] magnitude = new double[Length];
        for (int i = 0; i < Length; i++)
        {
            double f = Math.Min(i, Length - i) / (double)Length;
            magnitude[i] = 0.01 + Math.Exp(-Math.Pow((f - 0.1) / 0.05, 2.0));
        }

        double[] reference = MinimumPhase.FromMagnitude(magnitude);
        double[] scaled = MinimumPhase.FromMagnitude(
            Array.ConvertAll(magnitude, value => value * gain));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(reference[i], scaled[i], 9);
        }
    }

    [Fact]
    public void FromMagnitude_NonFiniteBinsDoNotPoisonTheCepstrum()
    {
        // A NaN magnitude used to slip through Math.Max into the log and turn
        // the whole cepstrum — and every phase bin — into NaN.
        double[] magnitude = new double[Length];
        Array.Fill(magnitude, 1.0);
        magnitude[100] = double.NaN;
        magnitude[200] = double.PositiveInfinity;
        magnitude[300] = -1.0;

        double[] phase = MinimumPhase.FromMagnitude(magnitude);

        Assert.All(phase, value => Assert.True(double.IsFinite(value)));
    }

    private static Complex[] TransferFunction(double[] impulse)
    {
        var buffer = new Complex[Length];
        for (int i = 0; i < impulse.Length; i++)
        {
            buffer[i] = new Complex(impulse[i], 0.0);
        }

        Fourier.Forward(buffer, FourierOptions.Matlab);
        return buffer;
    }

    private static double WrapToPi(double angle)
    {
        return Math.Atan2(Math.Sin(angle), Math.Cos(angle));
    }
}
