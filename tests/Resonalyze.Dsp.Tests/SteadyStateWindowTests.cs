using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

public sealed class SteadyStateWindowTests
{
    [Fact]
    public void At48k_TheFullWindowFits()
    {
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(48_000);

        // 2 + 500 + 180 ms at 48 kHz fits the 32768-sample FFT, so nothing trims.
        Assert.Equal(32_736, window);
        Assert.Equal(96, left);
        Assert.Equal(8_640, right);
    }

    [Theory]
    [InlineData(96_000)]
    [InlineData(176_400)]
    [InlineData(192_000)]
    public void AtHighRates_TheClampKeepsAPlateau_NotJustFades(int sampleRate)
    {
        // 682 ms outruns the FFT above 48 kHz; trimming only the fade left zero plateau at 192 kHz, so the loss is shared.
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        int plateau = window - left - right;

        Assert.Equal(DataHelper.GatedFftLength, window);
        Assert.True(
            plateau > right,
            $"at {sampleRate} Hz the plateau is {plateau} samples against a " +
            $"{right}-sample fade-out — the window is mostly fade");
        Assert.InRange((double)plateau / right, 2.3, 3.3);
        Assert.True(window * 1_000.0 / sampleRate > 150);
    }

    [Fact]
    public void TheTrimIsSharedByTheGatedAndPlainPaths()
    {
        // The plain window and the gated carve (ResolveGatePlacement) share one helper, so they cannot drift.
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(192_000);
        (int trimWindow, int trimLeft, int trimRight) =
            FrequencyResponseOptions.TrimGateToFft(384, 96_000, 34_560);

        Assert.Equal((window, left, right), (trimWindow, trimLeft, trimRight));
    }

    [Fact]
    public void AGateShorterThanTheFft_PassesThroughUntouched()
    {
        // Phase gates are far shorter than the FFT: the shared trim must be a no-op for them.
        (int window, int left, int right) =
            FrequencyResponseOptions.TrimGateToFft(24, 192, 72);

        Assert.Equal(288, window);
        Assert.Equal(24, left);
        Assert.Equal(72, right);
    }

    // Q 8 bell at 60 Hz (~290 ms ring). The clamp is in samples, so the window shortens to 341/171 ms at 96/192 kHz:
    // measured 0.60 / 1.34 dB. Root fix would be a rate-scaled gated FFT.
    [Theory]
    [InlineData(48_000, 0.05)]
    [InlineData(96_000, 0.70)]
    [InlineData(192_000, 1.40)]
    public void TheWindowResolvesADeepHighQBassBand(
        int sampleRate, double toleranceDb)
    {
        var bank = new EqualizationCurve(new[] { new PeqBand(60, 8, -8) });
        Complex[] impulse = UnitImpulse(sampleRate, out int peak);

        (double windowed, double ideal) = ReadBandDepth(impulse, peak, sampleRate, bank);

        Assert.True(
            Math.Abs(windowed - ideal) < toleranceDb,
            $"at {sampleRate} Hz the window read {windowed:0.00} dB against the " +
            $"filter's {ideal:0.00} dB");
    }

    [Fact]
    public void ThePlainWindowOpensOnTheResponseStart_NotItsPeak()
    {
        // LF envelope peaks ms after onset; a peak-anchored 2 ms fade-in dropped the direct arrival. Anchor on the start.
        const int sampleRate = 48_000;
        const int onset = 960; // 20 ms
        (Complex[] impulse, int peak) = RisingBurst(sampleRate, onset);

        var measurement = new WindowMeasurement(impulse, peak, sampleRate);
        int anchor = TransferIrStartCache.ResolveStartIndex(
            impulse, sampleRate, peak);
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        double toMs = 1_000.0 / sampleRate;

        // The anchors must differ by more than the fade-in, or the test proves nothing.
        Assert.True(
            peak - anchor > left,
            $"fixture: peak {peak} is only {peak - anchor} samples past the " +
            $"estimated start {anchor}, within the {left}-sample fade-in");

        var options = new FrequencyResponseOptions
        {
            Window = window,
            LeftTukeyWindow = left,
            RightTukeyWindow = right,
            SmoothingInverseOctaves = 0,
            UseCalibration = false
        };
        AnalysisCurve plain = DataHelper.GetPrimarySpectrum(
            measurement, options, calibration: null);
        AnalysisCurve atStart = Carved(measurement, anchor * toMs,
            left * toMs, (window - left - right) * toMs, right * toMs);
        AnalysisCurve atPeak = Carved(measurement, peak * toMs,
            left * toMs, (window - left - right) * toMs, right * toMs);

        for (int i = 0; i < plain.Points.Count; i++)
        {
            if (plain.Points[i].X is < 30 or > 120)
            {
                continue;
            }

            Assert.True(
                Math.Abs(plain.Points[i].Y - atStart.Points[i].Y) < 0.05,
                $"plain read {plain.Points[i].Y:0.000} dB at " +
                $"{plain.Points[i].X:0.#} Hz against the start-anchored carve's " +
                $"{atStart.Points[i].Y:0.000} dB");
        }

        double at60Start = AtHz(atStart, 60);
        double at60Peak = AtHz(atPeak, 60);
        Assert.True(
            Math.Abs(at60Start - at60Peak) > 0.5,
            $"fixture: start- and peak-anchored windows read within " +
            $"{Math.Abs(at60Start - at60Peak):0.000} dB of each other at 60 Hz");
    }

    [Fact]
    public void AnExplicitAnchorOverridesTheEstimator()
    {
        // A composite caller's anchor is where the window opens; the estimator could pick a later, louder arrival.
        const int sampleRate = 48_000;
        const int onset = 960;
        (Complex[] impulse, int peak) = RisingBurst(sampleRate, onset);
        var measurement = new WindowMeasurement(impulse, peak, sampleRate);
        int estimated = TransferIrStartCache.ResolveStartIndex(
            impulse, sampleRate, peak);
        Assert.NotEqual(peak, estimated); // the override must actually differ

        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        double toMs = 1_000.0 / sampleRate;
        var options = new FrequencyResponseOptions
        {
            Window = window,
            LeftTukeyWindow = left,
            RightTukeyWindow = right,
            SmoothingInverseOctaves = 0,
            UseCalibration = false
        };
        AnalysisCurve overridden = DataHelper.GetPrimarySpectrum(
            measurement, options, calibration: null, anchorIndex: peak);
        AnalysisCurve atPeak = Carved(measurement, peak * toMs,
            left * toMs, (window - left - right) * toMs, right * toMs);

        Assert.True(
            Math.Abs(AtHz(overridden, 60) - AtHz(atPeak, 60)) < 0.05,
            $"the explicit anchor read {AtHz(overridden, 60):0.000} dB against " +
            $"the same anchor's carve at {AtHz(atPeak, 60):0.000} dB");
        Assert.NotEqual(
            AtHz(atPeak, 60),
            AtHz(DataHelper.GetPrimarySpectrum(
                measurement, options, calibration: null), 60),
            precision: 1);
    }

    private static (Complex[] Impulse, int Peak) RisingBurst(
        int sampleRate, int onset)
    {
        var impulse = new Complex[65_536];
        int peak = 0;
        for (int i = 0; onset + i < impulse.Length; i++)
        {
            double t = i / (double)sampleRate;
            impulse[onset + i] = Math.Sin(2 * Math.PI * 60 * t) *
                (1 - Math.Exp(-t / 0.003)) * Math.Exp(-t / 0.080);
            if (impulse[onset + i].Magnitude > impulse[peak].Magnitude)
            {
                peak = onset + i;
            }
        }

        return (impulse, peak);
    }

    private static AnalysisCurve Carved(
        IImpulseMeasurement measurement,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs) =>
        DataHelper.GetGatedPrimarySpectrum(
            measurement,
            new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                gateOffsetMs,
                leftMs,
                plateauMs,
                rightMs,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            calibration: null,
            smoothingInverseOctaves: 0);

    private static double AtHz(AnalysisCurve curve, double hz)
    {
        SignalPoint best = curve.Points[0];
        foreach (SignalPoint point in curve.Points)
        {
            if (Math.Abs(point.X - hz) < Math.Abs(best.X - hz))
            {
                best = point;
            }
        }

        return best.Y;
    }

    [Fact]
    public void TheWindowBeatsTheJunctionGateItReplaced()
    {
        // At the weakest rate (192 kHz, 171 ms) the window must still beat a 0.5/4/1.5 ms junction gate (1.34 vs 7.23 dB).
        const int rate = 192_000;
        var bank = new EqualizationCurve(new[] { new PeqBand(60, 8, -8) });
        Complex[] impulse = UnitImpulse(rate, out int peak);

        (double steady, double ideal) = ReadBandDepth(impulse, peak, rate, bank);
        (double junction, _) = ReadBandDepth(
            impulse, peak, rate, bank, gateMs: (0.5, 4.0, 1.5));

        double steadyError = Math.Abs(steady - ideal);
        double junctionError = Math.Abs(junction - ideal);
        // Assert the gap, not a ratio: a ratio flatters a merely less-bad window.
        Assert.True(
            junctionError - steadyError > 4.0,
            $"steady-state window off by {steadyError:0.00} dB, junction gate by " +
            $"{junctionError:0.00} dB — the gap has closed");
    }

    // Measured through filter → window → FFT over the UI ranges: evidence for the EQ Wizard handoff refusing delay/all-pass edits.
    // The handoff window is frozen, so a delay slides the response under it.
    [Theory]
    [InlineData(48_000, ChainEdit.Delay, 0.01)]
    [InlineData(48_000, ChainEdit.AllPass, 0.40)]
    // At 192 kHz (171 ms window) both stages move the reading by dB.
    [InlineData(192_000, ChainEdit.Delay, 2.00)]
    [InlineData(192_000, ChainEdit.AllPass, 5.20)]
    public void DelayAndAllPass_MoveTheGatedCurve_AtTheLimitsTheUiAllows(
        int sampleRate, ChainEdit edit, double boundDb)
    {
        double worst = WorstOverSweep(sampleRate, edit);

        Assert.True(
            worst < boundDb,
            $"{edit} at {sampleRate} Hz moved the gated curve by {worst:0.000} dB");

        // Lower bound: if a rate-scaled FFT ever makes these edits harmless, revisit the refusal policy.
        if (sampleRate == 192_000)
        {
            Assert.True(
                worst > 1.0,
                $"{edit} now moves the curve only {worst:0.000} dB — the refusal may " +
                "no longer be earning its cost");
        }
    }

    [Theory]
    [InlineData(48_000)]
    [InlineData(192_000)]
    public void APolarityFlip_MovesNothing(int sampleRate)
    {
        // Exact, not empirical: |-x·w| = |x·w| for any window.
        var baseChain = BaseChain();

        Assert.Equal(
            0,
            WorstShapeShiftDb(
                sampleRate, baseChain, baseChain with { InvertPolarity = true }),
            10);
    }

    [Fact]
    public void ACrossoverEdit_MovesTheGatedCurveByFarMore()
    {
        DspChannelChain baseChain = BaseChain();
        DspChannelChain edited = baseChain with
        {
            Crossover = new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 900, 24),
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24))
        };

        Assert.True(WorstShapeShiftDb(48_000, baseChain, edited) > 5.0);
    }

    private static DspChannelChain BaseChain() => new(
        GainDb: -2,
        DelayMs: 0.8,
        InvertPolarity: false,
        Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
        Peq: new EqualizationCurve(new[]
        {
            new PeqBand(300, 1.0, 0, PeqBandType.AllPassSecondOrder)
        }));

    private static double WorstOverSweep(int sampleRate, ChainEdit edit)
    {
        DspChannelChain baseChain = BaseChain();
        double worst = 0;
        if (edit == ChainEdit.Delay)
        {
            foreach (double ms in new[] { 2.0, 10.0, 25.0, 50.0, 100.0 })
            {
                worst = Math.Max(
                    worst,
                    WorstShapeShiftDb(sampleRate, baseChain, baseChain with { DelayMs = ms }));
            }

            return worst;
        }

        // 20 = PeqSlotControl.MaximumQ (app project, not referenceable here).
        foreach (double q in new[] { 1.0, 5.0, 10.0, 20.0 })
        {
            foreach (double hz in new[] { 10.0, 40.0, 120.0, 2_000.0 })
            {
                worst = Math.Max(
                    worst,
                    WorstShapeShiftDb(
                        sampleRate,
                        baseChain,
                        baseChain with
                        {
                            Peq = new EqualizationCurve(new[]
                            {
                                new PeqBand(hz, q, 0, PeqBandType.AllPassSecondOrder)
                            })
                        }));
            }
        }

        return worst;
    }

    public enum ChainEdit
    {
        Delay,
        AllPass
    }

    // Within 30 dB of the curve's peak: a deep null turns a hair of complex difference into tens of dB.
    private static double WorstShapeShiftDb(
        int sampleRate, DspChannelChain before, DspChannelChain after)
    {
        int peak = sampleRate / 100;
        Complex[] impulse = DriverLikeImpulse(sampleRate, peak);
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        double toMs = 1_000.0 / sampleRate;
        var gate = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: (peak + before.DelayMs / 1_000.0 * sampleRate) * toMs,
            left * toMs,
            (window - left - right) * toMs,
            right * toMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        AnalysisCurve a = RenderChain(impulse, before, peak, sampleRate, gate);
        AnalysisCurve b = RenderChain(impulse, after, peak, sampleRate, gate);

        double peakDb = double.NegativeInfinity;
        foreach (SignalPoint point in a.Points)
        {
            if (point.X is >= 20 and <= 20_000 && double.IsFinite(point.Y))
            {
                peakDb = Math.Max(peakDb, point.Y);
            }
        }

        double sum = 0;
        int count = 0;
        for (int i = 0; i < a.Points.Count; i++)
        {
            if (Counts(a.Points[i], peakDb))
            {
                sum += b.Points[i].Y - a.Points[i].Y;
                count++;
            }
        }

        double levelOffset = count > 0 ? sum / count : 0;
        double worst = 0;
        for (int i = 0; i < a.Points.Count; i++)
        {
            if (Counts(a.Points[i], peakDb))
            {
                worst = Math.Max(
                    worst, Math.Abs(b.Points[i].Y - a.Points[i].Y - levelOffset));
            }
        }

        return worst;

        static bool Counts(SignalPoint point, double peakDb) =>
            point.X is >= 20 and <= 20_000 && point.Y > peakDb - 30;
    }

    private static AnalysisCurve RenderChain(
        Complex[] impulse,
        DspChannelChain chain,
        int peak,
        int sampleRate,
        PhaseAnalysisSettings gate) =>
        DataHelper.GetGatedPrimarySpectrum(
            new WindowMeasurement(
                VirtualCrossoverAnalysis.ApplyChain(impulse, chain, sampleRate, sampleRate),
                peak,
                sampleRate),
            gate,
            calibration: null,
            smoothingInverseOctaves: 0);

    // A room tail is what a window can cut; a bare impulse would flatter the result.
    private static Complex[] DriverLikeImpulse(int sampleRate, int peak)
    {
        var impulse = new Complex[sampleRate / 2];
        var random = new Random(11);
        int direct = sampleRate / 250;
        for (int i = 0; i < direct && peak + i < impulse.Length; i++)
        {
            impulse[peak + i] = Math.Exp(-i / (sampleRate / 2000.0)) *
                Math.Cos(2 * Math.PI * i / (sampleRate / 2000.0));
        }
        for (int i = direct; peak + i < impulse.Length; i++)
        {
            impulse[peak + i] = 0.05 * Math.Exp(-i / (sampleRate / 50.0)) *
                (random.NextDouble() * 2 - 1);
        }

        return impulse;
    }

    private static Complex[] UnitImpulse(int sampleRate, out int peak)
    {
        var impulse = new Complex[DataHelper.GatedFftLength * 2];
        peak = sampleRate / 100;
        impulse[peak] = 1.0;
        return impulse;
    }

    private static (double Windowed, double Ideal) ReadBandDepth(
        Complex[] impulse,
        int peak,
        int sampleRate,
        EqualizationCurve bank,
        (double Left, double Plateau, double Right)? gateMs = null)
    {
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        double toMs = 1_000.0 / sampleRate;
        (double leftMs, double plateauMs, double rightMs) = gateMs
            ?? (left * toMs, (window - left - right) * toMs, right * toMs);
        var gate = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: peak * toMs,
            leftMs,
            plateauMs,
            rightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        Complex[] filtered = VirtualCrossoverAnalysis.ApplyChain(
            impulse, new DspChannelChain(Peq: bank), sampleRate, sampleRate);
        AnalysisCurve flat = DataHelper.GetGatedPrimarySpectrum(
            new WindowMeasurement(impulse, peak, sampleRate), gate, null, 0);
        AnalysisCurve corrected = DataHelper.GetGatedPrimarySpectrum(
            new WindowMeasurement(filtered, peak, sampleRate), gate, null, 0);

        int centre = 0;
        double closest = double.MaxValue;
        for (int i = 0; i < flat.Points.Count; i++)
        {
            double distance = Math.Abs(flat.Points[i].X - 60);
            if (distance < closest)
            {
                closest = distance;
                centre = i;
            }
        }

        return (
            corrected.Points[centre].Y - flat.Points[centre].Y,
            DigitalEqualizationResponse.MagnitudeDbAt(
                bank, flat.Points[centre].X, sampleRate));
    }

    private sealed class WindowMeasurement : IImpulseMeasurement
    {
        public WindowMeasurement(Complex[] impulseResponse, int peakIndex, int sampleRate)
        {
            ImpulseResponse = impulseResponse;
            PeakIndex = peakIndex;
            SampleRate = sampleRate;
        }

        public Complex[]? ImpulseResponse { get; }
        public int PeakIndex { get; }
        public int SampleRate { get; }
        public double HarmonicIROffset(double harmonic) => 0;
    }
}
