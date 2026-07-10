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
    public void FindFractionalPeakOffset_FlatTripleReturnsZero()
    {
        // previous - 2*center + next == 0: the parabola is degenerate, so the offset
        // must be exactly the flat-guard value rather than a division by ~zero.
        Assert.Equal(0.0, SignalEnvelope.FindFractionalPeakOffset(1.0, 1.0, 1.0));
    }

    [Fact]
    public void FindFractionalPeakOffset_ReturnsTheParabolicVertex()
    {
        // 0.5 * (previous - next) / (previous - 2*center + next)
        // = 0.5 * (1 - 2) / (1 - 8 + 2) = 0.5 * (-1) / (-5) = 0.1.
        Assert.Equal(0.1, SignalEnvelope.FindFractionalPeakOffset(1.0, 4.0, 2.0), precision: 12);
    }

    [Fact]
    public void FindPeak_SnrGateRejectsASubNoiseEarlyBumpUnlessSnrIsRelaxed()
    {
        // A 0.01 noise bed with an early bump (0.08) and a much later strong peak
        // (1.0). The bump clears the -25 dB-below-max threshold (0.056), so only the
        // SNR gate can decide it. Raising FirstPeakMinimumSnrDb lifts the noise-based
        // threshold above the bump; relaxing it lets the bump through. This is the
        // only lever that changes, so the flip pins the SNR branch.
        var envelope = new double[2_000];
        Array.Fill(envelope, 0.01);
        envelope[100] = 0.08; // early candidate arrival
        envelope[500] = 1.0;  // dominant peak

        PeakSearchResult strict = SignalEnvelope.FindPeak(
            envelope, 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 30,
            });
        PeakSearchResult relaxed = SignalEnvelope.FindPeak(
            envelope, 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0,
            });

        Assert.Equal(500, strict.SelectedIndex);  // sub-SNR bump rejected -> strongest peak
        Assert.Equal(100, relaxed.SelectedIndex);  // bump accepted as the first arrival
    }

    [Fact]
    public void FindPeak_RejectsASymmetricPreRingingSidelobeOfAStrongerPeak()
    {
        // A zero-phase kernel rings symmetrically: the early bump at 14 has an
        // equal-height mirror at 26 around the main peak at 20, so it must be
        // read as pre-ringing, not as an earlier arrival.
        var envelope = new double[64];
        envelope[13] = 0.05;
        envelope[14] = 0.2;
        envelope[15] = 0.05;
        envelope[19] = 0.5;
        envelope[20] = 1.0;
        envelope[21] = 0.5;
        envelope[25] = 0.05;
        envelope[26] = 0.2;
        envelope[27] = 0.05;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0,
                SearchWindowMilliseconds = 1
            });

        Assert.Equal(20, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void FindPeak_KeepsAGenuineEarlyArrivalWithoutAMirrorCounterpart()
    {
        // Same early bump, but nothing at the mirrored position after the main
        // peak — a genuine earlier arrival, so it must stay the first arrival.
        var envelope = new double[64];
        envelope[13] = 0.05;
        envelope[14] = 0.2;
        envelope[15] = 0.05;
        envelope[19] = 0.5;
        envelope[20] = 1.0;
        envelope[21] = 0.5;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0,
                SearchWindowMilliseconds = 1
            });

        Assert.Equal(14, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_ReadsTheQuietFloorNotThePeak()
    {
        // A flat 0.01 floor with a peak cluster (wrapping the array end): the
        // noise estimate must read the floor, so peak/floor = 40 dB.
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
            peak: 1.0);

        Assert.InRange(confidence, 39.9, 40.1);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_ReverbTailDoesNotCountAsNoise()
    {
        // Half the record is a −20 dB reverb tail over a 0.001 floor. The old
        // everything-but-the-peak mean read the tail as noise (~ −20 dB SNR
        // reference → ~20 dB grade); the quietest-quarter floor must grade the
        // recording by its true 60 dB headroom.
        double[] envelope = Enumerable.Repeat(0.001, 1000).ToArray();
        for (int i = 100; i < 600; i++)
        {
            envelope[i] = 0.1;
        }
        envelope[100] = 1.0;

        double confidence = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope,
            peak: 1.0);

        Assert.InRange(confidence, 59.9, 60.1);
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
