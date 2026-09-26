using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>FDW group delay: τ = Re[T·conj(H)] / |H|² on the stitched FDW bank and its time-weighted twin.</summary>
public sealed class FrequencyDependentGroupDelayTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4_096;
    private const int ArrivalSample = 480; // 10 ms.

    [Fact]
    public void FixedWindow_SettingsOverloadMatchesTheLegacySignatureBitForBit()
    {
        // Pins the contract between the legacy signature and the settings overload under Fixed; arithmetic against the pre-FDW
        // build was verified once byte for byte (27 combinations), and BankPlan_IsTheOriginalWalk_CentreForCentre keeps the bank's half.
        var response = new Complex[TransformLength];
        for (int i = 0; i < 600; i++)
        {
            response[ArrivalSample + i] = new Complex(Math.Pow(0.9, i), 0);
        }
        var measurement = new SyntheticMeasurement(response, SampleRate, ArrivalSample);

        GroupDelayCurveSet legacy = DataHelper.GetGroupDelayCurves(
            measurement,
            gateOffsetMs: 10.0,
            leftMs: 2.0,
            plateauMs: 20.0,
            rightMs: 5.0,
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);
        GroupDelayCurveSet viaSettings = DataHelper.GetGroupDelayCurves(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6) with
            {
                LeftMs = 2.0,
                PlateauMs = 20.0,
                RightMs = 5.0
            },
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);

        Assert.Equal(legacy.Measured.Points, viaSettings.Measured.Points);
        Assert.Equal(legacy.Minimum!.Points, viaSettings.Minimum!.Points);
        Assert.Equal(legacy.Excess!.Points, viaSettings.Excess!.Points);
    }

    [Fact]
    public void GroupDelayRead_LeavesThePhaseSpectrumBitIdentical()
    {
        // Replacing the cached phase-only entry with the pair must not move the phase by a bit.
        SyntheticMeasurement measurement = ReflectedImpulse();
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, 8);

        List<SignalPoint> before = DataHelper.GetGatedPhaseData(measurement, settings);
        DataHelper.GetGroupDelayCurves(measurement, settings, smoothingInverseOctaves: 12);
        List<SignalPoint> after = DataHelper.GetGatedPhaseData(measurement, settings);

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(PhaseWindowMode.Fixed)]
    [InlineData(PhaseWindowMode.FrequencyDependent)]
    public void AGroupDelayReadAfterAPhaseRead_MatchesACold_OneBitForBit(PhaseWindowMode windowMode)
    {
        // The upgrade transforms only the twin; a cold read transforms the pair together.
        PhaseAnalysisSettings settings = Settings(windowMode, 6);
        SyntheticMeasurement warm = ReflectedImpulse();
        DataHelper.GetGatedPhaseData(warm, settings);

        GroupDelayCurveSet upgraded = DataHelper.GetGroupDelayCurves(
            warm, settings, smoothingInverseOctaves: 12, includeMinimumPhase: true);
        GroupDelayCurveSet cold = DataHelper.GetGroupDelayCurves(
            ReflectedImpulse(), settings, smoothingInverseOctaves: 12, includeMinimumPhase: true);

        Assert.Equal(cold.Measured.Points, upgraded.Measured.Points);
        Assert.Equal(cold.Minimum!.Points, upgraded.Minimum!.Points);
        Assert.Equal(cold.Excess!.Points, upgraded.Excess!.Points);
    }

    [Fact]
    public void ACancelledRead_StopsAndCachesNothing()
    {
        SyntheticMeasurement measurement = ReflectedImpulse();
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, 8);

        Assert.Throws<OperationCanceledException>(() => DataHelper.GetGroupDelayCurves(
            measurement, settings, smoothingInverseOctaves: 12, cancellationToken: new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => DataHelper.GetPhase(
            measurement, settings, cancellationToken: new CancellationToken(true)));

        Assert.Equal(0, DataHelper.CachedPhaseSpectrumCount(measurement.ImpulseResponse!));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void PureDelay_IsFlatAtTheDelayUnderEveryCycleCount(int cycles)
    {
        SyntheticMeasurement measurement = DelayedImpulse(ArrivalSample);
        PhaseAnalysisSettings fdw = Settings(PhaseWindowMode.FrequencyDependent, cycles);

        GroupDelayCurveSet fdwCurves = DataHelper.GetGroupDelayCurves(
            measurement, fdw, smoothingInverseOctaves: 96);
        GroupDelayCurveSet fixedCurves = DataHelper.GetGroupDelayCurves(
            measurement, fdw with { WindowMode = PhaseWindowMode.Fixed }, smoothingInverseOctaves: 96);

        double delayMs = ArrivalSample * 1000.0 / SampleRate;
        List<int> band = BandIndices(fdwCurves.Measured, 100, 10_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(
            fdwCurves.Measured.Points[i].Y, delayMs - 0.01, delayMs + 0.01));
        Assert.All(band, i => Assert.InRange(
            fdwCurves.Measured.Points[i].Y - fixedCurves.Measured.Points[i].Y, -1e-9, 1e-9));
    }

    [Fact]
    public void Fdw_WhenEveryWindowIsClamped_MatchesFixed()
    {
        // Gate shorter than the 0.8 ms floor: the bank is one entry, so FDW equals Fixed.
        SyntheticMeasurement measurement = ReflectedImpulse();
        PhaseAnalysisSettings fixedSettings = Settings(PhaseWindowMode.Fixed, 8) with
        {
            LeftMs = 0.5,
            PlateauMs = 0.4,
            RightMs = 0.1
        };
        PhaseAnalysisSettings fdwSettings = fixedSettings with
        {
            WindowMode = PhaseWindowMode.FrequencyDependent
        };

        GroupDelayCurveSet fixedCurves = DataHelper.GetGroupDelayCurves(
            measurement, fixedSettings, smoothingInverseOctaves: 12, includeMinimumPhase: true);
        GroupDelayCurveSet fdwCurves = DataHelper.GetGroupDelayCurves(
            measurement, fdwSettings, smoothingInverseOctaves: 12, includeMinimumPhase: true);

        AssertSameCurve(fixedCurves.Measured, fdwCurves.Measured, 1e-9);
        AssertSameCurve(fixedCurves.Minimum!, fdwCurves.Minimum!, 1e-9);
        AssertSameCurve(fixedCurves.Excess!, fdwCurves.Excess!, 1e-9);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void DirectSoundWithALateReflection_FdwReadsTheDirectArrivalWhereFixedRipples()
    {
        // Window 1 + 8/f ms with left = 1 ms: the 6 ms reflection is outside it from 1.6 kHz, so from 2 kHz the curve is the direct arrival.
        // Below 615 Hz the window clamps to the full gate.
        const int reflectionSamples = 288; // 6 ms.
        var response = new Complex[TransformLength];
        response[ArrivalSample] = Complex.One;
        response[ArrivalSample + reflectionSamples] = new Complex(0.5, 0.0);
        var measurement = new SyntheticMeasurement(response, SampleRate, ArrivalSample);
        PhaseAnalysisSettings fixedSettings = Settings(PhaseWindowMode.Fixed, 8) with
        {
            LeftMs = 1.0,
            PlateauMs = 10.0,
            RightMs = 3.0
        };
        PhaseAnalysisSettings fdwSettings = fixedSettings with
        {
            WindowMode = PhaseWindowMode.FrequencyDependent
        };

        GroupDelayCurveSet fixedCurves = DataHelper.GetGroupDelayCurves(
            measurement, fixedSettings, smoothingInverseOctaves: 0);
        GroupDelayCurveSet fdwCurves = DataHelper.GetGroupDelayCurves(
            measurement, fdwSettings, smoothingInverseOctaves: 0);

        double arrivalMs = ArrivalSample * 1000.0 / SampleRate;
        List<int> ripplingBand = BandIndices(fixedCurves.Measured, 2_000, 10_000);
        Assert.NotEmpty(ripplingBand);
        double fixedSwing =
            ripplingBand.Max(i => fixedCurves.Measured.Points[i].Y) -
            ripplingBand.Min(i => fixedCurves.Measured.Points[i].Y);
        Assert.True(
            fixedSwing >= 1.0,
            $"the Fixed gate did not see the interference (swing {fixedSwing:0.000} ms).");
        Assert.All(ripplingBand, i => Assert.InRange(
            fdwCurves.Measured.Points[i].Y, arrivalMs - 0.1, arrivalMs + 0.1));

        List<int> clampedBand = BandIndices(fixedCurves.Measured, 100, 500);
        Assert.NotEmpty(clampedBand);
        Assert.All(clampedBand, i => Assert.InRange(
            fdwCurves.Measured.Points[i].Y - fixedCurves.Measured.Points[i].Y, -1e-6, 1e-6));
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(100)]
    public void SumGatedSpectraPairs_CarriesTheTimeWeightAcrossExtractionStarts(int startShift)
    {
        // Re-referencing moves the time origin; T gains ((s − s_ref) / fs) · H or the curve shifts by the origin's move.
        SyntheticMeasurement measurement = DelayedImpulse(ArrivalSample);
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.Fixed, 8) with
        {
            PlateauMs = 5.0,
            RightMs = 4.0
        };
        GroupDelaySpectra pair = DataHelper.GetGroupDelayAnalysisSpectra(
            measurement, settings, out int extractionStart);
        int targetStart = extractionStart + startShift;

        GroupDelaySpectra moved = DataHelper.SumGatedSpectraPairs(
            new[] { (pair, extractionStart) }, targetStart, SampleRate);
        GroupDelayCurveSet direct = DataHelper.GetGroupDelayCurves(
            pair, extractionStart, SampleRate, settings, smoothingInverseOctaves: 96);
        GroupDelayCurveSet viaMove = DataHelper.GetGroupDelayCurves(
            moved, targetStart, SampleRate, settings, smoothingInverseOctaves: 96);

        List<int> band = BandIndices(direct.Measured, 100, 20_000);
        Assert.NotEmpty(band);
        double delayMs = ArrivalSample * 1000.0 / SampleRate;
        Assert.All(band, i => Assert.InRange(
            direct.Measured.Points[i].Y, delayMs - 1e-9, delayMs + 1e-9));
        Assert.All(band, i => Assert.InRange(
            viaMove.Measured.Points[i].Y - direct.Measured.Points[i].Y, -1e-9, 1e-9));
    }

    [Fact]
    public void TwoPlacementsOfOneResponse_SumToTheSameArrival()
    {
        // Bank invariant: two extractions at different starts summed in one reference show no step.
        SyntheticMeasurement measurement = DelayedImpulse(ArrivalSample);
        PhaseAnalysisSettings early = Settings(PhaseWindowMode.Fixed, 8) with
        {
            GateOffsetMs = 8.0,
            LeftMs = 1.0,
            PlateauMs = 6.0,
            RightMs = 4.0
        };
        PhaseAnalysisSettings late = early with { GateOffsetMs = 9.5 };
        GroupDelaySpectra earlyPair = DataHelper.GetGroupDelayAnalysisSpectra(
            measurement, early, out int earlyStart);
        GroupDelaySpectra latePair = DataHelper.GetGroupDelayAnalysisSpectra(
            measurement, late, out int lateStart);
        Assert.NotEqual(earlyStart, lateStart);

        GroupDelaySpectra sum = DataHelper.SumGatedSpectraPairs(
            new[] { (earlyPair, earlyStart), (latePair, lateStart) }, earlyStart, SampleRate);
        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            sum, earlyStart, SampleRate, early, smoothingInverseOctaves: 96);

        double delayMs = ArrivalSample * 1000.0 / SampleRate;
        List<int> band = BandIndices(curves.Measured, 100, 20_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(
            curves.Measured.Points[i].Y, delayMs - 1e-9, delayMs + 1e-9));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void SumGatedSpectraPairs_SharedWindow_MatchesTheGateOverTheSummedImpulse()
    {
        // Linearity through one window, which the Virtual DSP Sum rests on.
        var first = new Complex[TransformLength];
        var second = new Complex[TransformLength];
        var both = new Complex[TransformLength];
        for (int i = 0; i < 600; i++)
        {
            first[ArrivalSample + i] = new Complex(Math.Pow(0.9, i), 0);
            second[ArrivalSample + 20 + i] = new Complex(0.7 * Math.Pow(0.8, i), 0);
        }
        for (int i = 0; i < TransformLength; i++)
        {
            both[i] = first[i] + second[i];
        }
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, 8);

        GroupDelaySpectra firstPair = DataHelper.GetGroupDelayAnalysisSpectra(
            new SyntheticMeasurement(first, SampleRate, ArrivalSample), settings, out int firstStart);
        GroupDelaySpectra secondPair = DataHelper.GetGroupDelayAnalysisSpectra(
            new SyntheticMeasurement(second, SampleRate, ArrivalSample), settings, out int secondStart);
        Assert.Equal(firstStart, secondStart);
        GroupDelaySpectra sum = DataHelper.SumGatedSpectraPairs(
            new[] { (firstPair, firstStart), (secondPair, secondStart) }, firstStart, SampleRate);

        GroupDelayCurveSet viaSum = DataHelper.GetGroupDelayCurves(
            sum, firstStart, SampleRate, settings, smoothingInverseOctaves: 12);
        GroupDelayCurveSet viaImpulse = DataHelper.GetGroupDelayCurves(
            new SyntheticMeasurement(both, SampleRate, ArrivalSample), settings, smoothingInverseOctaves: 12);

        AssertSameCurve(viaImpulse.Measured, viaSum.Measured, 1e-9);
    }

    [Fact]
    public void SumOfTwoDelays_EachGatedAtItsOwnArrival_ReadsTheEnergyWeightedMean()
    {
        // Per bin the sum of two gated delays carries a √(e1·e2)·cos(ω·Δ) term; FDW-8 smoothing (f/16 each side) spans
        // ≥2 ripple periods from 2 kHz for Δ = 8 ms, leaving a few hundredths of a ms.
        const int secondArrival = ArrivalSample + 384; // +8 ms.
        const double secondAmplitude = 0.5;
        var first = new Complex[TransformLength];
        var second = new Complex[TransformLength];
        first[ArrivalSample] = Complex.One;
        second[secondArrival] = new Complex(secondAmplitude, 0.0);
        PhaseAnalysisSettings atFirst = Settings(PhaseWindowMode.FrequencyDependent, 8);
        PhaseAnalysisSettings atSecond = atFirst with
        {
            GateOffsetMs = secondArrival * 1000.0 / SampleRate
        };

        GroupDelaySpectra firstPair = DataHelper.GetGroupDelayAnalysisSpectra(
            new SyntheticMeasurement(first, SampleRate, ArrivalSample), atFirst, out int firstStart);
        GroupDelaySpectra secondPair = DataHelper.GetGroupDelayAnalysisSpectra(
            new SyntheticMeasurement(second, SampleRate, secondArrival), atSecond, out int secondStart);
        Assert.NotEqual(firstStart, secondStart);
        GroupDelaySpectra sum = DataHelper.SumGatedSpectraPairs(
            new[] { (firstPair, firstStart), (secondPair, secondStart) }, firstStart, SampleRate);
        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            sum, firstStart, SampleRate, atFirst, smoothingInverseOctaves: 12);

        double d1 = ArrivalSample * 1000.0 / SampleRate;
        double d2 = secondArrival * 1000.0 / SampleRate;
        double e2 = secondAmplitude * secondAmplitude;
        double expected = (d1 + e2 * d2) / (1.0 + e2);
        List<int> band = BandIndices(curves.Measured, 4_000, 12_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(
            curves.Measured.Points[i].Y, expected - 0.05, expected + 0.05));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void MinimumPhaseSystem_HasNearZeroExcessUnderFdw()
    {
        // 0.9ⁿ and every truncation of it are minimum-phase, so the excess reads ≈ 0.
        var response = new Complex[TransformLength];
        for (int i = 0; i < 1_000; i++)
        {
            response[i] = new Complex(Math.Pow(0.9, i), 0);
        }
        var measurement = new SyntheticMeasurement(response, SampleRate, 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            FullPlateau(PhaseWindowMode.FrequencyDependent, 8),
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);

        List<int> band = BandIndices(curves.Measured, 100, 18_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(curves.Excess!.Points[i].Y, -0.01, 0.01));
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(2.0, 6, 0.5, 4.0, 1.5)]
    [InlineData(5.0, 6, 0.5, 4.0, 1.5)]
    [InlineData(10.0, 6, 0.5, 4.0, 1.5)]
    [InlineData(2.0, 8, 0.5, 10.0, 3.0)]
    [InlineData(5.0, 8, 0.5, 10.0, 3.0)]
    [InlineData(10.0, 8, 0.5, 10.0, 3.0)]
    [InlineData(10.0, 4, 1.0, 3.0, 12.0)]
    public void MinimumPhasePeqChain_ReadsNearZeroExcessUnderFdw(
        double q, int cycles, double leftMs, double plateauMs, double rightMs)
    {
        // FDW excess is not the classical all-pass GD by construction; empirically it reads PEQ content's bulk delay
        // to within 0.05 ms across gates and cycle counts, the same order as Fixed.
        const int arrival = 480;
        const int length = 16_384;
        double[] impulse = new double[length];
        impulse[arrival] = 1.0;
        foreach (PeqBand peq in new[]
        {
            new PeqBand(100.0, q, 6.0),
            new PeqBand(1_000.0, q, -6.0),
            new PeqBand(5_000.0, q, 6.0)
        })
        {
            impulse = FilterAdditiveFeedback(PeakingBiquad.Compute(peq, SampleRate), impulse);
        }
        var response = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            response[i] = new Complex(impulse[i], 0);
        }
        var measurement = new SyntheticMeasurement(response, SampleRate, arrival);
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, cycles) with
        {
            LeftMs = leftMs,
            PlateauMs = plateauMs,
            RightMs = rightMs
        };

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement, settings, smoothingInverseOctaves: 12, includeMinimumPhase: true);

        double bulkMs = arrival * 1000.0 / SampleRate;
        List<int> band = BandIndices(curves.Excess!, 100, 15_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(
            curves.Excess!.Points[i].Y, bulkMs - 0.06, bulkMs + 0.06));
    }

    [Fact]
    public void Crossover_ExcessUnderFdw_IsTheFixedGatesExcess()
    {
        // Gate truncation of a steep high-pass reads as low-edge excess under every window; FDW must not add to it.
        // Bins where only FDW's wider floor keeps a value are excluded.
        const int arrival = 480;
        const int length = 16_384;
        double[] impulse = new double[length];
        impulse[arrival] = 1.0;
        var sections = new List<BiquadCoefficients>();
        sections.AddRange(CrossoverFilter.BuildSections(
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80.0, 24), highPass: true, SampleRate));
        sections.AddRange(CrossoverFilter.BuildSections(
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000.0, 24), highPass: false, SampleRate));
        foreach (BiquadCoefficients section in sections)
        {
            impulse = FilterAdditiveFeedback(section, impulse);
        }
        var response = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            response[i] = new Complex(impulse[i], 0);
        }
        var measurement = new SyntheticMeasurement(response, SampleRate, arrival);
        PhaseAnalysisSettings fixedSettings = Settings(PhaseWindowMode.Fixed, 8) with
        {
            LeftMs = 0.5,
            PlateauMs = 10.0,
            RightMs = 3.0
        };

        GroupDelayCurveSet fixedCurves = DataHelper.GetGroupDelayCurves(
            measurement, fixedSettings, smoothingInverseOctaves: 12, includeMinimumPhase: true);
        GroupDelayCurveSet fdwCurves = DataHelper.GetGroupDelayCurves(
            measurement,
            fixedSettings with { WindowMode = PhaseWindowMode.FrequencyDependent },
            smoothingInverseOctaves: 12,
            includeMinimumPhase: true);

        List<int> band = BandIndices(fixedCurves.Excess!, 100, 15_000);
        Assert.NotEmpty(band);
        List<int> both = band
            .Where(i => double.IsFinite(fixedCurves.Excess!.Points[i].Y) &&
                double.IsFinite(fdwCurves.Excess!.Points[i].Y))
            .ToList();
        int disagree = band.Count(i =>
            double.IsFinite(fixedCurves.Excess!.Points[i].Y) !=
            double.IsFinite(fdwCurves.Excess!.Points[i].Y));
        Assert.NotEmpty(both);
        Assert.True(disagree < band.Count * 0.02, $"the two windows blank different bands ({disagree} bins)");
        Assert.All(both, i => Assert.InRange(
            fdwCurves.Excess!.Points[i].Y - fixedCurves.Excess!.Points[i].Y, -0.05, 0.05));
        double lowEdge = NearestY(fixedCurves.Excess!, 101.0);
        double midBand = NearestY(fixedCurves.Excess!, 1_000.0);
        Assert.True(lowEdge - midBand > 0.5, $"no low-edge excess ({lowEdge - midBand:0.000} ms)");
    }

    [Fact]
    public void AllPass_DispersionLandsInExcessNotMinimumUnderFdw()
    {
        // All-pass ringing dies well inside 8 ms at 1 kHz: |H| flat, the corner pile-up lands in the excess.
        IReadOnlyList<BiquadCoefficients> sections = AllPassFilter.BuildSections(
            new AllPassSpec(AllPassType.SecondOrder, 1_000.0, Q: 2.0),
            SampleRate);
        Assert.NotEmpty(sections);
        double[] impulse = new double[TransformLength];
        impulse[0] = 1.0;
        foreach (BiquadCoefficients biquad in sections)
        {
            impulse = FilterAdditiveFeedback(biquad, impulse);
        }
        var response = new Complex[TransformLength];
        for (int i = 0; i < TransformLength; i++)
        {
            response[i] = new Complex(impulse[i], 0);
        }
        var measurement = new SyntheticMeasurement(response, SampleRate, 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            FullPlateau(PhaseWindowMode.FrequencyDependent, 8),
            smoothingInverseOctaves: 96,
            includeMinimumPhase: true);

        List<int> band = BandIndices(curves.Measured, 100, 18_000);
        Assert.NotEmpty(band);
        Assert.All(band, i => Assert.InRange(curves.Minimum!.Points[i].Y, -0.05, 0.05));
        double excessAtCorner = NearestY(curves.Excess!, 1_000.0);
        double excessFarAbove = NearestY(curves.Excess!, 10_000.0);
        Assert.True(
            excessAtCorner - excessFarAbove > 0.8,
            $"the all-pass dispersion never reached the excess curve " +
            $"({excessAtCorner:0.000} ms at 1 kHz vs {excessFarAbove:0.000} ms at 10 kHz)");
    }

    [Fact]
    public void EffectiveGate_IsTheBankAndTheSmoothingFloor()
    {
        // Floor = half the window's resolution: 62.5 Hz at 1 kHz for 8 cycles (0.5 · 48000 / 384).
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, 8) with
        {
            LeftMs = 0.0,
            PlateauMs = 100.0,
            RightMs = 0.0
        };

        IReadOnlyList<(double CenterFrequencyHz, int EffectiveGateSamples)> bank =
            DataHelper.DescribeFdwBank(settings, SampleRate);
        Assert.NotEmpty(bank);
        Assert.All(bank, entry => Assert.Equal(
            entry.EffectiveGateSamples,
            DataHelper.FdwEffectiveGateSamples(entry.CenterFrequencyHz, settings, SampleRate)));

        int previous = int.MaxValue;
        for (double f = 20.0; f <= 20_000.0; f *= 1.01)
        {
            int gate = DataHelper.FdwEffectiveGateSamples(f, settings, SampleRate);
            Assert.True(gate <= previous, $"the window grew with frequency at {f:0.#} Hz.");
            previous = gate;
        }

        Assert.Equal(384, DataHelper.FdwEffectiveGateSamples(1_000.0, settings, SampleRate));
        Assert.Equal(62.5, DataHelper.GroupDelayMinimumHalfWidthHz(1_000.0, settings, SampleRate));

        PhaseAnalysisSettings fixedSettings = settings with { WindowMode = PhaseWindowMode.Fixed };
        Assert.Equal(4_800, DataHelper.FdwEffectiveGateSamples(1_000.0, fixedSettings, SampleRate));
        Assert.Equal(4_800, DataHelper.FdwEffectiveGateSamples(20_000.0, fixedSettings, SampleRate));
    }

    [Theory]
    [InlineData(44_100, 4, 1.0, 3.0, 12.0)]
    [InlineData(48_000, 6, 1.0, 3.0, 12.0)]
    [InlineData(48_000, 8, 0.0, 100.0, 0.0)]
    [InlineData(96_000, 8, 0.5, 0.4, 0.1)]
    [InlineData(96_000, 6, 2.0, 20.0, 5.0)]
    [InlineData(192_000, 4, 2.0, 500.0, 180.0)]
    public void BankPlan_IsTheOriginalWalk_CentreForCentre(
        int sampleRate, int cycles, double leftMs, double plateauMs, double rightMs)
    {
        // The original BuildFdwSpectrum walk kept verbatim: same centres, merge into the LAST centre, shortest window at Nyquist.
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, cycles) with
        {
            LeftMs = leftMs,
            PlateauMs = plateauMs,
            RightMs = rightMs
        };
        int ToSamples(double ms) => (int)Math.Round(Math.Max(0.0, ms) * sampleRate / 1000.0);
        int left = ToSamples(leftMs);
        int fixedGate = Math.Clamp(
            left + ToSamples(plateauMs) + ToSamples(rightMs), 1, DataHelper.GatedFftLength);
        int minimumGate = Math.Clamp(
            left + (int)Math.Round(0.0008 * sampleRate), 1, fixedGate);
        double binWidth = sampleRate / (double)DataHelper.GatedFftLength;
        double nyquist = sampleRate / 2.0;
        var expected = new List<(double Center, int Gate)>();
        int previousGate = -1;
        for (double center = binWidth; center <= nyquist; center *= Math.Pow(2.0, 1.0 / 3.0))
        {
            int effectiveGate = Math.Clamp(
                left + (int)Math.Round(cycles * sampleRate / center), minimumGate, fixedGate);
            if (effectiveGate == previousGate)
            {
                expected[^1] = (center, effectiveGate);
                continue;
            }

            expected.Add((center, effectiveGate));
            previousGate = effectiveGate;
        }
        if (expected.Count == 0 || expected[^1].Center < nyquist)
        {
            if (expected.Count == 0 || expected[^1].Gate != minimumGate)
            {
                expected.Add((nyquist, minimumGate));
            }
        }

        IReadOnlyList<(double CenterFrequencyHz, int EffectiveGateSamples)> actual =
            DataHelper.DescribeFdwBank(settings, sampleRate);

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Center, actual[i].CenterFrequencyHz);
            Assert.Equal(expected[i].Gate, actual[i].EffectiveGateSamples);
        }
    }

    [Theory]
    [InlineData(PhaseWindowMode.Fixed)]
    [InlineData(PhaseWindowMode.FrequencyDependent)]
    public void ValidityGate_BlanksTheSameBinsInEveryCurve(PhaseWindowMode mode)
    {
        var response = new Complex[TransformLength];
        response[0] = Complex.One;
        response[1] = -Complex.One;
        var measurement = new SyntheticMeasurement(response, SampleRate, 0);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            measurement,
            FullPlateau(mode, 8),
            smoothingInverseOctaves: 0,
            includeMinimumPhase: true);

        Assert.Equal(curves.Measured.Points.Count, curves.Minimum!.Points.Count);
        Assert.Equal(curves.Measured.Points.Count, curves.Excess!.Points.Count);
        for (int i = 0; i < curves.Measured.Points.Count; i++)
        {
            bool measuredIsNaN = double.IsNaN(curves.Measured.Points[i].Y);
            Assert.Equal(measuredIsNaN, double.IsNaN(curves.Minimum.Points[i].Y));
            Assert.Equal(measuredIsNaN, double.IsNaN(curves.Excess.Points[i].Y));
        }
        Assert.Contains(curves.Measured.Points, point => double.IsNaN(point.Y));
        Assert.Contains(curves.Measured.Points, point => double.IsFinite(point.Y));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void MeasuredBand_BlanksTheCurveOutsideIt()
    {
        SyntheticMeasurement measurement = DelayedImpulse(ArrivalSample);
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, 8);
        GroupDelaySpectra pair = DataHelper.GetGroupDelayAnalysisSpectra(
            measurement, settings, out int extractionStart);

        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            pair,
            extractionStart,
            SampleRate,
            settings,
            smoothingInverseOctaves: 12,
            lowestMeasuredFrequencyHz: 200.0,
            highestMeasuredFrequencyHz: 8_000.0);

        Assert.All(curves.Measured.Points.Where(p => p.X < 200.0), p => Assert.True(double.IsNaN(p.Y)));
        Assert.All(curves.Measured.Points.Where(p => p.X > 8_000.0), p => Assert.True(double.IsNaN(p.Y)));
        Assert.All(
            curves.Measured.Points.Where(p => p.X is >= 200.0 and <= 8_000.0),
            p => Assert.True(double.IsFinite(p.Y)));
    }

    private static void AssertSameCurve(AnalysisCurve expected, AnalysisCurve actual, double toleranceMs)
    {
        Assert.Equal(expected.Points.Count, actual.Points.Count);
        for (int i = 0; i < expected.Points.Count; i++)
        {
            Assert.Equal(expected.Points[i].X, actual.Points[i].X);
            bool expectedNaN = double.IsNaN(expected.Points[i].Y);
            Assert.Equal(expectedNaN, double.IsNaN(actual.Points[i].Y));
            if (!expectedNaN)
            {
                Assert.InRange(
                    actual.Points[i].Y - expected.Points[i].Y, -toleranceMs, toleranceMs);
            }
        }
    }

    private static PhaseAnalysisSettings Settings(PhaseWindowMode windowMode, int cycles) => new(
        windowMode,
        cycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: ArrivalSample * 1000.0 / SampleRate,
        LeftMs: 1.0,
        PlateauMs: 3.0,
        RightMs: 12.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    private static PhaseAnalysisSettings FullPlateau(PhaseWindowMode windowMode, int cycles) =>
        Settings(windowMode, cycles) with
        {
            GateOffsetMs = 0.0,
            LeftMs = 0.0,
            PlateauMs = TransformLength * 1000.0 / SampleRate,
            RightMs = 0.0
        };

    private static SyntheticMeasurement DelayedImpulse(int sample)
    {
        var impulse = new Complex[TransformLength];
        impulse[sample] = Complex.One;
        return new SyntheticMeasurement(impulse, SampleRate, sample);
    }

    private static SyntheticMeasurement ReflectedImpulse()
    {
        var impulse = new Complex[TransformLength];
        impulse[ArrivalSample] = Complex.One;
        impulse[ArrivalSample + 96] = new Complex(0.4, 0.0); // 2 ms late reflection
        return new SyntheticMeasurement(impulse, SampleRate, ArrivalSample);
    }

    private static List<int> BandIndices(AnalysisCurve curve, double lowHz, double highHz)
    {
        var indices = new List<int>();
        for (int i = 0; i < curve.Points.Count; i++)
        {
            if (curve.Points[i].X >= lowHz && curve.Points[i].X <= highHz)
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private static double NearestY(AnalysisCurve curve, double frequencyHz) =>
        curve.Points
            .Where(point => double.IsFinite(point.Y))
            .MinBy(point => Math.Abs(point.X - frequencyHz))
            .Y;

    // Additive-feedback convention: y[n] = b0·x[n] + b1·x[n−1] + b2·x[n−2] + a1·y[n−1] + a2·y[n−2].
    private static double[] FilterAdditiveFeedback(BiquadCoefficients biquad, double[] input)
    {
        double[] output = new double[input.Length];
        double x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0;
        for (int i = 0; i < input.Length; i++)
        {
            double x = input[i];
            double y = biquad.B0 * x + biquad.B1 * x1 + biquad.B2 * x2 +
                biquad.A1 * y1 + biquad.A2 * y2;
            output[i] = y;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
        }

        return output;
    }
}
