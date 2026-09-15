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
        // Re-referencing cancels placement only while both plateaus contain the response; GateLeadingEdgeLossDb checks that.
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
        // A steeply low-passed channel peaks long after it starts: a peak-placed plateau reads 177° off (field 176°).
        SyntheticMeasurement channel = LowPassedArrival(startSample: 480);
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
        // Field figures at this gate: own-arrival placements -28.4 to -72.2 dB, peak placements -3.5 to -10.8 dB.
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
        Assert.True(
            onThePeak - containing > 15.0,
            $"the guard separated the placements by only {onThePeak - containing:0.0} dB");
    }

    [Fact]
    public void GateLeadingEdgeLoss_SeesTheWrappedContentAGateReachingBeforeZeroReads()
    {
        // An offset smaller than the left shoulder extracts with wrap, so the guard must read the circular tail too.
        var samples = new Complex[4_096];
        samples[0] = Complex.One;      // the arrival, at the plateau
        samples[^12] = Complex.One;    // negative time, inside the fade-in
        var channel = new SyntheticMeasurement(samples, SampleRate, 0);

        double loss = DataHelper.GateLeadingEdgeLossDb(
            channel, gateOffsetMs: 0.0, leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        Assert.True(
            double.IsFinite(loss),
            "the wrapped shoulder read as empty, so nothing counted as lost");
        // -2.2 dB against the -20 dB ceiling; ignoring the wrap read -3233 dB.
        Assert.True(loss > -20.0, $"the wrapped content only read {loss:0.0} dB");
    }

    [Fact]
    public void GateLeadingEdgeLoss_AWindowHoldingNoneOfTheChannelReadsAsUnsafe()
    {
        // A window holding none of the channel must read as the worst placement, not 'nothing lost'.
        var samples = new Complex[4_096];
        samples[SampleRate * 20 / 1_000] = Complex.One;
        var channel = new SyntheticMeasurement(
            samples, SampleRate, SampleRate * 20 / 1_000);

        double missed = DataHelper.GateLeadingEdgeLossDb(
            channel, gateOffsetMs: 0.0, leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);
        double onTheArrival = DataHelper.GateLeadingEdgeLossDb(
            channel, gateOffsetMs: 20.0, leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        Assert.True(
            missed > onTheArrival,
            $"a window holding none of the channel read {missed:0.0} dB against " +
            $"{onTheArrival:0.0} dB for one placed on its arrival");
        Assert.False(double.IsNegativeInfinity(missed));
    }

    [Fact]
    public void AGuardedPerCurvePlacement_ReadsTheSamePhaseAsAContainingSharedOne()
    {
        // A passing placement lets each channel take its own FDW window without making curves incomparable.
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

        // Judged only where the 55 Hz ring has energy (field: 0.2-1.5° between placements).
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

    // Steep-LP sub shape (field: start 15.6 ms, peak 36.7 ms).
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
        // Pins the rotation's sign and scale, which the Virtual DSP Sum rests on.
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
        // Each channel gated at its own arrival keeps both in the summed power (~2); one window at the earliest reads ~1.
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
        // FDW(A+B) = FDW(A) + FDW(B) bin for bin, including between bank centres; different reflections break
        // a log-magnitude interpolation by tens of degrees.
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
        // No energy far above the band: wrapped phase there is blanked (NaN).
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
