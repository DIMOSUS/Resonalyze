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
        // A chain latency parks the whole IR beyond the search window: the
        // start-anchored window holds only residue far below the first-arrival
        // search depth, so the search re-anchors on the global envelope
        // maximum and reports it in the envelope's own coordinates.
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
        // The 3RC head shape: the acausal residue wrapped across the buffer
        // seam decays from sample 0, sitting tens of dB above the noise
        // floor yet more than the search depth below the real peak — loud
        // residue is still residue, and the window re-anchors past it.
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
        // A near-noise record with a weak but real event beyond the window:
        // the window's noise sits within the 25 dB search depth of that weak
        // global peak, but below the noise gate — depth alone would keep the
        // start-anchored window, whose content then fails the first-arrival
        // threshold and the fallback returns a noise sample. Sub-noise
        // content must not block the re-anchor.
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
        // The re-anchor gate is conservative: content inside the start-anchored
        // window within the first-arrival search depth of the global peak means
        // the true front may live there (the modal-cabin geometry, where a room
        // mode out-rings the direct front), so the legacy window must stay.
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
        // It is also 14 dB under a stronger peak 0.125 ms later: the envelope
        // nulls to zero between them, which resolves the two as separate events,
        // so the packet-rise floor has no say here.
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
        // A ripple 20 dB under the packet it belongs to, 0.83 ms ahead of that
        // packet's peak, riding a foot that only dips 4 dB behind it — the comb
        // structure a cabin leaves on a leading edge. It is too loud to be the
        // transform's own pre-ringing (the symmetry rule keeps it) and too quiet,
        // with nothing resolving it from the rise it sits on, to be the front.
        // Reading it as the arrival is what made two identical drivers
        // incomparable: one measured at its packet peak, the other 20 dB down
        // its own foot.
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
        // The same 20 dB gap inside the same millisecond, but the envelope nulls
        // to nothing between the two: destructive interference resolves them, so
        // these are two arrivals and the earlier one is the direct sound. Its
        // timing must survive — the packet ends at the null, and what rises
        // after it is somebody else's packet.
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
        // A reflection that PEAKS 1.25 ms after the direct sound — a separate
        // arrival by every rule here — but whose rising edge is already inside
        // the one-millisecond packet window, and by its end stands seven times
        // the direct arrival. The look-ahead must not borrow that edge to dwarf
        // the arrival in front of it.
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

    // Writes a linear segment into the envelope, endpoints included, so a test
    // can shape a real leading edge instead of isolated spikes: whether two
    // bumps are one packet or two arrivals is a question about what lies
    // BETWEEN them.
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
        // The same 20 dB gap, but the strong peak sits 4.2 ms later — a room
        // mode, not this arrival's own packet. The soft direct sound the 25 dB
        // search depth exists to find must survive: the packet floor is local.
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
        // The floor is a quarter of the packet's amplitude: a front at 0.3 of a
        // 1.0 packet — connected to it, no null between them — is that packet's
        // own leading edge and stays selected, so the guard cannot quietly
        // promote every arrival to its strongest lobe.
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

        // peak/floor is 40 dB; the reported figure compensates the Rayleigh
        // bias of the quartile floor (+20·log10(0.370) ≈ −8.64 dB), so the
        // metric reads peak vs the FULL envelope noise RMS, not vs the
        // flattering quartile.
        Assert.InRange(confidence, 31.2, 31.5);
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

        // 60 dB against the raw floor, minus the Rayleigh-bias compensation
        // (≈ 8.64 dB) — and nowhere near the ~20 dB the reverb-tail mean gave.
        Assert.InRange(confidence, 51.2, 51.5);
    }

    [Fact]
    public void EstimatePeakConfidenceDecibels_IgnoresTheDeconvolutionFftTail()
    {
        // A transfer IR's long FFT tail sits 100+ dB below the peak — far under
        // any real floor. Here 40% of the record is such a −140 dB tail over a
        // −60 dB real noise floor. The quietest quarter would otherwise land in
        // the tail and read ~131 dB; the valid-region bound must grade the
        // recording by its real −60 dB floor instead (the bug that reported a
        // clean cabin sweep as 123 dB while its envelope showed ~65).
        double[] envelope = Enumerable.Repeat(0.001, 1000).ToArray();
        for (int i = 400; i < 800; i++)
        {
            envelope[i] = 1e-7;
        }
        envelope[100] = 1.0;

        double confidence = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope,
            peak: 1.0);

        // The −60 dB floor minus the Rayleigh compensation (≈ 8.64 dB), the same
        // as the reverb-tail case — NOT the ~131 dB the raw quartile would read.
        Assert.InRange(confidence, 51.2, 51.5);
    }

    // The premise of the band-limited read's single transform: a caller that
    // already holds the forward spectrum gets exactly the envelope it would have
    // got by transforming back and asking for it, and its spectrum survives the
    // call (the analysis reads that same array again for the correlation).
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

    // An odd length takes the other half of the analytic mask, so it is pinned
    // on both sides of that branch.
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
