using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The sum behind the Virtual DSP hybrid view: each channel's gated spectrum
/// rescaled to a level that came from a different measurement, summed as phasors.
/// </summary>
/// <remarks>
/// It exists because the cheap alternative — adding the substituted magnitudes and
/// laying the channels' own summation loss on top — is valid only while the two
/// families of measurement agree about the RELATIVE levels of the channels. A loss
/// is a property of the levels it was measured at, and borrowing it across a
/// disagreement draws cancellation the summed channels cannot produce.
/// </remarks>
public sealed class GatedSubstitutedMagnitudeSumTests
{
    private const int Rate = 48_000;
    private const int Anchor = 4_000;

    /// <summary>
    /// Fed each channel's OWN level, the substitution gives back that channel's own
    /// contribution, so the result is the honest complex sum. Unlike an
    /// amplitude-sum identity this runs through the gate, the FFT and the phase.
    /// </summary>
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

        // Judged in LINEAR amplitude against the peak, never in decibels: inside a
        // cancellation notch both curves are numerically almost nothing, and the
        // ratio of two nothings is a large number of decibels about no disagreement
        // at all.
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
        // Not exact, and the residue is the input's own resolution: the substituted
        // levels arrive on the 1024-point display grid, so each bin is handed its
        // neighbourhood's level rather than its own. A spatial average — which is
        // what these levels always are in practice — carries nothing finer.
        Assert.True(worst < 0.02, $"worst disagreement {worst:P2} of the peak");
    }

    /// <summary>
    /// One channel alone, fed its own level: no summation involved, so this isolates
    /// the substitution itself from anything the addition of phasors does.
    /// </summary>
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

    /// <summary>
    /// A gain common to every channel factors straight out, which is what lets the
    /// set's single offset be applied to the finished curve rather than per channel.
    /// </summary>
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
            // Above the numerical floor, where a common gain has a level to move.
            if (double.IsFinite(plain[i].Y) && plain[i].Y > -100)
            {
                Assert.Equal(plain[i].Y + 6, lifted[i].Y, 4);
            }
        }
    }

    /// <summary>
    /// A channel told it has no level contributes nothing — whether that is a hole
    /// or a silence is the caller's to decide, since only it knows what the channel
    /// was doing there.
    /// </summary>
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

    // Two smoothly decaying bursts of the SAME polarity and different decay rates,
    // placed deep enough in the record that the steady-state window's 5 ms lead-in
    // fits ahead of them. That is not a detail: with the burst near the start the
    // window opens before the record does, the extraction comes back mostly empty,
    // and every curve in the test — the honest one included — turns into a floor
    // with occasional numerical spikes.
    //
    // Same polarity is deliberate too: an opposed pair of near-equal channels makes
    // the sum a difference of two similar things, and the test would then measure how
    // ill-conditioned that subtraction is rather than how faithful the substitution
    // is. They still sit 17 samples apart, so the phase has real work to do.
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
