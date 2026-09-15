using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

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
    public void FindPeak_ReAnchorsOnAGlobalPeakBeyondAnEmptyWindow()
    {
        // Chain latency parks the IR beyond the window: re-anchor on the global envelope maximum.
        var envelope = new double[48_000];
        Array.Fill(envelope, 1e-6);
        envelope[19_999] = 0.6;
        envelope[20_000] = 1.0; // ~417 ms, far beyond the 80 ms window
        envelope[20_001] = 0.6;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                SearchWindowMilliseconds = 80
            });

        Assert.Equal(20_000, result.StrongestIndex);
        Assert.Equal(20_000, result.SelectedIndex);
        Assert.NotEqual(0, result.SearchRotation);
    }

    [Fact]
    public void FindPeak_ReAnchorsPastALoudSeamResidue()
    {
        // 3RC head: acausal residue wrapped across the seam, loud but still beyond the search depth.
        var envelope = new double[48_000];
        Array.Fill(envelope, 1e-6);
        for (int i = 0; i < 200; i++)
        {
            envelope[i] = Math.Max(1e-6, 0.02 * Math.Exp(-i / 12.0));
        }
        envelope[19_999] = 0.6;
        envelope[20_000] = 1.0;
        envelope[20_001] = 0.6;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                SearchWindowMilliseconds = 80
            });

        Assert.Equal(20_000, result.StrongestIndex);
        Assert.Equal(20_000, result.SelectedIndex);
        Assert.NotEqual(0, result.SearchRotation);
    }

    [Fact]
    public void FindPeak_ReAnchorsWhenTheWindowHoldsOnlySubNoiseContent()
    {
        // Sub-noise window content must not block the re-anchor to a weak real event.
        var envelope = new double[48_000];
        Array.Fill(envelope, 0.01);
        envelope[19_999] = 0.06;
        envelope[20_000] = 0.1; // the only real event, beyond the window
        envelope[20_001] = 0.06;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                SearchWindowMilliseconds = 80
            });

        Assert.Equal(20_000, result.StrongestIndex);
        Assert.Equal(20_000, result.SelectedIndex);
        Assert.NotEqual(0, result.SearchRotation);
    }

    [Fact]
    public void FindPeak_KeepsTheStartAnchoredWindowWhenItHoldsReachableContent()
    {
        // In-window content within the search depth keeps the legacy window (a mode may out-ring the front).
        var envelope = new double[48_000];
        Array.Fill(envelope, 1e-6);
        envelope[499] = 0.12;
        envelope[500] = 0.2; // in-window, -14 dB re the far global peak
        envelope[501] = 0.12;
        envelope[19_999] = 0.6;
        envelope[20_000] = 1.0; // global maximum beyond the window
        envelope[20_001] = 0.6;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                SearchWindowMilliseconds = 80
            });

        Assert.Equal(500, result.StrongestIndex);
        Assert.Equal(500, result.SelectedIndex);
        Assert.Equal(0, result.SearchRotation);
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
        // Degenerate parabola: the flat-guard value, not a division by ~zero.
        Assert.Equal(0.0, SignalEnvelope.FindFractionalPeakOffset(1.0, 1.0, 1.0));
    }

    [Fact]
    public void FindFractionalPeakOffset_ReturnsTheParabolicVertex()
    {
        Assert.Equal(0.1, SignalEnvelope.FindFractionalPeakOffset(1.0, 4.0, 2.0), precision: 12);
    }

    [Fact]
    public void FindPeak_SnrGateRejectsASubNoiseEarlyBumpUnlessSnrIsRelaxed()
    {
        // The bump clears the -25 dB threshold, so only FirstPeakMinimumSnrDb decides it.
        var envelope = new double[2_000];
        Array.Fill(envelope, 0.01);
        envelope[100] = 0.08;
        envelope[500] = 1.0;

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
        // Equal-height mirror around the peak: pre-ringing, not an earlier arrival.
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
        // No mirror and a null between the two: separate events, the packet-rise floor has no say.
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
    public void FindPeak_RejectsARippleOnTheFootOfItsOwnWavePacket()
    {
        // A ripple 20 dB under its own packet on a foot dipping only 4 dB: too loud for pre-ringing, too quiet to be the front.
        var envelope = new double[4_096];
        Ramp(envelope, 480, 500, 0.0, 0.10);   // foot rising to the ripple
        Ramp(envelope, 500, 510, 0.10, 0.06);  // the ripple's own shallow dip
        Ramp(envelope, 510, 540, 0.06, 1.0);   // on into the packet's peak
        Ramp(envelope, 540, 600, 1.0, 0.0);

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0
            });

        Assert.Equal(540, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void FindPeak_KeepsADirectArrivalResolvedFromTheNextPacketByANull()
    {
        // A null between them resolves two arrivals: the earlier one keeps its timing.
        var envelope = new double[4_096];
        Ramp(envelope, 480, 500, 0.0, 0.10);
        Ramp(envelope, 500, 515, 0.10, 0.0);   // resolved: a null, not a dip
        Ramp(envelope, 520, 540, 0.0, 1.0);
        Ramp(envelope, 540, 600, 1.0, 0.0);

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0
            });

        Assert.Equal(500, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void FindPeak_KeepsADirectArrivalWhenOnlyALaterPacketsRisingEdgeIsInReach()
    {
        // A later reflection's rising edge inside the 1 ms look-ahead must not dwarf the direct arrival.
        var envelope = new double[4_096];
        Ramp(envelope, 480, 500, 0.0, 0.10);
        Ramp(envelope, 500, 515, 0.10, 0.0);
        Ramp(envelope, 520, 560, 0.0, 1.0);    // rising through the window, peaking past it
        Ramp(envelope, 560, 640, 1.0, 0.0);

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0
            });

        Assert.Equal(500, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    private static void Ramp(
        double[] envelope, int from, int to, double fromValue, double toValue)
    {
        for (int i = from; i <= to; i++)
        {
            double position = (double)(i - from) / (to - from);
            envelope[i] = fromValue + (toValue - fromValue) * position;
        }
    }

    [Fact]
    public void FindPeak_KeepsASoftArrivalWhenTheStrongPeakIsMillisecondsLater()
    {
        // A strong peak 4.2 ms later is a room mode: the packet floor is local.
        var envelope = new double[4_096];
        envelope[500] = 0.1;
        envelope[700] = 1.0;

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0
            });

        Assert.Equal(500, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void FindPeak_KeepsAFrontThatReachesAQuarterOfItsPacketPeak()
    {
        // The floor is a quarter of the packet: a connected 0.3 front stays selected.
        var envelope = new double[4_096];
        Ramp(envelope, 480, 500, 0.0, 0.3);
        Ramp(envelope, 500, 510, 0.3, 0.25);
        Ramp(envelope, 510, 540, 0.25, 1.0);
        Ramp(envelope, 540, 600, 1.0, 0.0);

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            sampleRate: 48_000,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = 25,
                FirstPeakMinimumSnrDb = 0
            });

        Assert.Equal(500, result.SelectedIndex);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_ReadsTheQuietFloorNotThePeak()
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
            peak: 1.0);

        // Rayleigh bias of the quartile floor compensated (+20·log10(0.370) ≈ −8.64 dB).
        Assert.InRange(confidence, 31.2, 31.5);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_ReverbTailDoesNotCountAsNoise()
    {
        // A −20 dB reverb tail must not count as noise: grade by the quietest quarter.
        double[] envelope = Enumerable.Repeat(0.001, 1000).ToArray();
        for (int i = 100; i < 600; i++)
        {
            envelope[i] = 0.1;
        }
        envelope[100] = 1.0;

        double confidence = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope,
            peak: 1.0);

        Assert.InRange(confidence, 51.2, 51.5);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_IgnoresTheDeconvolutionFftTail()
    {
        // A −140 dB FFT tail would put the quietest quarter off the real floor (a clean sweep once graded 123 dB).
        double[] envelope = Enumerable.Repeat(0.001, 1000).ToArray();
        for (int i = 400; i < 800; i++)
        {
            envelope[i] = 1e-7;
        }
        envelope[100] = 1.0;

        double confidence = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope,
            peak: 1.0);

        Assert.InRange(confidence, 51.2, 51.5);
    }

    // The caller's spectrum must survive the call: the analysis reads it again for the correlation.
    [Fact]
    public void EnvelopeFromSpectrum_MatchesTheEnvelopeOfTheSignalItCameFrom()
    {
        const int length = 512;
        double[] signal = CreateSine(length, 11, 0.8);
        for (int i = 0; i < length; i++)
        {
            signal[i] += 0.3 * Math.Cos(2.0 * Math.PI * 37 * i / length);
        }

        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        Complex[] untouched = (Complex[])spectrum.Clone();

        double[] expected = SignalEnvelope.Envelope(signal);
        double[] actual = SignalEnvelope.EnvelopeFromSpectrum(spectrum);

        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], precision: 12);
        }

        Assert.Equal(untouched, spectrum);
    }

    // An odd length takes the other half of the analytic mask.
    [Fact]
    public void EnvelopeFromSpectrum_MatchesForAnOddLength()
    {
        const int length = 255;
        double[] signal = CreateSine(length, 9, 1.1);

        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        double[] expected = SignalEnvelope.Envelope(signal);
        double[] actual = SignalEnvelope.EnvelopeFromSpectrum(spectrum);

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], precision: 12);
        }
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
