using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The group delay read through the frequency-dependent window: the same
/// τ = Re[T·conj(H)] / |H|² identity as the Fixed curve, evaluated on the
/// stitched FDW bank and its time-weighted twin, so at every frequency it is
/// the energy-weighted arrival time inside the window applied there.
/// </summary>
public sealed class FrequencyDependentGroupDelayTests
{
    private const int SampleRate = 48_000;
    private const int TransformLength = 4_096;
    private const int ArrivalSample = 480; // 10 ms.

    [Fact]
    public void FixedWindow_SettingsOverloadMatchesTheLegacySignatureBitForBit()
    {
        // The app's typical geometry (a bulk delay ahead of a one-pole, the
        // gate on the arrival, a left shoulder): the settings overload under
        // Fixed must be the legacy signature, curve for curve and bit for
        // bit. The legacy signature now delegates to the settings overload,
        // so this pins the CONTRACT between the two (the Fixed defaults it
        // fills in), not the arithmetic against the pre-FDW build — that was
        // held byte for byte against the pre-feature library once, by dumping
        // both over 27 gate/rate/smoothing combinations (106 MB, identical),
        // and BankPlan_IsTheOriginalWalk_CentreForCentre keeps the bank's
        // half of it under test.
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
        // A group-delay reader replaces the cached phase-only entry with the
        // pair. The phase read before and after must not move by a bit: the
        // spectrum member of the pair is the same extraction through the
        // same transform.
        SyntheticMeasurement measurement = ReflectedImpulse();
        PhaseAnalysisSettings settings = Settings(PhaseWindowMode.FrequencyDependent, 8);

        List<SignalPoint> before = DataHelper.GetGatedPhaseData(measurement, settings);
        DataHelper.GetGroupDelayCurves(measurement, settings, smoothingInverseOctaves: 12);
        List<SignalPoint> after = DataHelper.GetGatedPhaseData(measurement, settings);

        Assert.Equal(before, after);
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
        // A gate shorter than the 0.8 ms floor after its shoulder: every
        // bank window clamps to the full gate, the bank is one entry, and
        // the FDW curve must be the Fixed curve — values and blanked bins.
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
    public void DirectSoundWithALateReflection_FdwReadsTheDirectArrivalWhereFixedRipples()
    {
        // Direct sound plus a copy 6 ms later at −6 dB, through a 1/10/3 ms
        // gate. Fixed keeps both arrivals at every frequency and the
        // interference makes the group delay swing by milliseconds; FDW-8
        // holds the reflection only while its window is long enough to
        // reach it. With left = 1 ms the window is 1 + 8/f ms; the
        // reflection is fully outside it from f = 8 / (6 − 1) = 1.6 kHz
        // upward, so from 2 kHz — the first bank centre past that, with
        // the interpolation from the centre below it settled — the curve
        // is the direct arrival. Below 615 Hz the window clamps to the full
        // gate and both curves are one analysis.
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
        // One pair re-referenced to a different extraction start must read the
        // same ABSOLUTE arrival: the rotation moves the time origin, and the
        // time weight has to move with it (T gains ((s − s_ref) / fs) · H), or
        // the curve would shift by exactly the origin's move. Both directions:
        // a target before the extraction and one after it (where the arrival
        // wraps to the end of the circular buffer).
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
        // The bank's own invariant, stated through the public sum: two
        // extractions of one response at DIFFERENT starts, both windows
        // containing the whole response, added in one reference. Each half
        // reads the arrival on its own; the sum must too, with no step from
        // the differing starts.
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
    public void SumGatedSpectraPairs_SharedWindow_MatchesTheGateOverTheSummedImpulse()
    {
        // Two channels through ONE window (same offset, same FDW bank): the
        // group delay of the summed pairs must be the group delay of the
        // summed impulse through that window — the linearity the Virtual DSP
        // Sum rests on, for both members of the pair.
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
        // Two pure delays d1, d2 with energies e1, e2, each gated at its own
        // arrival (the Virtual DSP Auto placement) and summed. Per bin the
        // identity does NOT give (e1·d1 + e2·d2) / (e1 + e2): numerator and
        // energy both carry a √(e1·e2)·cos(ω·Δ) interference term, and the
        // ratio oscillates with it. The energy-weighted mean is what the
        // smoothing converges to once its kernel spans the ripple's period
        // 1/Δ: the FDW-8 floor is f/16 either side, so with Δ = 8 ms
        // (125 Hz period) the kernel holds two periods or more from 2 kHz up
        // and the Hann sidelobes leave the cross term at a few percent,
        // falling with frequency — a few hundredths of a millisecond on
        // Δ = 8 ms, against the 8 ms a time weight left behind at its own
        // extraction start would move the curve by.
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
    public void MinimumPhaseSystem_HasNearZeroExcessUnderFdw()
    {
        // h[n] = 0.9ⁿ is minimum-phase, and so is every rectangular
        // truncation of it (the truncation zeros sit at radius 0.9). Through
        // FDW-8 the window shrinks to 0.8 ms at the top of the band — still a
        // minimum-phase signal, whose measured delay the windowed magnitude
        // explains entirely: the excess reads ≈ 0 across the band.
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
        // The FDW excess is NOT the classical all-pass group delay by
        // construction: the arrival is read inside a window that changes with
        // frequency, while the minimum-phase part is the classical group delay
        // of the stitched magnitude's minimum-phase counterpart. Whether the
        // difference matters is an empirical question, and this is the
        // answer for the content a PEQ adds: three peaking bands at Q up to
        // 10, a bulk delay in front, the Phase tab's, the Group Delay tab's
        // and the Virtual DSP default gates with their Tukey fades, and every
        // cycle count. The excess reads the bulk delay to within a twentieth
        // of a millisecond across the band — the same order the Fixed gate
        // reads it to — so what the diagnostic calls "what no PEQ can touch"
        // stays true under FDW to that tolerance.
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
        // A steep high-pass rings for longer than any gate a junction uses,
        // and the gate's truncation of that ringing reads as excess at the low
        // edge under EVERY window — the FDW window is the whole gate there.
        // What FDW must not do is add to it: wherever both windows read a
        // value, the two excess curves agree to a few hundredths of a
        // millisecond. (The validity gates differ by a few bins at the
        // low-pass's stop-band edge, where FDW's wider smoothing floor keeps
        // a bin the Fixed gate blanks — those bins are left out.)
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
        // And the truncated ringing IS there to be seen under both: the low
        // edge reads a millisecond of excess against the flat band above.
        double lowEdge = NearestY(fixedCurves.Excess!, 101.0);
        double midBand = NearestY(fixedCurves.Excess!, 1_000.0);
        Assert.True(lowEdge - midBand > 0.5, $"no low-edge excess ({lowEdge - midBand:0.000} ms)");
    }

    [Fact]
    public void AllPass_DispersionLandsInExcessNotMinimumUnderFdw()
    {
        // A second-order all-pass at 1 kHz (Q = 2) through FDW-8: its ringing
        // is gone well inside the 8 ms window at 1 kHz, so |H| stays flat, the
        // minimum curve stays ≈ 0 and the pile-up at the corner lands in the
        // excess, as it does under the Fixed gate.
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
        // One function serves the bank and the floor: on every bank centre it
        // returns the window that centre was analysed through; between
        // centres it never grows with frequency; and the floor it yields is
        // half the window's resolution — 62.5 Hz at 1 kHz for 8 cycles with
        // no shoulder and no clamp (0.5 · 48000 / 384).
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

        // Fixed is the degenerate bank: the full gate everywhere.
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
        // The bank's plan was lifted out of the original BuildFdwSpectrum into
        // FdwBankPlan so the smoothing floor could share its geometry. This is
        // that original walk, kept verbatim: the same centres, the same merge
        // of equal windows into the LAST centre they hold for, the same
        // shortest-window entry at Nyquist — or the phase view's spectra would
        // have moved without a test noticing.
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
        // A differencer's low end falls below the −60 dB backstop under either
        // window; the three curves must agree bin-exactly about what is blanked.
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

    // The whole record in one plateau from sample 0, the geometry the Fixed
    // group-delay tests analyse the minimum-phase and all-pass systems through.
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

    // y[n] = b0·x[n] + b1·x[n−1] + b2·x[n−2] + a1·y[n−1] + a2·y[n−2] — the
    // additive-feedback convention BiquadCoefficients documents.
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
