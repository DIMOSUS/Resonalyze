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

    [Fact]
    public void FindPeak_FirstArrivalPrefersEarlierPeakAboveThreshold()
    {
        double[] envelope = [0, 0.45, 0.10, 0.80, 0.20, 0, 0, 0];

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 10,
                FirstPeakMinimumSnrDb = 0,
                SearchWindowMilliseconds = 10
            });

        Assert.Equal(1, result.SelectedIndex);
        Assert.Equal(3, result.StrongestIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void FindPeak_StrongestPeakReturnsMaximumPeak()
    {
        double[] envelope = [0, 0.45, 0.10, 0.80, 0.20, 0, 0, 0];

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.StrongestPeak,
                SearchWindowMilliseconds = 10
            });

        Assert.Equal(3, result.SelectedIndex);
        Assert.Equal(3, result.StrongestIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void FindPeak_UsesFallbackWhenNoEarlierPeakPassesThreshold()
    {
        double[] envelope = [0, 0.20, 0.10, 0.80, 0.20, 0, 0, 0];

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 6,
                FirstPeakMinimumSnrDb = 0,
                SearchWindowMilliseconds = 10
            });

        Assert.Equal(3, result.SelectedIndex);
        Assert.Equal(3, result.StrongestIndex);
        Assert.True(result.FallbackUsed);
    }

    [Fact]
    public void FindFractionalPeakOffset_ClampsToHalfSample()
    {
        double offset = SignalEnvelope.FindFractionalPeakOffset(
            previous: 0.0,
            center: 1.0,
            next: 10.0);

        Assert.Equal(-0.5, offset);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_ExcludesWrappedPeakNeighborhood()
    {
        double[] envelope = Enumerable.Repeat(0.01, 1000).ToArray();
        envelope[998] = 1.0;
        envelope[999] = 1.0;
        envelope[0] = 1.0;
        envelope[1] = 1.0;
        envelope[2] = 1.0;
        envelope[3] = 1.0;
        envelope[4] = 1.0;

        double confidence = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope,
            peakIndex: 2,
            peak: 1.0);

        Assert.InRange(confidence, 39.9, 40.1);
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
