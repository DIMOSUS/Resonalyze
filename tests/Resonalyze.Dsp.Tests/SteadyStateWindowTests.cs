using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

// The steady-state magnitude window: one definition in milliseconds for every
// magnitude curve the Virtual DSP tool and the EQ Wizard draw, realized in samples
// with the same clamp-and-trim the gated carve applies (ResolveGatePlacement).
public sealed class SteadyStateWindowTests
{
    [Fact]
    public void At48k_TheFullWindowFits()
    {
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(48_000);

        // 2 + 500 + 180 ms at 48 kHz — under the 32768-sample FFT, so nothing trims.
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
        // 682 ms outruns the FFT above 48 kHz, and what the clamp does with the
        // shortfall is the whole question. Trimming the fade alone spends all of it
        // on the plateau — at 192 kHz that left ZERO plateau, a window that faded in
        // and immediately out. The loss is shared instead, so the window keeps its
        // shape: a real unity plateau with a fade-out a fraction of it.
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        int plateau = window - left - right;

        Assert.Equal(DataHelper.GatedFftLength, window);
        Assert.True(
            plateau > right,
            $"at {sampleRate} Hz the plateau is {plateau} samples against a " +
            $"{right}-sample fade-out — the window is mostly fade");
        // The 500:180 ratio the constants ask for, kept within rounding.
        Assert.InRange((double)plateau / right, 2.3, 3.3);
        // Still a steady-state window: ~171 ms at the worst rate, dozens of times
        // the junction gate it replaced.
        Assert.True(window * 1_000.0 / sampleRate > 150);
    }

    [Fact]
    public void TheTrimIsSharedByTheGatedAndPlainPaths()
    {
        // Both ways to a windowed spectrum realize ONE geometry: the plain
        // oversampled window asks for it here, and the gated carve
        // (ResolveGatePlacement) asks the same helper. They cannot drift.
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(192_000);
        (int trimWindow, int trimLeft, int trimRight) =
            FrequencyResponseOptions.TrimGateToFft(384, 96_000, 34_560);

        Assert.Equal((window, left, right), (trimWindow, trimLeft, trimRight));
    }

    [Fact]
    public void AGateShorterThanTheFft_PassesThroughUntouched()
    {
        // Every phase gate is far shorter than the FFT, so the shared trim must be a
        // no-op for them — the fix must not have moved the phase view's window.
        (int window, int left, int right) =
            FrequencyResponseOptions.TrimGateToFft(24, 192, 72);

        Assert.Equal(288, window);
        Assert.Equal(24, left);
        Assert.Equal(72, right);
    }

    // What the window actually delivers on the hardest realistic band — a Q 8 bell at
    // 60 Hz, whose ringing needs ~290 ms to decay 60 dB. The tolerance is per rate
    // BECAUSE the carve clamp is in SAMPLES: the window is the full 682 ms at 48 kHz
    // (exact, under a hundredth of a dB) and shortens to 341 and 171 ms as the rate
    // doubles, so the deepest band is read progressively short — measured 0.60 dB at
    // 96 kHz and 1.34 dB at 192 kHz. Pinned so a change to the constants, the clamp
    // or the carve cannot move them unnoticed; the root fix is a rate-scaled gated
    // FFT, which is DSP-core work of its own.
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

        // Against the ideal filter's own depth at its centre — the response the DSP
        // realizes and the ear hears.
        Assert.True(
            Math.Abs(windowed - ideal) < toleranceDb,
            $"at {sampleRate} Hz the window read {windowed:0.00} dB against the " +
            $"filter's {ideal:0.00} dB");
    }

    [Fact]
    public void ThePlainWindowOpensOnTheResponseStart_NotItsPeak()
    {
        // The Passat woofer defect, synthetic: a low-frequency driver's envelope
        // peaks MILLISECONDS after its onset (group delay), while the plain
        // window's fade-in is only 2 ms — anchored on the peak it opened after
        // the response had begun and read the record minus its direct arrival.
        // The plain path must anchor on the estimated START instead, i.e. read
        // the same curve as the gated carve placed at that start.
        const int sampleRate = 48_000;
        const int onset = 960; // 20 ms
        var impulse = new Complex[65_536];
        int peak = 0;
        for (int i = 0; onset + i < impulse.Length; i++)
        {
            double t = i / (double)sampleRate;
            // A 60 Hz burst whose envelope rises over ~3 ms and decays over
            // ~80 ms: the envelope maximum lands ~10 ms after the onset.
            impulse[onset + i] = Math.Sin(2 * Math.PI * 60 * t) *
                (1 - Math.Exp(-t / 0.003)) * Math.Exp(-t / 0.080);
            if (impulse[onset + i].Magnitude > impulse[peak].Magnitude)
            {
                peak = onset + i;
            }
        }

        var measurement = new WindowMeasurement(impulse, peak, sampleRate);
        int anchor = TransferIrStartCache.ResolveStartIndex(
            impulse, sampleRate, peak);
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        double toMs = 1_000.0 / sampleRate;

        // The fixture parts the two anchors by more than the fade-in — without
        // that, peak and start anchoring would read the same and prove nothing.
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

        // Judged where the fixture has content; elsewhere both read noise floor.
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

        // And the anchor matters: at the band centre the peak-anchored window
        // reads a different level — the reading the bug reports showed.
        double at60Start = AtHz(atStart, 60);
        double at60Peak = AtHz(atPeak, 60);
        Assert.True(
            Math.Abs(at60Start - at60Peak) > 0.5,
            $"fixture: start- and peak-anchored windows read within " +
            $"{Math.Abs(at60Start - at60Peak):0.000} dB of each other at 60 Hz");
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
        // The claim that justifies the whole change, at the rate where the steady-state
        // window is WEAKEST (192 kHz, clamped to 171 ms): even there it reads a deep
        // high-Q bass band far closer to the truth than the old junction gate did.
        // Without this, a future clamp could quietly shrink the window back toward the
        // gate and every per-rate tolerance above would still pass.
        const int rate = 192_000;
        var bank = new EqualizationCurve(new[] { new PeqBand(60, 8, -8) });
        Complex[] impulse = UnitImpulse(rate, out int peak);

        (double steady, double ideal) = ReadBandDepth(impulse, peak, rate, bank);
        (double junction, _) = ReadBandDepth(
            impulse, peak, rate, bank, gateMs: (0.5, 4.0, 1.5));

        double steadyError = Math.Abs(steady - ideal);
        double junctionError = Math.Abs(junction - ideal);
        // Measured: 1.34 dB against 7.23 dB. The assertion is the GAP rather than a
        // ratio — what matters is how many dB of the band's depth the reader would
        // miss, and the ratio flatters a window that is merely less bad.
        Assert.True(
            junctionError - steadyError > 4.0,
            $"steady-state window off by {steadyError:0.00} dB, junction gate by " +
            $"{junctionError:0.00} dB — the gap has closed");
    }

    // Which chain stages actually move the GATED magnitude — measured through the real
    // filter → window → FFT path, not argued from the ideal transfer function, and
    // sampled across the ranges the UI allows — delay at 2/10/25/50/100 ms, all-pass
    // at 10/40/120/2000 Hz with Q 1/5/10/20. A sample, not a proof of the true
    // maximum: enough to decide the policy (both stages clearly move the curve at the
    // clamped rate) without claiming the extremum has been found. It is the evidence
    // behind the
    // EQ Wizard handoff's return policy — a bank is refused when what it was fitted to
    // has moved — and this PR has already been wrong twice by reasoning from |H| alone
    // and then by measuring too narrow a case, so the policy is held to the sweep.
    //
    // The window is the handoff's: frozen at handoff time, which is what makes a DELAY
    // edit matter — the response slides under a window that does not move.
    [Theory]
    [InlineData(48_000, ChainEdit.Delay, 0.01)]
    [InlineData(48_000, ChainEdit.AllPass, 0.40)]
    // At 192 kHz the window is clamped to 171 ms, and both stages then move the
    // reading by dB: this is why neither is allowed to change under an open handoff.
    [InlineData(192_000, ChainEdit.Delay, 2.00)]
    [InlineData(192_000, ChainEdit.AllPass, 5.20)]
    public void DelayAndAllPass_MoveTheGatedCurve_AtTheLimitsTheUiAllows(
        int sampleRate, ChainEdit edit, double boundDb)
    {
        double worst = WorstOverSweep(sampleRate, edit);

        // An upper bound, so a regression that made things WORSE is caught...
        Assert.True(
            worst < boundDb,
            $"{edit} at {sampleRate} Hz moved the gated curve by {worst:0.000} dB");

        // ...and, at the clamped rate, a lower one: the guard that refuses these edits
        // is only justified while they really do move the curve. If this ever stops
        // being true (a rate-scaled FFT would do it), the policy should be revisited
        // rather than kept out of habit.
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
        // The single chain stage the handoff lets change under it, and the reason is
        // exact rather than empirical: |-x·w| = |x·w| for any window at all.
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
        // The clearest member of the refused class, at the rate where everything else
        // is quietest: even with the full 682 ms window a moved corner is worth many
        // dB, so the distinction between "refuse" and "allow" can never come down to
        // measurement noise.
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
        Peq: null,
        AllPass: new AllPassSpec(AllPassType.SecondOrder, 300, 1.0));

    // The worst the edit can do anywhere in the range the UI offers — the figure the
    // policy needs, rather than one convenient setting's.
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

        // 20 is VirtualCrossoverChannelSettings.MaximumAllPassQ — the UI's own
        // ceiling, in the app project this one cannot reference.
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
                            AllPass = new AllPassSpec(AllPassType.SecondOrder, hz, q)
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

    // The largest SHAPE difference the two chains produce through one frozen gate,
    // over 20 Hz..20 kHz and within 30 dB of the curve's own peak (a deep null turns
    // a hair of complex difference into tens of dB and answers a different question).
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
                VirtualCrossoverAnalysis.ApplyChain(impulse, chain, sampleRate),
                peak,
                sampleRate),
            gate,
            calibration: null,
            smoothingInverseOctaves: 0);

    // A decaying wavelet with a room tail, as a measured channel really carries —
    // the tail is what a window can cut, so a bare impulse would flatter the result.
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

    // A bare unit impulse: with a flat source the windowed reading IS the filter's
    // own response, so any gap from the ideal magnitude is the window's doing and
    // nothing else's.
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
            impulse, new DspChannelChain(Peq: bank), sampleRate);
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
