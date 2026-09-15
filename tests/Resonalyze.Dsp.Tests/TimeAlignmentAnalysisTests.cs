namespace Resonalyze.Dsp.Tests;

public sealed class TimeAlignmentAnalysisTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Analyze_FlagsALaterStrongestPeakAsASeparateArrival()
    {
        // Narrowband-sub trap: the strongest peak is a room mode, not the direct arrival.
        var impulseResponse = new double[8_192];
        impulseResponse[100] = 0.3;
        impulseResponse[500] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.True(result.StrongestPeakIsSeparateArrival);
        Assert.InRange(result.StrongestPeakSeparationMilliseconds, 8.0, 8.7);
    }

    // Non-power-of-two length: transforms run circular at that length, the whitened correlation pads; both must agree.
    [Fact]
    public void Analyze_PlacesTheSameArrivalWhenTheLengthIsNotAPowerOfTwo()
    {
        var options = new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = true,
            BandpassCenterHz = 1_000,
            BandpassPassOctaves = 2,
            BandpassFadeOctaves = 0.5,
            WrapPeakPositions = true
        };
        var padded = new double[8_192];
        padded[300] = 1.0;
        var odd = new double[8_193];
        odd[300] = 1.0;

        TimeAlignmentAnalysisResult power = TimeAlignmentAnalysis.Analyze(
            padded, SampleRate, options);
        TimeAlignmentAnalysisResult notPower = TimeAlignmentAnalysis.Analyze(
            odd, SampleRate, options);

        Assert.True(power.IsValid);
        Assert.True(notPower.IsValid);
        // Not bit for bit: different bin grids, ~1/400 of a sample.
        Assert.Equal(
            power.FirstArrivalDelayMilliseconds,
            notPower.FirstArrivalDelayMilliseconds,
            precision: 3);
    }

    [Fact]
    public void Analyze_DoesNotFlagACleanSingleArrival()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.False(result.StrongestPeakIsSeparateArrival);
        Assert.True(result.StrongestPeakSeparationMilliseconds < 1.0);
    }

    [Fact]
    public void Analyze_ReportsTheStrongestArrivalSampleAndDelayForACleanImpulse()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.Equal(300, result.StrongestEnvelopePeakIndex);
        Assert.InRange(result.StrongestPeakSample, 299.5, 300.5);
        Assert.InRange(result.StrongestDelayMilliseconds, 6.24, 6.26);
        Assert.Equal(
            result.StrongestPeakSample * 1000.0 / SampleRate,
            result.StrongestDelayMilliseconds,
            precision: 9);
        Assert.True(result.FirstArrivalPeakSample <= result.StrongestPeakSample + 0.5);
    }

    [Fact]
    public void Analyze_LocatesTheStrongArrivalAndAnEarlierFirstArrivalInTheTwoPeakTrap()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[100] = 0.3; // weak direct arrival
        impulseResponse[500] = 1.0; // strong late arrival (room mode)

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.Equal(500, result.StrongestEnvelopePeakIndex);
        Assert.InRange(result.StrongestPeakSample, 499.0, 501.0);
        Assert.InRange(result.StrongestDelayMilliseconds, 10.38, 10.44);
        Assert.InRange(result.FirstArrivalPeakSample, 90.0, 110.0);
        Assert.True(result.StrongestPeakSample - result.FirstArrivalPeakSample > 300.0);
    }

    [Fact]
    public void Analyze_FindsAnArrivalParkedBeyondTheSearchWindowByChainLatency()
    {
        // 3RC field case: ~160 ms chain buffering, beyond the 80 ms start-anchored search.
        var impulseResponse = new double[131_072];
        impulseResponse[7_680] = 1.0; // 160 ms at 48 kHz

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate,
            new TimeAlignmentAnalysisOptions { WrapPeakPositions = true });

        Assert.Equal(7_680, result.StrongestEnvelopePeakIndex);
        Assert.InRange(result.StrongestDelayMilliseconds, 159.9, 160.1);
        Assert.InRange(result.FirstArrivalDelayMilliseconds, 159.9, 160.1);
        Assert.False(result.StrongestPeakIsSeparateArrival);
    }

    [Fact]
    public void Analyze_ReportsALeadingArrivalBeyondTheWindowAsANegativeDelay()
    {
        // Energy leading the reference wraps to the buffer end; the wrap maps it to a negative delay.
        var impulseResponse = new double[131_072];
        impulseResponse[131_072 - 480] = 1.0; // -10 ms at 48 kHz

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate,
            new TimeAlignmentAnalysisOptions { WrapPeakPositions = true });

        Assert.InRange(result.StrongestDelayMilliseconds, -10.1, -9.9);
        Assert.InRange(result.FirstArrivalDelayMilliseconds, -10.1, -9.9);
    }

    [Fact]
    public void Analyze_FindsADirectArrivalAFullWindowAheadOfTheStrongestPeak()
    {
        // Window anchored at its far edge, not centred on the mode: centring would leave only 40 ms of pre-history.
        var impulseResponse = new double[131_072];
        impulseResponse[7_680] = 0.3;  // direct arrival, 160 ms at 48 kHz
        impulseResponse[10_320] = 1.0; // stronger room mode, 215 ms

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.InRange(result.FirstArrivalDelayMilliseconds, 159.5, 160.5);
        Assert.InRange(result.StrongestDelayMilliseconds, 214.5, 215.5);
        Assert.True(result.StrongestPeakIsSeparateArrival);
        Assert.InRange(result.StrongestPeakSeparationMilliseconds, 54.5, 55.5);
    }

    [Fact]
    public void Analyze_KeepsTheTwoPeakTrapGeometryUnderChainLatency()
    {
        // The classic trap shifted by 160 ms latency must read identically in the re-anchored frame.
        var impulseResponse = new double[131_072];
        impulseResponse[7_680] = 0.3; // weak direct arrival
        impulseResponse[8_080] = 1.0; // strong late arrival (room mode)

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.Equal(8_080, result.StrongestEnvelopePeakIndex);
        Assert.InRange(result.FirstArrivalPeakSample, 7_670.0, 7_690.0);
        Assert.True(result.StrongestPeakIsSeparateArrival);
        Assert.InRange(result.StrongestPeakSeparationMilliseconds, 8.0, 8.7);
    }

    [Fact]
    public void Analyze_WrapPeakPositionsLeavesASubHalfArrivalUnchanged()
    {
        // Pins ToSignedDelaySamples' pivot direction: a flipped comparison would wrap a normal arrival negative.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult unwrapped = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());
        TimeAlignmentAnalysisResult wrapped = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions { WrapPeakPositions = true });

        Assert.Equal(unwrapped.FirstArrivalPeakSample, wrapped.FirstArrivalPeakSample, precision: 12);
        Assert.Equal(unwrapped.StrongestPeakSample, wrapped.StrongestPeakSample, precision: 12);
        Assert.True(wrapped.FirstArrivalPeakSample > 0);
    }

    [Fact]
    public void Analyze_DoesNotFlagACloseSecondPeakBelowTheThreshold()
    {
        // ~0.4 ms apart: too close to matter, so no warning.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 0.6;
        impulseResponse[320] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.False(result.StrongestPeakIsSeparateArrival);
    }

    [Fact]
    public void Analyze_ReportsHighConfidenceAndPhatRefinementForTheStrongestArrival()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.InRange(result.FirstArrivalConfidence, 0.0, 1.0);
        Assert.InRange(result.StrongestConfidence, 0.0, 1.0);
        Assert.True(
            result.StrongestConfidence > 0.2,
            $"Strongest confidence {result.StrongestConfidence:0.000} should clear the trust gate.");
        Assert.True(result.StrongestRefinedByPhat);
    }

    [Fact]
    public void Analyze_CleanImpulseHasZeroProminenceGapAndHighSnr()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.Equal(0.0, result.FirstArrivalProminenceDecibels, precision: 12);
        Assert.True(
            result.SignalToNoiseDecibels > 30,
            $"SNR {result.SignalToNoiseDecibels:0.0} dB should be high for a clean impulse.");
    }

    [Fact]
    public void Analyze_FirstArrivalDoesNotSitOnTheHilbertSkirtOfACleanImpulse()
    {
        // A delta's Hilbert envelope has a 1/t skirt whose bumps sit ~11 samples early; sidelobe rejection must skip them.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.InRange(result.FirstArrivalPeakSample, 299.5, 300.5);
        Assert.Equal(result.StrongestEnvelopePeakIndex, result.EnvelopePeakIndex);
    }

    [Fact]
    public void Analyze_BandpassPreRingingDoesNotPullTheFirstArrivalEarly()
    {
        // The zero-phase bandpass pre-lobe (-24 dB, ~2.1 ms early) clears the -25 dB threshold: reject the pre-ring train.
        var impulseResponse = new double[32_768];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = 1000,
                BandpassPassOctaves = 1,
                BandpassFadeOctaves = 0.5
            });

        Assert.InRange(result.FirstArrivalPeakSample, 299.5, 300.5);
        Assert.True(result.FirstArrivalRefinedByPhat);
    }

    [Fact]
    public void Analyze_ReverberantBassKeepsTheGenuineDirectArrival()
    {
        // Direct sound ~9 dB under a reflection cluster whose mirror position is energized:
        // the kernel-level ceiling keeps it (at 7.6 ms the window cannot ring at -9 dB).
        var impulseResponse = new double[65_536];
        void Add(double ms, double amplitude) =>
            impulseResponse[(int)Math.Round(ms * SampleRate / 1000.0)] += amplitude;
        Add(11.466, 0.35); // direct sound
        Add(14.8, 0.25);
        Add(16.5, 0.4);
        Add(17.9, 0.55);
        Add(19.41, 1.0);   // strongest reflection
        Add(20.8, 0.7);
        Add(22.6, 0.55);
        Add(24.9, 0.45);
        Add(27.5, 0.35);   // keeps the direct sound's mirror position hot
        Add(30.4, 0.3);
        Add(33.8, 0.22);
        Add(38.0, 0.15);

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(88.0 * 350.0),
                BandpassPassOctaves = Math.Log2(350.0 / 88.0),
                BandpassFadeOctaves = 1.0,
                FirstPeakThresholdBelowMaxDb = 15
            });

        double firstArrivalMs = result.FirstArrivalPeakSample * 1000.0 / SampleRate;
        Assert.InRange(firstArrivalMs, 11.0, 12.5);
    }

    [Fact]
    public void Analyze_AGenuineWeakEarlyArrivalSurvivesSidelobeRejection()
    {
        // The early arrival has no mirror counterpart, so it is kept while pre-rings are rejected.
        var impulseResponse = new double[32_768];
        impulseResponse[300] = 0.316;
        impulseResponse[540] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = 1000,
                BandpassPassOctaves = 1,
                BandpassFadeOctaves = 0.5
            });

        Assert.InRange(result.FirstArrivalPeakSample, 297.0, 302.0);
        Assert.InRange(result.StrongestPeakSample, 539.0, 541.0);
    }

    [Fact]
    public void Analyze_ZeroSignalReportsAnInvalidResult()
    {
        // Silent IR: report no signal, not a delay near the window end.
        var impulseResponse = new double[8_192];

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.False(result.IsValid);
        Assert.Equal(0.0, result.FirstArrivalDelayMilliseconds);
        Assert.Equal(0.0, result.StrongestDelayMilliseconds);
        Assert.Equal(0.0, result.FirstArrivalConfidence);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Analyze_TinyImpulseResponsesDoNotThrow(int length)
    {
        // The search-end floor of 3 (parabolic refinement) once ran past a 1-2 sample envelope.
        var impulseResponse = new double[length];
        impulseResponse[0] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Analyze_ABroadLowFrequencyRiseIsNotASeparateArrival()
    {
        // 1.5 ms apart under a 200 Hz octave band: one packet (valley −0.2 dB), so no separate-arrival flag.
        var impulseResponse = new double[65_536];
        impulseResponse[4_000] = 0.9;
        impulseResponse[4_072] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = 200,
                BandpassPassOctaves = 1,
                BandpassFadeOctaves = 0.5,
                FirstPeakThresholdBelowMaxDb = 15
            });

        Assert.True(result.StrongestPeakSeparationMilliseconds > 1.0);
        Assert.False(result.StrongestPeakIsSeparateArrival);
    }

    [Fact]
    public void Analyze_TwoArrivalsWithARealValleyStayFlagged()
    {
        // 2.5 ms apart the envelope dips ~22 dB: a genuine second arrival.
        var impulseResponse = new double[65_536];
        impulseResponse[4_000] = 0.75;
        impulseResponse[4_120] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = 200,
                BandpassPassOctaves = 1,
                BandpassFadeOctaves = 0.5,
                FirstPeakThresholdBelowMaxDb = 15
            });

        Assert.True(result.StrongestPeakIsSeparateArrival);
    }

    private static void AddToneBurst(
        double[] impulseResponse,
        double startMs,
        double frequencyHz,
        int periods,
        double amplitude)
    {
        int start = (int)Math.Round(startMs * SampleRate / 1000.0);
        int length = (int)Math.Round(periods * SampleRate / frequencyHz);
        for (int i = 0; i < length && start + i < impulseResponse.Length; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(Math.Tau * i / length);
            impulseResponse[start + i] +=
                amplitude * hann * Math.Sin(Math.Tau * frequencyHz * i / SampleRate);
        }
    }

    private static TimeAlignmentAnalysisOptions OneOctaveAroundOneKilohertz => new()
    {
        UseBandpassWindow = true,
        BandpassCenterHz = 1000,
        BandpassPassOctaves = 1,
        BandpassFadeOctaves = 0.5
    };

    [Fact]
    public void ProbeArrivalHonesty_WithoutABandpassWindow_ReturnsNull()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;
        var options = new TimeAlignmentAnalysisOptions();

        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, options);

        Assert.Null(TimeAlignmentAnalysis.ProbeArrivalHonesty(
            impulseResponse, SampleRate, options, full));
    }

    [Fact]
    public void ProbeArrivalHonesty_PassBandTooNarrowForAnUpperHalf_ReturnsNull()
    {
        // MinimumArrivalBandRatio is 1/3 octave, so under 2/3 octave of pass band cannot be probed.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;
        var options = new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = true,
            BandpassCenterHz = 1000,
            BandpassPassOctaves = 0.5,
            BandpassFadeOctaves = 0.5
        };

        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, options);

        Assert.Null(TimeAlignmentAnalysis.ProbeArrivalHonesty(
            impulseResponse, SampleRate, options, full));
    }

    [Fact]
    public void ProbeArrivalHonesty_CleanImpulse_VerifiesTheArrival()
    {
        var impulseResponse = new double[32_768];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, OneOctaveAroundOneKilohertz);
        TimeAlignmentArrivalProbe? probe = TimeAlignmentAnalysis.ProbeArrivalHonesty(
            impulseResponse, SampleRate, OneOctaveAroundOneKilohertz, full);

        Assert.NotNull(probe);
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Verified,
            probe.Value.Certificate);
        Assert.Equal(1000.0, probe.Value.ProbeLowHz, precision: 6);
        Assert.InRange(probe.Value.ProbeHighHz, 1414.0, 1414.5);
        Assert.InRange(
            Math.Abs(
                probe.Value.ProbeResult.FirstArrivalDelayMilliseconds -
                full.FirstArrivalDelayMilliseconds),
            0.0,
            probe.Value.ToleranceMs);
    }

    [Fact]
    public void ProbeArrivalHonesty_LateFullBandArrival_FlagsTheModalLatch()
    {
        // Weak early front in the upper half (-30 dB, under the -25 dB threshold) and a loud late build-up at the lower edge.
        var impulseResponse = new double[32_768];
        AddToneBurst(impulseResponse, startMs: 5.0, frequencyHz: 1200, periods: 5, amplitude: 0.0316);
        AddToneBurst(impulseResponse, startMs: 12.0, frequencyHz: 800, periods: 20, amplitude: 1.0);

        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, OneOctaveAroundOneKilohertz);
        TimeAlignmentArrivalProbe? probe = TimeAlignmentAnalysis.ProbeArrivalHonesty(
            impulseResponse, SampleRate, OneOctaveAroundOneKilohertz, full);

        Assert.NotNull(probe);
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Latched,
            probe.Value.Certificate);
        Assert.True(
            full.FirstArrivalDelayMilliseconds -
            probe.Value.ProbeResult.FirstArrivalDelayMilliseconds >
            probe.Value.ToleranceMs,
            $"full {full.FirstArrivalDelayMilliseconds:0.000} ms should read far " +
            $"later than the probe {probe.Value.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms");
    }

    [Fact]
    public void ProbeArrivalHonesty_NoiseFloorUpperHalf_ReturnsUnverified()
    {
        // Signal below the probe band over a -50 dB floor: usable, no certificate (digital silence would leak edge transients).
        var impulseResponse = new double[32_768];
        var random = new Random(7);
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] = 0.003 * (random.NextDouble() * 2.0 - 1.0);
        }
        AddToneBurst(impulseResponse, startMs: 10.0, frequencyHz: 600, periods: 60, amplitude: 1.0);

        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, OneOctaveAroundOneKilohertz);
        TimeAlignmentArrivalProbe? probe = TimeAlignmentAnalysis.ProbeArrivalHonesty(
            impulseResponse, SampleRate, OneOctaveAroundOneKilohertz, full);

        Assert.True(full.IsValid);
        Assert.True(
            full.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb,
            $"full SNR {full.SignalToNoiseDecibels:0.0} dB should clear the floor");
        Assert.NotNull(probe);
        Assert.True(
            probe.Value.ProbeResult.SignalToNoiseDecibels <
                AutoAlignmentEngine.MinimumArrivalSnrDb,
            $"probe SNR {probe.Value.ProbeResult.SignalToNoiseDecibels:0.0} dB " +
            "should sit below the floor");
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Unverified,
            probe.Value.Certificate);
    }

    [Fact]
    public void Analyze_FlatUnityCoherence_ReproducesTheNullResultExactly()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;
        // fftLength = NextPowerOfTwo(8192) = 8192 -> half spectrum length 4097.
        double[] ones = Enumerable.Repeat(1.0, 8_192 / 2 + 1).ToArray();

        TimeAlignmentAnalysisResult baseline = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());
        TimeAlignmentAnalysisResult weighted = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions(), ones);

        Assert.Equal(baseline.FirstArrivalPeakSample, weighted.FirstArrivalPeakSample);
        Assert.Equal(baseline.StrongestPeakSample, weighted.StrongestPeakSample);
        Assert.Equal(baseline.FirstArrivalConfidence, weighted.FirstArrivalConfidence);
        Assert.Equal(baseline.StrongestConfidence, weighted.StrongestConfidence);
    }

    [Fact]
    public void Analyze_ReadsAPairOfIdenticalDriversAtTheSamePointOfTheirFronts()
    {
        // Band-limiting two door wavefronts with asymmetric reflections leaves a leading-edge bump 0.27 ms early (1.771 ms for 1.5).
        var near = new double[8_192];
        near[500] = 0.6;
        near[530] = 1.0;
        var far = new double[8_192];
        far[572] = 0.6;
        far[596] = 1.0;
        var options = new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = true,
            BandpassCenterHz = 500,
            BandpassPassOctaves = 7.9,
            BandpassFadeOctaves = 0.5
        };

        TimeAlignmentAnalysisResult nearResult = TimeAlignmentAnalysis.Analyze(
            near, SampleRate, options);
        TimeAlignmentAnalysisResult farResult = TimeAlignmentAnalysis.Analyze(
            far, SampleRate, options);

        double splitMs =
            farResult.FirstArrivalDelayMilliseconds -
            nearResult.FirstArrivalDelayMilliseconds;
        Assert.InRange(splitMs, 1.45, 1.55);
        Assert.InRange(
            Math.Abs(
                nearResult.FirstArrivalProminenceDecibels -
                farResult.FirstArrivalProminenceDecibels),
            0.0,
            1.0);
    }
}
