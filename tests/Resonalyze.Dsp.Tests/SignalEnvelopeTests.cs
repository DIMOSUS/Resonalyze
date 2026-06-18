namespace Resonalyze.Dsp.Tests;

public sealed class SignalEnvelopeTests
{
    [Fact]
    public void Envelope_PreservesConstantDcLevel()
    {
        double[] signal = Enumerable.Repeat(0.75, 64).ToArray();

        double[] envelope = SignalEnvelope.Envelope(signal);

        Assert.Equal(signal.Length, envelope.Length);
        Assert.All(envelope, sample => Assert.Equal(0.75, sample, precision: 10));
    }

    [Fact]
    public void Envelope_ReturnsConstantMagnitudeForBinCenteredSine()
    {
        const int length = 256;
        const int bin = 7;
        const double amplitude = 1.5;
        double[] signal = CreateSine(length, bin, amplitude);

        double[] envelope = SignalEnvelope.Envelope(signal);

        Assert.All(envelope, sample => Assert.Equal(amplitude, sample, precision: 10));
    }

    [Fact]
    public void Envelope_ReturnsConstantMagnitudeForOddLengthBinCenteredCosine()
    {
        const int length = 255;
        const int bin = 9;
        const double amplitude = 0.625;
        double[] signal = CreateCosine(length, bin, amplitude);

        double[] envelope = SignalEnvelope.Envelope(signal);

        Assert.All(envelope, sample => Assert.Equal(amplitude, sample, precision: 10));
    }

    [Fact]
    public void Envelope_RejectsEmptySignal()
    {
        Assert.Throws<ArgumentException>(() => SignalEnvelope.Envelope([]));
    }

    private static double[] CreateSine(int length, int bin, double amplitude)
    {
        var signal = new double[length];
        for (int i = 0; i < length; i++)
        {
            signal[i] = amplitude * Math.Sin(2.0 * Math.PI * bin * i / length);
        }

        return signal;
    }

    private static double[] CreateCosine(int length, int bin, double amplitude)
    {
        var signal = new double[length];
        for (int i = 0; i < length; i++)
        {
            signal[i] = amplitude * Math.Cos(2.0 * Math.PI * bin * i / length);
        }

        return signal;
    }
}
