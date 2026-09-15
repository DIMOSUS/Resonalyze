using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>Hybrid-view sum: each gated spectrum rescaled to a level from another measurement, summed as phasors.</summary>
/// <remarks>Borrowing a summation loss across measurements that disagree on relative levels draws impossible cancellation.</remarks>
public sealed class GatedSubstitutedMagnitudeSumTests
{
    private const int Rate = 48_000;
    private const int Anchor = 4_000;

    [Fact]
    public void FedTheChannelsOwnLevels_ItReproducesTheirComplexSum()
    {
        Complex[] low = Ir(0, 1.0, 40);
        Complex[] high = Ir(17, 0.6, 18);
        PhaseAnalysisSettings gate = Gate();

        List<SignalPoint> honest = Own(Add(low, high), gate);
        List<SignalPoint> substituted = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [(Measure(low), Own(low, gate)), (Measure(high), Own(high, gate))],
            gate,
            smoothingInverseOctaves: 0);

        Assert.Equal(honest.Count, substituted.Count);

        // Linear amplitude against the peak: inside a notch a dB ratio of two near-zeros is meaningless.
        double peak = 0;
        for (int i = 0; i < honest.Count; i++)
        {
            if (InBand(honest[i]))
            {
                peak = Math.Max(peak, DataHelper.DecibelsToAmplitude(honest[i].Y));
            }
        }

        int compared = 0;
        double worst = 0;
        for (int i = 0; i < honest.Count; i++)
        {
            if (!InBand(honest[i]) || !double.IsFinite(substituted[i].Y))
            {
                continue;
            }

            compared++;
            worst = Math.Max(
                worst,
                Math.Abs(DataHelper.DecibelsToAmplitude(honest[i].Y) -
                    DataHelper.DecibelsToAmplitude(substituted[i].Y)) / peak);
        }

        Assert.True(compared > 300, $"compared only {compared} points");
        // Residue from the 1024-point display grid the levels arrive on.
        Assert.True(worst < 0.02, $"worst disagreement {worst:P2} of the peak");
    }

    [Fact]
    public void OneChannelFedItsOwnLevel_ComesBackAsItself()
    {
        Complex[] only = Ir(0, 1.0, 40);
        PhaseAnalysisSettings gate = Gate();

        List<SignalPoint> honest = Own(only, gate);
        List<SignalPoint> substituted = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [(Measure(only), honest)], gate, smoothingInverseOctaves: 0);

        double worst = 0;
        int compared = 0;
        for (int i = 0; i < honest.Count; i++)
        {
            if (!InBand(honest[i]) || !double.IsFinite(substituted[i].Y))
            {
                continue;
            }

            compared++;
            worst = Math.Max(worst, Math.Abs(honest[i].Y - substituted[i].Y));
        }

        Assert.True(compared > 300, $"compared only {compared} points");
        Assert.True(worst < 0.5, $"worst disagreement {worst:0.00} dB");
    }

    /// <summary>A common gain factors out, so the set's offset can apply to the finished curve.</summary>
    [Fact]
    public void ACommonGain_MovesTheSumByExactlyThatMuch()
    {
        Complex[] low = Ir(0, 1.0, 40);
        Complex[] high = Ir(17, 0.6, 18);
        PhaseAnalysisSettings gate = Gate();

        List<SignalPoint> plain = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [(Measure(low), Own(low, gate)), (Measure(high), Own(high, gate))],
            gate,
            smoothingInverseOctaves: 0);
        List<SignalPoint> lifted = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [
                (Measure(low), Lift(Own(low, gate), 6)),
                (Measure(high), Lift(Own(high, gate), 6))
            ],
            gate,
            smoothingInverseOctaves: 0);

        for (int i = 0; i < plain.Count; i++)
        {
            if (double.IsFinite(plain[i].Y) && plain[i].Y > -100)
            {
                Assert.Equal(plain[i].Y + 6, lifted[i].Y, 4);
            }
        }
    }

    /// <summary>Whether no level is a hole or silence is the caller's to decide.</summary>
    [Fact]
    public void AChannelWithNoLevel_ContributesNothing()
    {
        Complex[] low = Ir(0, 1.0, 40);
        Complex[] high = Ir(17, 0.6, 18);
        PhaseAnalysisSettings gate = Gate();

        List<SignalPoint> alone = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [(Measure(low), Own(low, gate))],
            gate,
            smoothingInverseOctaves: 0);
        List<SignalPoint> withSilent = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [(Measure(low), Own(low, gate)), (Measure(high), Silent(Own(high, gate)))],
            gate,
            smoothingInverseOctaves: 0);

        for (int i = 0; i < alone.Count; i++)
        {
            if (double.IsFinite(alone[i].Y))
            {
                Assert.Equal(alone[i].Y, withSilent[i].Y, 10);
            }
        }
    }

    /// <summary>Known limitation, characterized not guarded: in a deep null the phase is noise but the substituted level is not.</summary>
    /// <remarks>Not asserted into a hole: on the owner's car no deep sum dip had a contributor phase from a bin 30 dB under its envelope.</remarks>
    [Fact]
    public void APhaseTakenFromADeepNull_StaysInsideTheInterferenceWindow()
    {
        Complex[] plain = Ir(0, 1.0, 40);

        var nulled = new Complex[32_768];
        nulled[Anchor] = 1.0;
        nulled[Anchor + 24] = -1.0;

        PhaseAnalysisSettings gate = Gate();
        List<SignalPoint> plainLevel = Own(plain, gate);

        List<SignalPoint> strongLevel = plainLevel
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();

        List<SignalPoint> sum = DataHelper.GetGatedSubstitutedMagnitudeSum(
            [(Measure(plain), plainLevel), (Measure(nulled), strongLevel)],
            gate,
            smoothingInverseOctaves: 0);

        double worstAbove = double.NegativeInfinity;
        double worstBelow = double.PositiveInfinity;
        for (int i = 0; i < sum.Count && i < plainLevel.Count; i++)
        {
            if (!InBand(plainLevel[i]) || !double.IsFinite(sum[i].Y))
            {
                continue;
            }

            double relative = sum[i].Y - plainLevel[i].Y;
            worstAbove = Math.Max(worstAbove, relative);
            worstBelow = Math.Min(worstBelow, relative);
        }

        // Two equal contributions stay within [cancel, +6.02 dB] whatever the phase.
        Assert.True(
            worstAbove <= 6.03,
            $"sum reached {worstAbove:0.00} dB over one channel, above the +6.02 dB ceiling");
        Assert.True(worstBelow < 0, "the pair never interfered at all");
    }

    private static bool InBand(SignalPoint point) =>
        point.X is >= 100 and <= 10_000 && double.IsFinite(point.Y);

    private static PhaseAnalysisSettings Gate() => new(
        PhaseWindowMode.Fixed,
        FdwCycles: 0,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: Anchor * 1_000.0 / Rate,
        FrequencyResponseOptions.SteadyStateLeftMs,
        FrequencyResponseOptions.SteadyStatePlateauMs,
        FrequencyResponseOptions.SteadyStateRightMs,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    // Deep enough for the 5 ms lead-in (near the start the extraction comes back empty).
    // Same polarity: an opposed pair would measure ill-conditioned subtraction, not substitution.
    private static Complex[] Ir(int offset, double gain, double decaySamples)
    {
        var ir = new Complex[32_768];
        for (int i = 0; i < 240; i++)
        {
            ir[Anchor + offset + i] = gain * Math.Exp(-i / decaySamples);
        }

        return ir;
    }

    private static SyntheticMeasurement Measure(Complex[] ir) =>
        new(ir, Rate, Anchor);

    private static Complex[] Add(Complex[] a, Complex[] b)
    {
        var sum = new Complex[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
        }

        return sum;
    }

    private static List<SignalPoint> Own(Complex[] ir, PhaseAnalysisSettings gate) =>
        DataHelper.GetGatedPrimarySpectrumPair(
            Measure(ir), gate, calibration: null, smoothingInverseOctaves: 0)
            .Unsmoothed.Points.ToList();

    private static List<SignalPoint> Lift(List<SignalPoint> curve, double db) =>
        curve.Select(point => new SignalPoint(point.X, point.Y + db)).ToList();

    private static List<SignalPoint> Silent(List<SignalPoint> curve) =>
        curve.Select(point => new SignalPoint(point.X, double.NaN)).ToList();
}
