namespace Resonalyze.Dsp.Tests;

// Moving a signal by part of a sample. The tests below check the two things that make
// such a shift trustworthy: that a whole-sample shift changes nothing at all, and that a
// fractional one lands exactly where the underlying continuous signal says it should —
// including which direction "advance" means, since a sign error here is invisible in a
// magnitude plot and moves every arrival by twice the offset.
public sealed class FractionalSampleShiftTests
{
    [Fact]
    public void AdvanceCircular_WithAWholeShift_MovesTheSamplesUntouched()
    {
        double[] signal = [1.0, 2.0, 3.0, 4.0, 5.0];

        Assert.Equal([3.0, 4.0, 5.0, 1.0, 2.0], FractionalSampleShift.AdvanceCircular(signal, 2));
        // A negative shift delays, and the tail wraps back to the head.
        Assert.Equal([4.0, 5.0, 1.0, 2.0, 3.0], FractionalSampleShift.AdvanceCircular(signal, -2));
        Assert.Equal(signal, FractionalSampleShift.AdvanceCircular(signal, 0));
        Assert.Equal(signal, FractionalSampleShift.AdvanceCircular(signal, signal.Length));
    }

    [Fact]
    public void AdvanceCircular_LandsWhereTheContinuousSignalIs()
    {
        // A band-limited signal is known everywhere between its samples, so there is an
        // exact answer to compare against: three partials well below Nyquist, evaluated
        // at the shifted times.
        const int n = 128;
        const double shift = 7.37;
        (int Bin, double Amplitude, double Phase)[] partials =
            [(3, 1.0, 0.4), (11, 0.5, -1.1), (29, 0.25, 2.2)];

        double At(double t) => partials.Sum(p =>
            p.Amplitude * Math.Cos((2.0 * Math.PI * p.Bin * t / n) + p.Phase));

        double[] signal = [.. Enumerable.Range(0, n).Select(i => At(i))];

        double[] shifted = FractionalSampleShift.AdvanceCircular(signal, shift);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(At(i + shift), shifted[i], 10);
        }
    }

    [Fact]
    public void AdvanceCircular_ThereAndBack_ReturnsTheSignal()
    {
        const int n = 256;
        var random = new Random(20260820);
        // Band-limited by construction: energy only in the lower eighth of the spectrum,
        // which is what a sweep measured to 20 kHz at 96 kHz looks like. A signal with
        // energy at Nyquist could not survive this round trip and should not: that bin
        // holds no phase to shift, so an exact shift can only scale it.
        double[] amplitudes = [.. Enumerable.Range(0, (n / 8) + 1).Select(_ => random.NextDouble())];
        double[] signal = [.. Enumerable.Range(0, n).Select(i =>
            Enumerable.Range(1, n / 8).Sum(k =>
                amplitudes[k] * Math.Cos((2.0 * Math.PI * k * i / n) + k)))];

        double[] roundTrip = FractionalSampleShift.AdvanceCircular(
            FractionalSampleShift.AdvanceCircular(signal, 0.37), -0.37);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(signal[i], roundTrip[i], 9);
        }
    }

    [Fact]
    public void AdvanceCircular_RefusesWhatItCannotShift()
    {
        Assert.Throws<ArgumentNullException>(() => FractionalSampleShift.AdvanceCircular(null!, 1.0));
        Assert.Throws<ArgumentException>(() => FractionalSampleShift.AdvanceCircular([], 1.0));
        Assert.Throws<ArgumentException>(() => FractionalSampleShift.AdvanceCircular([1.0], double.NaN));
    }
}
