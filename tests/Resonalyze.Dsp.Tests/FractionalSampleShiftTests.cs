namespace Resonalyze.Dsp.Tests;

// A sign error in 'advance' is invisible in a magnitude plot and moves every arrival by twice the offset.
public sealed class FractionalSampleShiftTests
{
    [Fact]
    public void AdvanceCircular_WithAWholeShift_MovesTheSamplesUntouched()
    {
        double[] signal = [1.0, 2.0, 3.0, 4.0, 5.0];

        Assert.Equal([3.0, 4.0, 5.0, 1.0, 2.0], FractionalSampleShift.AdvanceCircular(signal, 2));
        Assert.Equal([4.0, 5.0, 1.0, 2.0, 3.0], FractionalSampleShift.AdvanceCircular(signal, -2));
        Assert.Equal(signal, FractionalSampleShift.AdvanceCircular(signal, 0));
        Assert.Equal(signal, FractionalSampleShift.AdvanceCircular(signal, signal.Length));
    }

    [Fact]
    public void AdvanceCircular_LandsWhereTheContinuousSignalIs()
    {
        // A band-limited signal is known between samples, giving an exact reference.
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
        // Energy at Nyquist holds no phase to shift, so only band-limited signals round-trip.
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
