using System.Numerics;
using System.Text;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>The impulse response is chain-applied (current gain baked in); a right channel names its left counterpart.</summary>
public sealed record GainBalanceInput(
    IAlignmentChannel Channel,
    Complex[] ImpulseResponse,
    int SampleRate,
    double CurrentGainDb,
    double BandLowHz,
    double BandHighHz,
    bool HasCrossover,
    bool Mono,
    bool RightSide,
    IAlignmentChannel? LeftPeer = null);

public sealed record GainBalanceResult(
    IAlignmentChannel Channel,
    bool Adjusted,
    string? SkipReason,
    double LevelDb,
    double ProposedGainDb,
    double SpreadDb,
    AlignmentConfidence? Confidence,
    string Detail);

/// <summary>Cut-only gain balancing on 1/f-weighted band power (energy per octave), measured at 0 dB gain.
/// Left channels share one target, right channels that target minus the requested L-R difference; the quietest ends at 0 dB cut.</summary>
public static class GainBalanceEngine
{
    /// <summary>Below this, channel level is a bass-tuning decision, not an imaging one.</summary>
    public const double EligibilityFloorHz = 300;

    /// <summary>Measured in octaves; a linear-Hz fraction would qualify nearly everything.</summary>
    public const double EligibilityMinOctaveFraction = 1.0 / 3.0;

    /// <summary>Past this a level difference is one side switched off; larger requests are clamped as typos.</summary>
    public const double MaxLevelDifferenceDb = 6.0;

    public const double HighConfidenceMaxSpreadDb = 1.5;
    public const double LowConfidenceMinSpreadDb = 6.0;

    // Symmetric 1/3-octave power smoothing swallows dips at different frequencies on the two sides (~critical band above 500 Hz).
    private const double SmoothingHalfWidthOctaves = 1.0 / 6.0;
    private const double SpreadGridStepOctaves = 1.0 / 24.0;

    // Smaller cuts snap to exactly 0 dB, so a channel already at target reads unchanged, not -0.0.
    private const double MinimumCutDb = 0.05;

    /// <summary>Fewer grid points read as NaN (Low confidence), never as perfectly stable.</summary>
    public const int MinimumSpreadSamples = 8;

    /// <summary>A dead input's noise floor reads 40+ dB down and, as the quietest, would drag every cut to it. Real drivers sit within a couple of tens of dB.</summary>
    public const double MaxLevelBelowLoudestDb = 30;

    /// <summary>Keeps proposals inside the validator's gain range; independent of the credibility gate.</summary>
    public const double MaxProposedCutDb = DspChannelChain.MaximumGainDb;

    /// <summary>LEFT minus RIGHT, clamped; non-finite reads as 0.</summary>
    public static double LevelDifferenceDb(double requestedDb) =>
        double.IsFinite(requestedDb)
            ? Math.Clamp(requestedDb, -MaxLevelDifferenceDb, MaxLevelDifferenceDb)
            : 0;

    public static string? SkipReason(
        double bandLowHz,
        double bandHighHz,
        bool hasCrossover,
        bool mono)
    {
        if (mono)
        {
            return "mono channel";
        }
        if (!hasCrossover)
        {
            return "no crossover";
        }
        if (bandHighHz <= bandLowHz)
        {
            return "empty band";
        }

        double octavesAboveFloor = bandHighHz <= EligibilityFloorHz
            ? 0
            : Math.Log2(bandHighHz / Math.Max(bandLowHz, EligibilityFloorHz));
        double bandOctaves = Math.Log2(bandHighHz / bandLowHz);
        if (octavesAboveFloor < bandOctaves * EligibilityMinOctaveFraction)
        {
            return $"band mostly below {EligibilityFloorHz:0} Hz";
        }

        return null;
    }

    /// <summary>Pass 0 for the L-R difference on a single-side run.</summary>
    public static IReadOnlyList<GainBalanceResult> Compute(
        IReadOnlyList<GainBalanceInput> channels,
        double levelDifferenceDb,
        StringBuilder log)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(log);

        double differenceDb = channels.Any(input => input.RightSide)
            ? LevelDifferenceDb(levelDifferenceDb)
            : 0;
        // Targets are relative to the left side, so the right offset is the negated L-R request.
        double tiltDb = -differenceDb;
        log.AppendLine(
            $"Gain balance: L-R level difference {differenceDb:+0.00;-0.00} dB " +
            "(positive: left side louder), cut-only");

        var spectra = new Dictionary<IAlignmentChannel, (double[] Power, double BinWidthHz)>();
        var levels = new Dictionary<IAlignmentChannel, double>();
        var reasons = new Dictionary<IAlignmentChannel, string?>();
        foreach (GainBalanceInput input in channels)
        {
            double[] power = PowerSpectrum(input.ImpulseResponse);
            double binWidthHz = input.SampleRate / (2.0 * (power.Length - 1));
            spectra[input.Channel] = (power, binWidthHz);
            double measured = WeightedBandLevelDb(
                power, binWidthHz, input.BandLowHz, input.BandHighHz);
            levels[input.Channel] = measured - input.CurrentGainDb;
            string? reason = SkipReason(
                input.BandLowHz, input.BandHighHz, input.HasCrossover, input.Mono);
            if (reason == null && double.IsNaN(levels[input.Channel]))
            {
                reason = "no energy in band";
            }

            reasons[input.Channel] = reason;
        }

        List<GainBalanceInput> credible = channels
            .Where(input => reasons[input.Channel] == null)
            .ToList();
        if (credible.Count > 0)
        {
            double loudestDb = credible.Max(input => levels[input.Channel]);
            foreach (GainBalanceInput input in credible)
            {
                double belowDb = loudestDb - levels[input.Channel];
                if (belowDb > MaxLevelBelowLoudestDb)
                {
                    reasons[input.Channel] = FormattableString.Invariant(
                        $"level {belowDb:0} dB below the loudest channel") +
                        " - looks like a dead capture";
                }
            }
        }

        // Pairs are balanced together or not at all: cut-only cannot let a skipped twin follow the tilt.
        foreach (GainBalanceInput right in channels)
        {
            if (!right.RightSide || right.LeftPeer == null)
            {
                continue;
            }

            GainBalanceInput? left = channels.FirstOrDefault(
                item => item.Channel == right.LeftPeer);
            if (left == null)
            {
                continue;
            }

            string? leftReason = reasons[left.Channel];
            string? rightReason = reasons[right.Channel];
            if (leftReason == null && rightReason != null)
            {
                reasons[left.Channel] = $"right side ineligible: {rightReason}";
            }
            else if (rightReason == null && leftReason != null)
            {
                reasons[right.Channel] = $"left side ineligible: {leftReason}";
            }
        }

        // Each eligible channel votes the target the others need if it were quietest (right votes tilt-adjusted), so no boost is needed.
        List<GainBalanceInput> eligible = channels
            .Where(input => reasons[input.Channel] == null)
            .ToList();
        double targetLeftDb = eligible.Count > 0
            ? eligible.Min(input =>
                levels[input.Channel] - (input.RightSide ? tiltDb : 0))
            : double.NaN;
        if (eligible.Count == 0)
        {
            log.AppendLine("  no eligible channels — gains left unchanged");
        }
        else
        {
            log.AppendLine(
                $"  target: left {targetLeftDb:0.00} dB, " +
                $"right {targetLeftDb + tiltDb:0.00} dB (at 0 dB gain)");
        }

        var results = new List<GainBalanceResult>(channels.Count);
        foreach (GainBalanceInput input in channels)
        {
            double levelDb = levels[input.Channel];
            string? reason = reasons[input.Channel];
            if (reason != null)
            {
                log.AppendLine(
                    $"  {input.Channel.Name}: skipped ({reason})" +
                    (double.IsNaN(levelDb) ? "" : $", level {levelDb:0.00} dB"));
                results.Add(new GainBalanceResult(
                    input.Channel, Adjusted: false, reason, levelDb,
                    input.CurrentGainDb, double.NaN, Confidence: null,
                    $"kept ({reason})"));
                continue;
            }

            double targetDb = input.RightSide ? targetLeftDb + tiltDb : targetLeftDb;
            double gainDb = Math.Min(0, Math.Round(targetDb - levelDb, 1));
            if (gainDb > -MinimumCutDb)
            {
                gainDb = 0;
            }
            if (gainDb < -MaxProposedCutDb)
            {
                // Unreachable while the credibility gate holds; the |GainDb| <= 60 invariant must not depend on it.
                log.AppendLine(
                    $"  {input.Channel.Name}: proposed cut clamped to " +
                    $"-{MaxProposedCutDb:0} dB (settings range)");
                gainDb = -MaxProposedCutDb;
            }

            (double spreadDb, string spreadDetail) = SpreadOf(input, channels, spectra);
            AlignmentConfidence confidence = ConfidenceOf(spreadDb);
            log.AppendLine(
                $"  {input.Channel.Name}: level {levelDb:0.00} dB " +
                $"in {input.BandLowHz:0}-{input.BandHighHz:0} Hz, " +
                $"gain {input.CurrentGainDb:0.0} -> {gainDb:0.0} dB, " +
                $"{spreadDetail} ({confidence})");
            results.Add(new GainBalanceResult(
                input.Channel, Adjusted: true, SkipReason: null, levelDb,
                gainDb, spreadDb, confidence, spreadDetail));
        }

        return results;
    }

    /// <summary>1/f-weighted power mean in dB: every octave counts equally and narrow dips vanish. NaN for an empty band.</summary>
    public static double WeightedBandLevelDb(
        IReadOnlyList<double> powerByBin,
        double binWidthHz,
        double lowHz,
        double highHz)
    {
        int first = Math.Max(1, (int)Math.Ceiling(lowHz / binWidthHz));
        int last = Math.Min(
            powerByBin.Count - 1, (int)Math.Floor(highHz / binWidthHz));
        double weightedPower = 0;
        double weightSum = 0;
        for (int bin = first; bin <= last; bin++)
        {
            double weight = 1.0 / (bin * binWidthHz);
            weightedPower += weight * powerByBin[bin];
            weightSum += weight;
        }

        return weightSum > 0 && weightedPower > 0
            ? 10.0 * Math.Log10(weightedPower / weightSum)
            : double.NaN;
    }

    public static List<double> SmoothedBandCurveDb(
        IReadOnlyList<double> powerByBin,
        double binWidthHz,
        double lowHz,
        double highHz)
    {
        var values = new List<double>();
        if (highHz <= lowHz)
        {
            return values;
        }

        int steps = (int)Math.Floor(
            Math.Log2(highHz / lowHz) / SpreadGridStepOctaves);
        for (int i = 0; i <= steps; i++)
        {
            double centerHz = lowHz * Math.Pow(2.0, i * SpreadGridStepOctaves);
            double value = SmoothedLevelAtDb(powerByBin, binWidthHz, centerHz);
            if (!double.IsNaN(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>IQR-based σ-equivalent spread; NaN below <see cref="MinimumSpreadSamples"/>.</summary>
    public static double RobustSpreadDb(IReadOnlyList<double> valuesDb)
    {
        if (valuesDb.Count < MinimumSpreadSamples)
        {
            return double.NaN;
        }

        List<double> sorted = valuesDb.OrderBy(value => value).ToList();
        double q25 = Percentile(sorted, 0.25);
        double q75 = Percentile(sorted, 0.75);
        return (q75 - q25) / 1.349;
    }

    public static AlignmentConfidence ConfidenceOf(double spreadDb) =>
        double.IsNaN(spreadDb) || spreadDb >= LowConfidenceMinSpreadDb
            ? AlignmentConfidence.Low
            : spreadDb <= HighConfidenceMaxSpreadDb
                ? AlignmentConfidence.High
                : AlignmentConfidence.Medium;

    // Right channel with a left peer: spread of L−R over the intersection (NaN if unmeasurable, no fallback to own flatness).
    private static (double SpreadDb, string Detail) SpreadOf(
        GainBalanceInput input,
        IReadOnlyList<GainBalanceInput> channels,
        IReadOnlyDictionary<IAlignmentChannel, (double[] Power, double BinWidthHz)> spectra)
    {
        if (input.RightSide && input.LeftPeer != null)
        {
            GainBalanceInput? peer = channels.FirstOrDefault(
                item => item.Channel == input.LeftPeer);
            if (peer != null)
            {
                double lowHz = Math.Max(input.BandLowHz, peer.BandLowHz);
                double highHz = Math.Min(input.BandHighHz, peer.BandHighHz);
                if (highHz <= lowHz)
                {
                    return (double.NaN, "no shared L-R band");
                }

                (double[] rightPower, double rightBinWidth) = spectra[input.Channel];
                (double[] leftPower, double leftBinWidth) = spectra[peer.Channel];
                var difference = new List<double>();
                int steps = (int)Math.Floor(
                    Math.Log2(highHz / lowHz) / SpreadGridStepOctaves);
                for (int i = 0; i <= steps; i++)
                {
                    double centerHz =
                        lowHz * Math.Pow(2.0, i * SpreadGridStepOctaves);
                    double left = SmoothedLevelAtDb(
                        leftPower, leftBinWidth, centerHz);
                    double right = SmoothedLevelAtDb(
                        rightPower, rightBinWidth, centerHz);
                    if (!double.IsNaN(left) && !double.IsNaN(right))
                    {
                        difference.Add(left - right);
                    }
                }

                double spread = RobustSpreadDb(difference);
                if (double.IsNaN(spread))
                {
                    return (spread, "shared L-R band too narrow to judge");
                }

                // Invariant culture: the report must not depend on OS locale.
                return (spread, FormattableString.Invariant(
                    $"L-R spread {spread:0.0} dB"));
            }
        }

        (double[] power, double binWidth) = spectra[input.Channel];
        List<double> curve = SmoothedBandCurveDb(
            power, binWidth, input.BandLowHz, input.BandHighHz);
        double own = RobustSpreadDb(curve);
        return double.IsNaN(own)
            ? (own, "band too narrow to judge")
            : (own, FormattableString.Invariant($"in-band spread {own:0.0} dB"));
    }

    private static double SmoothedLevelAtDb(
        IReadOnlyList<double> powerByBin,
        double binWidthHz,
        double centerHz)
    {
        double factor = Math.Pow(2.0, SmoothingHalfWidthOctaves);
        int first = Math.Max(1, (int)Math.Ceiling(centerHz / factor / binWidthHz));
        int last = Math.Min(
            powerByBin.Count - 1,
            (int)Math.Floor(centerHz * factor / binWidthHz));
        if (last < first)
        {
            return double.NaN;
        }

        double sum = 0;
        for (int bin = first; bin <= last; bin++)
        {
            sum += powerByBin[bin];
        }

        double mean = sum / (last - first + 1);
        return mean > 0 ? 10.0 * Math.Log10(mean) : double.NaN;
    }

    private static double Percentile(IReadOnlyList<double> sorted, double fraction)
    {
        double position = fraction * (sorted.Count - 1);
        int below = (int)Math.Floor(position);
        int above = Math.Min(sorted.Count - 1, below + 1);
        double weight = position - below;
        return sorted[below] * (1 - weight) + sorted[above] * weight;
    }

    private static double[] PowerSpectrum(Complex[] impulseResponse)
    {
        int length = DspMath.NextPowerOfTwo(impulseResponse.Length);
        var spectrum = new Complex[length];
        Array.Copy(impulseResponse, spectrum, impulseResponse.Length);
        Fourier.Forward(spectrum, FourierOptions.Matlab);
        var power = new double[length / 2 + 1];
        for (int bin = 0; bin < power.Length; bin++)
        {
            double real = spectrum[bin].Real;
            double imaginary = spectrum[bin].Imaginary;
            power[bin] = real * real + imaginary * imaginary;
        }

        return power;
    }
}
