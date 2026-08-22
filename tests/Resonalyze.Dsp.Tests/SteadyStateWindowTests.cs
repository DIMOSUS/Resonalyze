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
    // filter → window → FFT path, not argued from the ideal transfer function. It is
    // the evidence behind the EQ Wizard handoff's return policy (a bank is refused
    // when the magnitude it corrects has moved), and this PR has already been wrong
    // once by reasoning from |H| alone, so the policy is held to a measurement.
    //
    // The window is the handoff's: frozen at handoff time, which is what makes a later
    // DELAY edit interesting — the response slides under a window that does not move.
    [Theory]
    // Rate, edit, and the bound it must stay under (dB, shape only: a pure level
    // offset is removed first, because a bank corrects shape).
    [InlineData(48_000, ChainEdit.Gain, 0.01)]
    [InlineData(48_000, ChainEdit.Delay, 0.01)]
    [InlineData(48_000, ChainEdit.Polarity, 0.01)]
    [InlineData(48_000, ChainEdit.ExtremeAllPass, 0.01)]
    // At 192 kHz the window is clamped to 171 ms, and an all-pass at 60 Hz with Q 10
    // piles up ~106 ms of group delay — enough of the response to move past the
    // fade-out to shift the reading. Still under a tenth of the window's own error
    // for a comparable band (1.34 dB), which is why the policy stands; pinned so it
    // cannot grow unnoticed.
    [InlineData(192_000, ChainEdit.Gain, 0.01)]
    [InlineData(192_000, ChainEdit.Delay, 0.05)]
    [InlineData(192_000, ChainEdit.Polarity, 0.01)]
    [InlineData(192_000, ChainEdit.ExtremeAllPass, 0.60)]
    public void MagnitudeFlatChainStages_BarelyMoveTheGatedCurve(
        int sampleRate, ChainEdit edit, double boundDb)
    {
        var baseChain = new DspChannelChain(
            GainDb: -2,
            DelayMs: 0.8,
            InvertPolarity: false,
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            Peq: null,
            AllPass: new AllPassSpec(AllPassType.SecondOrder, 300, 1.0));
        DspChannelChain edited = edit switch
        {
            ChainEdit.Gain => baseChain with { GainDb = -8 },
            ChainEdit.Delay => baseChain with { DelayMs = 20.0 },
            ChainEdit.Polarity => baseChain with { InvertPolarity = true },
            _ => baseChain with
            {
                AllPass = new AllPassSpec(AllPassType.SecondOrder, 60, 10)
            }
        };

        double worst = WorstShapeShiftDb(sampleRate, baseChain, edited);

        Assert.True(
            worst < boundDb,
            $"{edit} at {sampleRate} Hz moved the gated curve by {worst:0.000} dB");
    }

    [Fact]
    public void ACrossoverEdit_MovesTheGatedCurveByFarMore()
    {
        // The other side of the same policy: the crossover is refused precisely
        // because it is not in the class above. Without this, tightening the bounds
        // there could quietly make the distinction meaningless.
        var baseChain = new DspChannelChain(
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)));
        DspChannelChain edited = baseChain with
        {
            Crossover = new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 900, 24),
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24))
        };

        Assert.True(WorstShapeShiftDb(48_000, baseChain, edited) > 5.0);
    }

    public enum ChainEdit
    {
        Gain,
        Delay,
        Polarity,
        ExtremeAllPass
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
