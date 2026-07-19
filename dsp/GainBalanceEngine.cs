using System.Numerics;
using System.Text;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// One channel's inputs to a gain-balance run: its identity, the processed
/// (chain-applied, so the CURRENT gain is baked in) impulse response the level
/// is measured from, the current gain to subtract back out, the band the
/// channel plays in (its crossover corners) and its side/pairing. A right-side
/// channel names its left counterpart so the balance can judge how consistent
/// the two sides' responses are across the shared band.
/// </summary>
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

/// <summary>
/// The gain proposal for one channel. A skipped channel (mono, no crossover,
/// band mostly below the localization floor) keeps its current gain and
/// carries the reason instead of a confidence.
/// </summary>
public sealed record GainBalanceResult(
    IAlignmentChannel Channel,
    bool Adjusted,
    string? SkipReason,
    double LevelDb,
    double ProposedGainDb,
    double SpreadDb,
    AlignmentConfidence? Confidence,
    string Detail);

/// <summary>
/// Cut-only gain balancing of the channels that carry the localized part of
/// the spectrum. Levels are 1/f-weighted band power means (energy per octave,
/// so narrow interference dips barely register and band width does not bias
/// the figure), measured at 0 dB gain by subtracting the current gain from the
/// chain-applied response. Every eligible left channel is levelled to one
/// target, every eligible right channel to that target plus the scene tilt
/// derived from the stereo scene offset; the shared target sits as high as the
/// cut-only constraint allows (the quietest channel ends at 0 dB of cut).
/// </summary>
public static class GainBalanceEngine
{
    /// <summary>
    /// The localization floor: gain balancing only touches drivers whose band
    /// meaningfully reaches above it — below, channel level is a bass-tuning
    /// decision (sub elevation), not an image/tonality one.
    /// </summary>
    public const double EligibilityFloorHz = 300;

    /// <summary>
    /// The minimum fraction of a channel's band (in OCTAVES — a linear-Hz
    /// fraction would qualify nearly everything) that must sit above the
    /// floor for its gain to be adjusted.
    /// </summary>
    public const double EligibilityMinOctaveFraction = 1.0 / 3.0;

    // The time-intensity trading anchor: a 0.27 ms scene offset (image at the
    // cabin center for a left-seated listener) maps to the near side sitting
    // 2 dB below the far side — ~135 µs/dB, inside the psychoacoustic trading
    // range. Interpolated linearly and clamped: the offset control reaches
    // ±5 ms, and extrapolating the trade that far would demand ±37 dB.
    public const double SceneTiltReferenceMs = 0.27;
    public const double SceneTiltReferenceDb = 2.0;
    public const double MaxSceneTiltDb = 6.0;

    // Confidence thresholds on the robust in-band spread of the smoothed
    // level (or of the L−R difference): a flat relation means the single
    // average genuinely represents the band; past several dB of swing the
    // average is an arbitrary compromise.
    public const double HighConfidenceMaxSpreadDb = 1.5;
    public const double LowConfidenceMinSpreadDb = 6.0;

    // The smoothing that precedes the spread estimate: symmetric 1/3-octave
    // power smoothing — wide enough to swallow the narrow interference dips
    // that sit at DIFFERENT frequencies on the two sides (an asymmetric,
    // peak-favouring smoother would leave residue in the difference), and
    // roughly the ear's critical bandwidth above ~500 Hz.
    private const double SmoothingHalfWidthOctaves = 1.0 / 6.0;
    private const double SpreadGridStepOctaves = 1.0 / 24.0;

    // Proposed cuts smaller than this land on exactly 0 dB, so a channel that
    // already sits at the target is reported as unchanged instead of -0.0.
    private const double MinimumCutDb = 0.05;

    /// <summary>
    /// The minimum grid samples a spread estimate needs before it means
    /// anything — a third of an octave of 1/24-octave points. Fewer reads as
    /// NaN (mapped to Low confidence), never as a perfectly stable
    /// measurement: an unmeasurably narrow band is the opposite of certainty.
    /// </summary>
    public const int MinimumSpreadSamples = 8;

    /// <summary>
    /// The intentional L/R level tilt (dB) for a stereo scene offset (ms):
    /// positive means the RIGHT side plays louder — the same "image toward
    /// the dash center for a left-seated listener" convention as the delay
    /// offset itself. Clamped to <see cref="MaxSceneTiltDb"/>.
    /// </summary>
    public static double SceneTiltDb(double sceneOffsetMs) =>
        Math.Clamp(
            sceneOffsetMs / SceneTiltReferenceMs * SceneTiltReferenceDb,
            -MaxSceneTiltDb,
            MaxSceneTiltDb);

    /// <summary>
    /// Why a channel's gain is left alone, or null when it is adjustable.
    /// </summary>
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

    /// <summary>
    /// Computes the cut-only gain proposal for every channel. The scene offset
    /// only matters when right-side channels participate; pass 0 for a
    /// single-side run.
    /// </summary>
    public static IReadOnlyList<GainBalanceResult> Compute(
        IReadOnlyList<GainBalanceInput> channels,
        double sceneOffsetMs,
        StringBuilder log)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(log);

        double tiltDb = channels.Any(input => input.RightSide)
            ? SceneTiltDb(sceneOffsetMs)
            : 0;
        log.AppendLine(
            $"Gain balance: scene tilt {tiltDb:+0.00;-0.00} dB " +
            "(positive: right side louder), cut-only");

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

        // A stereo pair is balanced as a pair or not at all: adjusting one
        // side while its twin is skipped silently breaks the promised L/R
        // relation for that pair — the adjusted side would chase a tilt its
        // twin cannot follow (cut-only forbids the compensating boost).
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

        // One shared left target as high as cut-only allows: every eligible
        // channel votes with the target the OTHERS would need if it became
        // the quietest — a right channel's vote arrives tilt-adjusted so the
        // final relation (right = left + tilt) holds without a boost anywhere.
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

    /// <summary>
    /// The 1/f-weighted power mean of a band in dB: with the weight, every
    /// octave contributes equally (energy per octave), so a wide band is not
    /// dominated by its numerous high-frequency bins; the POWER mean itself
    /// is what lets narrow interference dips vanish — they hold almost no
    /// energy. NaN when the band contains no bins.
    /// </summary>
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

    /// <summary>
    /// The band's smoothed level curve on a 1/24-octave grid, each point a
    /// plain power mean over ±1/6 octave (symmetric 1/3-octave smoothing).
    /// Feeds the spread estimates; points whose window holds no bins are
    /// skipped.
    /// </summary>
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

    /// <summary>
    /// A robust spread estimate (σ-equivalent) of a dB curve: the
    /// interquartile range scaled by the normal-distribution factor, so a
    /// single surviving outlier cannot dominate the figure. NaN below
    /// <see cref="MinimumSpreadSamples"/> — too few points to judge is NOT
    /// the same as perfectly stable.
    /// </summary>
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

    /// <summary>
    /// Maps an in-band spread to the qualitative confidence the report shows:
    /// a flat relation means the single average genuinely represents the band.
    /// </summary>
    public static AlignmentConfidence ConfidenceOf(double spreadDb) =>
        double.IsNaN(spreadDb) || spreadDb >= LowConfidenceMinSpreadDb
            ? AlignmentConfidence.Low
            : spreadDb <= HighConfidenceMaxSpreadDb
                ? AlignmentConfidence.High
                : AlignmentConfidence.Medium;

    // The spread the confidence is judged by: a right channel with an
    // adjustable left peer measures the L−R difference across the bands'
    // intersection — the actual quantity its gain equalizes. When that
    // intersection cannot be measured the confidence honestly reads
    // unavailable (NaN -> Low) rather than silently falling back to the
    // channel's own flatness, which says nothing about the L/R relation. A
    // channel without a measured peer judges its own in-band flatness (how
    // well-defined "the channel's level" is at all).
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

                // Invariant: the detail feeds the user report, which must
                // read the same regardless of the OS locale.
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

    // One point of the smoothed curve: the plain power mean over ±1/6 octave
    // around the center. The window is narrow enough that the 1/f weight
    // inside it would change nothing measurable.
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

    // The one-sided power spectrum of the processed IR, DC..Nyquist. The
    // search crops are power-of-two already; anything else is zero-padded up.
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
