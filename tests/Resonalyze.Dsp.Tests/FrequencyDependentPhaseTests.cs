using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class FrequencyDependentPhaseTests
{
    private const int SampleRate = 48_000;

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void PureDelay_AutoDetrendIsFlat(int cycles)
    {
        SyntheticMeasurement measurement = DelayedImpulse(960);
        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent,
            cycles,
            PhaseDetrendMode.Auto,
            gateOffsetMs: 20.0);

        double resolved = DataHelper.ResolvePhaseDetrendMilliseconds(measurement, settings);
        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(measurement, settings);

        Assert.Equal(20.0, resolved, tolerance: 0.06);
        Assert.All(
            phase.Where(point => point.X is >= 100 and <= 15_000),
            point => Assert.True(Math.Abs(point.Y) < 1e-5,
                $"Residual {point.Y:e} rad at {point.X:0.#} Hz."));
    }

    [Fact]
    public void Fdw_InvalidCyclesFallsBackToSix()
    {
        SyntheticMeasurement measurement = ReflectedImpulse();
        List<SignalPoint> invalid = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.FrequencyDependent, 123, PhaseDetrendMode.Manual));
        List<SignalPoint> six = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Manual));

        Assert.Equal(six, invalid);
    }

    [Fact]
    public void Fdw_WhenEveryWindowIsClamped_MatchesFixed()
    {
        SyntheticMeasurement measurement = DelayedImpulse(480);
        PhaseAnalysisSettings fixedSettings = Settings(
            PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual) with
        {
            LeftMs = 0.5,
            PlateauMs = 0.4,
            RightMs = 0.1
        };
        PhaseAnalysisSettings fdwSettings = fixedSettings with
        {
            WindowMode = PhaseWindowMode.FrequencyDependent
        };

        List<SignalPoint> fixedPhase = DataHelper.GetGatedPhaseData(measurement, fixedSettings);
        List<SignalPoint> fdwPhase = DataHelper.GetGatedPhaseData(measurement, fdwSettings);

        Assert.Equal(fixedPhase.Count, fdwPhase.Count);
        foreach ((SignalPoint expected, SignalPoint actual) in fixedPhase.Zip(fdwPhase))
        {
            Assert.Equal(expected.X, actual.X, precision: 12);
            double error = Math.IEEERemainder(actual.Y - expected.Y, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-10);
        }
    }

    [Fact]
    public void Fdw_PartialClampKeepsFixedSpectrumBelowTransition()
    {
        var impulse = new Complex[4_096];
        impulse[480] = Complex.One;
        impulse[576] = new Complex(0.6, 0.0); // 2 ms late reflection
        var measurement = new SyntheticMeasurement(impulse, SampleRate, 480);
        PhaseAnalysisSettings fixedSettings = Settings(
            PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual) with
        {
            LeftMs = 1.0,
            PlateauMs = 1.0,
            RightMs = 4.0
        };
        PhaseAnalysisSettings fdwSettings = fixedSettings with
        {
            WindowMode = PhaseWindowMode.FrequencyDependent
        };

        List<SignalPoint> fixedPhase = DataHelper.GetGatedPhaseData(measurement, fixedSettings);
        List<SignalPoint> fdwPhase = DataHelper.GetGatedPhaseData(measurement, fdwSettings);

        foreach ((SignalPoint expected, SignalPoint actual) in fixedPhase.Zip(fdwPhase)
                     .Where(pair => pair.First.X is >= 100 and <= 700))
        {
            double error = Math.IEEERemainder(actual.Y - expected.Y, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-10,
                $"FDW changed the clamped spectrum by {error:e} rad at {expected.X:0.#} Hz.");
        }

        double highFrequencyDifference = fixedPhase.Zip(fdwPhase)
            .Where(pair => pair.First.X is >= 4_000 and <= 10_000)
            .Average(pair => Math.Abs(Math.IEEERemainder(
                pair.Second.Y - pair.First.Y,
                Math.Tau)));
        Assert.True(highFrequencyDifference > 0.01,
            $"FDW did not shorten above the transition ({highFrequencyDifference:e} rad).");
    }

    [Fact]
    public void CommonDetrendPreservesRelativePhase()
    {
        SyntheticMeasurement first = DelayedImpulse(480);
        SyntheticMeasurement second = DelayedImpulse(504);
        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Manual,
            manualMs: 10.0);
        List<SignalPoint> firstPhase = DataHelper.GetGatedPhaseData(first, settings);
        List<SignalPoint> secondPhase = DataHelper.GetGatedPhaseData(second, settings);

        foreach ((SignalPoint a, SignalPoint b) in firstPhase.Zip(secondPhase)
                     .Where(pair => pair.First.X is >= 200 and <= 10_000))
        {
            double expected = -Math.Tau * a.X * 24 / SampleRate;
            double actual = Math.IEEERemainder(b.Y - a.Y, Math.Tau);
            double error = Math.IEEERemainder(actual - expected, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-5,
                $"Relative-phase error {error:e} at {a.X:0.#} Hz.");
        }
    }

    [Fact]
    public void DifferentGatePositions_ThatBothContainTheWholeResponse_AgreeOnRelativePhase()
    {
        // The re-reference contract, stated on the case where it holds: two
        // window POSITIONS whose plateaus both contain the whole (here
        // one-sample) response. BuildMeasuredPhase moves each extraction to the
        // absolute τ, so the placement cancels and only the content counts.
        // This is what lets the Virtual DSP phase view gate each curve on its
        // own arrival — but ONLY while the condition holds, which is why the
        // placement is measured first (GateLeadingEdgeLossDb). The two tests
        // below are its other half: what a window that cuts into its channel
        // does, and that the guard sees the difference.
        SyntheticMeasurement first = DelayedImpulse(480); // 10 ms
        SyntheticMeasurement second = DelayedImpulse(576); // 12 ms
        PhaseAnalysisSettings sharedReference = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Manual,
            manualMs: 10.0,
            gateOffsetMs: 10.0);
        PhaseAnalysisSettings ownWindow = sharedReference with
        {
            GateOffsetMs = 12.0
        };

        List<SignalPoint> firstPhase = DataHelper.GetGatedPhaseData(
            first, sharedReference);
        List<SignalPoint> secondPhase = DataHelper.GetGatedPhaseData(
            second, ownWindow);

        foreach ((SignalPoint a, SignalPoint b) in firstPhase.Zip(secondPhase)
                     .Where(pair => pair.First.X is >= 200 and <= 10_000))
        {
            double expected = -Math.Tau * a.X * 96 / SampleRate;
            double actual = Math.IEEERemainder(b.Y - a.Y, Math.Tau);
            double error = Math.IEEERemainder(actual - expected, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-5,
                $"Relative-phase error {error:e} at {a.X:0.#} Hz with per-curve gates.");
        }
    }

    [Fact]
    public void AGatePlacedOnTheArrivalPeak_TruncatesALowPassedChannelAndMovesItsPhase()
    {
        // Why the Virtual DSP phase view cannot gate each curve at its own
        // arrival PEAK. A steeply low-passed channel peaks long after it
        // starts, so a window whose plateau begins at the peak keeps only a
        // short shoulder of the rise; the response the FFT sees is no longer
        // the channel's, and the common τ cannot restore it. The same IR read
        // through a window that contains it and through one placed on its peak
        // must therefore DISAGREE: this shape reads 177° apart, the field pair
        // 176°.
        SyntheticMeasurement channel = LowPassedArrival(startSample: 480);
        // The field session's gate: long enough that, placed on the arrival, it
        // holds the whole channel — so only the PLACEMENT differs below.
        PhaseAnalysisSettings containing = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Manual,
            manualMs: 10.0,
            gateOffsetMs: 10.0) with
        {
            LeftMs = 5.0,
            PlateauMs = 50.0,
            RightMs = 20.0
        };
        PhaseAnalysisSettings onThePeak = containing with
        {
            GateOffsetMs = FindPeakMs(channel)
        };

        List<SignalPoint> whole = DataHelper.GetGatedPhaseData(channel, containing);
        List<SignalPoint> truncated = DataHelper.GetGatedPhaseData(channel, onThePeak);

        double worstDegrees = 0;
        foreach ((SignalPoint a, SignalPoint b) in whole.Zip(truncated)
                     .Where(pair => pair.First.X is >= 40 and <= 120))
        {
            worstDegrees = Math.Max(
                worstDegrees,
                Math.Abs(Math.IEEERemainder(b.Y - a.Y, Math.Tau)) / Math.PI * 180.0);
        }

        Assert.True(
            worstDegrees > 45.0,
            $"the peak-placed window changed the read by only {worstDegrees:0.0}°; " +
            "if placement no longer matters here, the guard can be revisited");
    }

    [Fact]
    public void GateLeadingEdgeLoss_SeparatesAContainingPlacementFromATruncatingOne()
    {
        // The guard the per-curve placement rests on. It must read the same
        // channel as safe when the window opens before its response and unsafe
        // when the plateau starts on its peak — the two placements the test
        // above showed 177° apart. Field figures at the same gate: own-arrival
        // placements -28.4 to -72.2 dB, peak placements -3.5 to -10.8 dB.
        SyntheticMeasurement channel = LowPassedArrival(startSample: 480);

        double containing = DataHelper.GateLeadingEdgeLossDb(
            channel, gateOffsetMs: 10.0, leftMs: 5.0, plateauMs: 50.0, rightMs: 20.0);
        double onThePeak = DataHelper.GateLeadingEdgeLossDb(
            channel, FindPeakMs(channel), leftMs: 5.0, plateauMs: 50.0, rightMs: 20.0);

        Assert.True(
            containing < -20.0,
            $"a window opening on the arrival lost {containing:0.0} dB ahead of its plateau");
        Assert.True(
            onThePeak > -20.0,
            $"a window opening on the peak lost only {onThePeak:0.0} dB ahead of its plateau");
        // And the two verdicts must not sit next to each other: the whole point
        // is a gap wide enough to put a ceiling in.
        Assert.True(
            onThePeak - containing > 15.0,
            $"the guard separated the placements by only {onThePeak - containing:0.0} dB");
    }

    [Fact]
    public void AGuardedPerCurvePlacement_ReadsTheSamePhaseAsAContainingSharedOne()
    {
        // What the guard buys: once a placement passes it, moving the window
        // from a shared position to the channel's own arrival must not move the
        // curve where the channel actually plays. That is the invariant the
        // Virtual DSP phase view relies on to give each channel its own FDW
        // window without making the curves incomparable.
        SyntheticMeasurement channel = LowPassedArrival(startSample: 480);
        PhaseAnalysisSettings shared = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Manual,
            manualMs: 10.0,
            gateOffsetMs: 8.0) with
        {
            LeftMs = 5.0,
            PlateauMs = 50.0,
            RightMs = 20.0
        };
        PhaseAnalysisSettings ownArrival = shared with { GateOffsetMs = 10.0 };
        Assert.True(
            DataHelper.GateLeadingEdgeLossDb(channel, 8.0, 5.0, 50.0, 20.0) < -20.0);
        Assert.True(
            DataHelper.GateLeadingEdgeLossDb(channel, 10.0, 5.0, 50.0, 20.0) < -20.0);

        List<SignalPoint> sharedPhase = DataHelper.GetGatedPhaseData(channel, shared);
        List<SignalPoint> ownPhase = DataHelper.GetGatedPhaseData(channel, ownArrival);

        // Judged where this channel plays — a 55 Hz ring decaying over 25 ms
        // carries its energy within roughly ±15 Hz of that, and a phase
        // difference read where there is no output is noise, not a defect.
        // (Field corroboration on the real channels, each inside its own
        // passband: 0.2-1.5° between the shared and the own-arrival placement.)
        foreach ((SignalPoint a, SignalPoint b) in sharedPhase.Zip(ownPhase)
                     .Where(pair => pair.First.X is >= 45 and <= 70))
        {
            double degrees =
                Math.Abs(Math.IEEERemainder(b.Y - a.Y, Math.Tau)) / Math.PI * 180.0;
            Assert.True(
                degrees < 5.0,
                $"a guarded placement moved the read {degrees:0.0}° at {a.X:0.#} Hz");
        }
    }

    // A band-limited arrival that keeps rising for some 15 ms after it starts —
    // the shape a steep low-pass gives a subwoofer channel, where the peak the
    // Auto gate used to anchor on sits nowhere near the arrival. Field figures
    // for scale: start 15.6 ms, peak 36.7 ms.
    private static SyntheticMeasurement LowPassedArrival(int startSample)
    {
        const double CyclesHz = 55.0;
        double rise = 15.0 * SampleRate / 1000.0;
        double decay = 25.0 * SampleRate / 1000.0;
        var samples = new Complex[4_096];
        for (int i = 0; startSample + i < samples.Length; i++)
        {
            double envelope = (1.0 - Math.Exp(-i / rise)) * Math.Exp(-i / decay);
            samples[startSample + i] =
                envelope * Math.Sin(Math.Tau * CyclesHz * i / SampleRate);
        }
        return new SyntheticMeasurement(samples, SampleRate, startSample);
    }

    private static double FindPeakMs(SyntheticMeasurement measurement)
    {
        Complex[] samples = measurement.ImpulseResponse!;
        int peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i].Real) > Math.Abs(samples[peak].Real))
            {
                peak = i;
            }
        }
        return peak * 1_000.0 / measurement.SampleRate;
    }

    [Fact]
    public void SumGatedSpectra_ReReferencesEachPartBeforeAdding()
    {
        // One delta seen through two window POSITIONS (both plateaus contain
        // it): the windowed content is identical up to the extraction shift,
        // so re-referenced to one start the two spectra must coincide and
        // their sum must equal exactly twice the directly extracted one —
        // pinning the rotation's sign and scale, which the Virtual DSP Sum
        // (the vector sum of individually gated channels) rests on.
        SyntheticMeasurement measurement = DelayedImpulse(960); // 20 ms
        PhaseAnalysisSettings at20 = Settings(
            PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual, gateOffsetMs: 20.0);
        PhaseAnalysisSettings at18 = at20 with { GateOffsetMs = 18.0 };

        Complex[] a = DataHelper.GetPhaseAnalysisSpectrum(
            measurement, at20, out int startA);
        Complex[] b = DataHelper.GetPhaseAnalysisSpectrum(
            measurement, at18, out int startB);
        Assert.NotEqual(startA, startB);

        Complex[] combined = DataHelper.SumGatedSpectra(
            [(a, startA), (b, startB)], startA);

        for (int bin = 1; bin < combined.Length / 2; bin++)
        {
            Complex expected = 2.0 * a[bin];
            double error = (combined[bin] - expected).Magnitude;
            Assert.True(error <= 1e-9 * (1.0 + expected.Magnitude),
                $"Re-reference broken by {error:e} at bin {bin} " +
                $"({bin * (double)SampleRate / combined.Length:0.#} Hz).");
        }
    }

    [Fact]
    public void SumOfOwnGatedSpectra_KeepsTheLateChannelsTreble()
    {
        // The property the Virtual DSP Sum rests on, end to end: two unit
        // arrivals 3 ms apart, each FDW-gated at its OWN arrival, summed as
        // spectra. At high frequencies each window keeps its arrival, so the
        // band-averaged summed POWER reads both (~2; the interference cross
        // term averages out across the band). The old construction — one
        // summed IR through a single window anchored at the earliest arrival —
        // reads only the early channel (~1): the FDW window there is far
        // shorter than the 3 ms spread.
        var early = new Complex[8_192];
        early[480] = Complex.One; // 10 ms
        var late = new Complex[8_192];
        late[624] = Complex.One; // 13 ms
        PhaseAnalysisSettings atEarly = Settings(
            PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Manual,
            gateOffsetMs: 10.0);
        PhaseAnalysisSettings atLate = atEarly with { GateOffsetMs = 13.0 };

        Complex[] a = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(early, SampleRate, 480), atEarly, out int startA);
        Complex[] b = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(late, SampleRate, 624), atLate, out int startB);
        Complex[] combined = DataHelper.SumGatedSpectra(
            [(a, startA), (b, startB)], Math.Min(startA, startB));

        var summedIr = new Complex[8_192];
        for (int i = 0; i < summedIr.Length; i++)
        {
            summedIr[i] = early[i] + late[i];
        }
        Complex[] gatedSummedIr = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(summedIr, SampleRate, 480), atEarly, out _);

        double combinedPower = BandPower(combined, 8_000, 15_000);
        double gatedSumPower = BandPower(gatedSummedIr, 8_000, 15_000);
        Assert.InRange(combinedPower, 1.7, 2.3);
        Assert.InRange(gatedSumPower, 0.7, 1.3);

        static double BandPower(Complex[] spectrum, double lowHz, double highHz)
        {
            double binWidth = (double)SampleRate / spectrum.Length;
            int lowBin = (int)(lowHz / binWidth);
            int highBin = (int)(highHz / binWidth);
            double sum = 0.0;
            for (int bin = lowBin; bin <= highBin; bin++)
            {
                sum += spectrum[bin].Magnitude * spectrum[bin].Magnitude;
            }

            return sum / (highBin - lowBin + 1);
        }
    }

    [Fact]
    public void GatedPhaseData_FromSpectrum_MatchesTheMeasurementPath()
    {
        SyntheticMeasurement measurement = ReflectedImpulse();
        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Manual,
            manualMs: 10.0);

        List<SignalPoint> viaMeasurement = DataHelper.GetGatedPhaseData(
            measurement, settings);
        Complex[] spectrum = DataHelper.GetPhaseAnalysisSpectrum(
            measurement, settings, out int extractionStart);
        List<SignalPoint> viaSpectrum = DataHelper.GetGatedPhaseData(
            spectrum,
            extractionStart,
            referenceSamples: 10.0 * SampleRate / 1_000.0,
            SampleRate,
            unwrap: false);

        Assert.Equal(viaMeasurement, viaSpectrum);
    }

    [Fact]
    public void CommonAutoDetrend_DoesNotIndependentlyFlattenOtherChannels()
    {
        SyntheticMeasurement anchor = DelayedImpulse(480);
        SyntheticMeasurement later = DelayedImpulse(504);
        PhaseAnalysisSettings auto = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Auto);
        double common = DataHelper.ResolveCommonPhaseDetrendMilliseconds(anchor, auto);
        PhaseAnalysisSettings shared = auto with
        {
            DetrendMode = PhaseDetrendMode.Manual,
            ManualDetrendMilliseconds = common
        };

        List<SignalPoint> anchorPhase = DataHelper.GetGatedPhaseData(anchor, shared);
        List<SignalPoint> laterPhase = DataHelper.GetGatedPhaseData(later, shared);
        double anchorEnergy = MeanAbsoluteAngle(anchorPhase, 500, 5_000);
        double laterEnergy = MeanAbsoluteAngle(laterPhase, 500, 5_000);

        Assert.True(anchorEnergy < 1e-5);
        Assert.True(laterEnergy > 0.2,
            "The later channel was independently flattened instead of using the common reference.");
    }

    [Fact]
    public void Fdw_IsLinear_SpectrumOfASumIsTheSumOfTheSpectra()
    {
        // Virtual DSP's core invariant: the tool draws per-channel FDW phase
        // next to the FDW phase of the sample-wise summed IR, so the analysis
        // must satisfy FDW(A+B) = FDW(A) + FDW(B) bin for bin — otherwise the
        // drawn Sum need not match the vector sum of the drawn channels. Two
        // channels with DIFFERENT early reflections make the bank spectra
        // rotate differently between window lengths, which is exactly where
        // the earlier log-magnitude/shortest-arc interpolation broke
        // superposition by tens of degrees; the complex-linear interpolation
        // is exact here (the FFT and the window lerp are both linear). The
        // comparison runs on the complex spectra across ALL bins, so it also
        // covers every point BETWEEN the bank centers where the interpolation
        // acts.
        var first = new Complex[4_096];
        first[480] = Complex.One;
        first[480 + 62] = new Complex(0.7, 0.0); // +0.7 at 1.3 ms
        var second = new Complex[4_096];
        second[480] = Complex.One;
        second[480 + 101] = new Complex(-0.5, 0.0); // -0.5 at 2.1 ms
        var summed = new Complex[4_096];
        for (int i = 0; i < summed.Length; i++)
        {
            summed[i] = first[i] + second[i];
        }

        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Manual);
        Complex[] firstSpectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(first, SampleRate, 480),
            settings,
            out int firstStart);
        Complex[] secondSpectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(second, SampleRate, 480),
            settings,
            out int secondStart);
        Complex[] sumSpectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(summed, SampleRate, 480),
            settings,
            out int sumStart);

        Assert.Equal(firstStart, secondStart);
        Assert.Equal(firstStart, sumStart);
        for (int bin = 1; bin < sumSpectrum.Length / 2; bin++)
        {
            Complex expected = firstSpectrum[bin] + secondSpectrum[bin];
            double error = (sumSpectrum[bin] - expected).Magnitude;
            Assert.True(error <= 1e-9 * (1.0 + expected.Magnitude),
                $"Superposition broken by {error:e} at bin {bin} " +
                $"({bin * (double)SampleRate / sumSpectrum.Length:0.#} Hz).");
        }
    }

    [Fact]
    public void WrappedPhase_MasksBinsBelowTheReliabilityGate()
    {
        // A narrowband tone burst has no energy far above its band: the wrapped
        // phase there is noise and must be blanked (NaN), not drawn as ±180°
        // chaos. In-band bins stay finite.
        var impulse = new Complex[8_192];
        const int Start = 480;
        const int Length = 480; // 10 ms burst at 1 kHz
        for (int i = 0; i < Length; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (Length - 1.0)));
            impulse[Start + i] = new Complex(
                window * Math.Sin(2 * Math.PI * 1_000.0 * i / SampleRate), 0.0);
        }
        var measurement = new SyntheticMeasurement(impulse, SampleRate, Start + Length / 2);

        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual));

        Assert.Contains(phase, point =>
            point.X is >= 800 and <= 1_200 && !double.IsNaN(point.Y));
        Assert.All(
            phase.Where(point => point.X is >= 10_000 and <= 20_000),
            point => Assert.True(double.IsNaN(point.Y),
                $"Unreliable bin at {point.X:0.#} Hz was drawn ({point.Y:0.###} rad)."));
    }

    [Fact]
    public void WrappedPhase_KeepsEveryBinOfAFlatSpectrum()
    {
        // The masking must not over-fire: a pure delay is reliable everywhere,
        // so no bin of its wrapped phase goes missing.
        SyntheticMeasurement measurement = DelayedImpulse(480);
        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual));

        Assert.All(
            phase.Where(point => point.X is >= 100 and <= 20_000),
            point => Assert.False(double.IsNaN(point.Y)));
    }

    [Fact]
    public void FdwSuppressesLateReflectionMoreAtHighFrequency()
    {
        SyntheticMeasurement measurement = ReflectedImpulse();
        List<SignalPoint> fixedPhase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual));
        List<SignalPoint> fdwPhase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.FrequencyDependent, 4, PhaseDetrendMode.Manual));

        double fixedHigh = MeanAbsoluteAngle(fixedPhase, 8_000, 15_000);
        double fdwHigh = MeanAbsoluteAngle(fdwPhase, 8_000, 15_000);
        Assert.True(fdwHigh < fixedHigh * 0.7,
            $"FDW {fdwHigh:0.###} rad, fixed {fixedHigh:0.###} rad.");
    }

    private static double MeanAbsoluteAngle(
        IEnumerable<SignalPoint> points,
        double low,
        double high) => points
        .Where(point => point.X >= low && point.X <= high)
        .Average(point => Math.Abs(Math.IEEERemainder(point.Y, Math.Tau)));

    private static PhaseAnalysisSettings Settings(
        PhaseWindowMode windowMode,
        int cycles,
        PhaseDetrendMode detrendMode,
        double manualMs = 10.0,
        double gateOffsetMs = 10.0) => new(
            windowMode,
            cycles,
            detrendMode,
            manualMs,
            GateOffsetMs: gateOffsetMs,
            LeftMs: 1.0,
            PlateauMs: 3.0,
            RightMs: 12.0,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

    private static SyntheticMeasurement DelayedImpulse(int sample)
    {
        var impulse = new Complex[4_096];
        impulse[sample] = Complex.One;
        return new SyntheticMeasurement(impulse, SampleRate, sample);
    }

    private static SyntheticMeasurement ReflectedImpulse()
    {
        var impulse = new Complex[4_096];
        impulse[480] = Complex.One;
        impulse[576] = new Complex(0.4, 0.0); // 2 ms late reflection
        return new SyntheticMeasurement(impulse, SampleRate, 480);
    }
}
