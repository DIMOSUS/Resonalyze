using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>The pair's raw responses (one sample rate) and current chains; the tuner keeps everything except the two facing edges.</summary>
/// <param name="LowerMagnitude">The plant for a stated acoustic slope: the channel through its chain without the facing
/// edge and the PEQ, e.g. its spatial average. Null reads it off the impulse response.</param>
public sealed record JunctionTuneSide(
    string Name,
    Complex[] LowerImpulseResponse,
    DspChannelChain LowerChain,
    Complex[] UpperImpulseResponse,
    DspChannelChain UpperChain,
    int SampleRate,
    IReadOnlyList<SignalPoint>? LowerMagnitude = null,
    IReadOnlyList<SignalPoint>? UpperMagnitude = null);

/// <summary>What <c>driver × filter</c> should look like at a junction, not what the filter is.</summary>
public sealed record JunctionAcousticTarget(CrossoverFilterFamily Family, int SlopeDbPerOctave);

/// <summary>One side's shapes against the stated acoustic crossover. Charges are per channel, null where unread; slopes
/// are fitted alike for the asked and the achieved curves, so they compare with each other, not with a nameplate.</summary>
public sealed record JunctionAcousticFit(
    double? LowerChargeDb,
    double? UpperChargeDb,
    double? LowerSlopeDbPerOctave,
    double? UpperSlopeDbPerOctave,
    double? TargetSlopeDbPerOctave)
{
    public double ChargeDb => LowerChargeDb is { } lower && UpperChargeDb is { } upper
        ? 0.5 * (lower + upper)
        : LowerChargeDb ?? UpperChargeDb ?? 0;
}

public sealed record JunctionAcousticMiss(string Side, bool Upper, double ChargeDb);

/// <summary>Each channel's magnitude with the facing edge out of the chain; the level is arbitrary.</summary>
public sealed record JunctionPlant(
    IReadOnlyList<SignalPoint> Lower,
    IReadOnlyList<SignalPoint> Upper);

/// <summary>The drivers' own fall through the handover region, the facing edge taken out of the chain.</summary>
public sealed record JunctionDriverSlopes(
    string Side,
    double? LowerDbPerOctave,
    double? UpperDbPerOctave);

/// <summary><see cref="Slopes"/> null = every family slope at or above 12 dB/oct, or with an acoustic target every slope
/// the family has.</summary>
/// <param name="OneAlignmentForAllSides">Re-align every side by one shift, as a mono block's single delay requires.</param>
/// <param name="KeepMarginDb">Per-side score margin a challenger needs to replace the user's current crossover.</param>
/// <param name="TargetCurveDb">The EQ stage's target in dB (null = flat), taken out of the plant before shapes are read.</param>
/// <param name="SumSlackDb">Score a stated acoustic slope may cost against the best sum; it chooses only inside.</param>
public sealed record JunctionTuneOptions(
    IReadOnlyList<CrossoverFilterFamily> Families,
    IReadOnlyList<int>? Slopes,
    double MinCrossoverHz,
    double MaxCrossoverHz,
    bool IndependentSlopes,
    int ProcessorSampleRateHz,
    double KeepMarginDb = CrossoverJunctionTuner.DefaultKeepMarginDb,
    JunctionAcousticTarget? AcousticTarget = null,
    double SumSlackDb = CrossoverJunctionTuner.DefaultSumSlackDb,
    IReadOnlyList<SignalPoint>? TargetCurveDb = null,
    bool SplitCorners = false,
    bool OneAlignmentForAllSides = false);

/// <summary>Coherent sum over a band after re-alignment; lower score is better. Ripple includes the room's own.</summary>
public sealed record JunctionTuneReading(
    string Side,
    double LossDb,
    double DipDb,
    double RippleDb,
    JunctionAcousticFit? Acoustic = null)
{
    /// <summary>Loss, plus the dip's excess at half weight (as in the wizard post-check), plus ripple.</summary>
    public double ScoreDb =>
        -LossDb +
        CrossoverJunctionTuner.DipPenaltyWeight * (LossDb - DipDb) +
        CrossoverJunctionTuner.RippleWeight * RippleDb;
}

/// <summary>A null edge in the current candidate means the channel has no such edge today.</summary>
/// <param name="Sides">Readings on the candidate's own junction band: what the panel shows.</param>
/// <param name="RankingSides">Readings on the band all candidates share: what they are ranked on. See docs/tech/crossover-auto-setup.md#junction-tuner.</param>
public sealed record JunctionTuneCandidate(
    CrossoverEdge? LowerLowPass,
    CrossoverEdge? UpperHighPass,
    IReadOnlyList<JunctionTuneReading> Sides,
    IReadOnlyList<JunctionTuneReading> RankingSides,
    double BandLowHz,
    double BandHighHz)
{
    public double ScoreDb => Sides.Count == 0 ? double.PositiveInfinity : Sides.Average(side => side.ScoreDb);

    public double RankingScoreDb =>
        RankingSides.Count == 0 ? double.PositiveInfinity : RankingSides.Average(side => side.ScoreDb);

    public double? AcousticCostDb
    {
        get
        {
            List<double> read = RankingSides.Where(side => side.Acoustic != null)
                .Select(side => side.Acoustic!.ChargeDb)
                .ToList();
            return read.Count > 0 ? read.Average() : null;
        }
    }

    /// <summary>The channel furthest from the stated slope on any side: each channel's EQ aims at the goal by itself,
    /// so this, not the average, says whether the goal lands.</summary>
    public JunctionAcousticMiss? WorstAcousticChannel =>
        RankingSides
            .Where(side => side.Acoustic != null)
            .SelectMany(side => new[]
            {
                side.Acoustic!.LowerChargeDb is { } lower ? new JunctionAcousticMiss(side.Side, false, lower) : null,
                side.Acoustic.UpperChargeDb is { } upper ? new JunctionAcousticMiss(side.Side, true, upper) : null
            })
            .OfType<JunctionAcousticMiss>()
            .MaxBy(miss => miss.ChargeDb);

    public double? WorstAcousticCostDb => WorstAcousticChannel?.ChargeDb;

    /// <summary>Every channel of every side read against the stated slope: an unread one is unknown, not a landing.</summary>
    public bool AcousticReadInFull =>
        RankingSides.Count > 0 &&
        RankingSides.All(side => side.Acoustic is { LowerChargeDb: not null, UpperChargeDb: not null });

    public bool AcousticGoalLands =>
        AcousticReadInFull && CrossoverJunctionTuner.WasAcousticTargetReached(WorstAcousticCostDb);
}

/// <summary>The junction after the delay production alignment would pick for the upper channel.</summary>
/// <param name="InvertUpper">The resulting polarity, not a flip of the current one.</param>
public sealed record JunctionTuneAlignment(
    string Side,
    double ExtraDelayMs,
    bool InvertUpper,
    double LossDb,
    double DipDb);

/// <param name="Changed">Best beats Current by the keep margin on the shared band and reads no worse on its own band.</param>
/// <param name="DriverSlopes">Read at the winner's corner; empty unless an acoustic slope was stated.</param>
/// <param name="ClosestAcousticCostDb">The least worst-channel miss over the whole lattice, before the corridor: what
/// this search space (window, families, slopes) could do, not what the drivers could.</param>
/// <param name="BestSumScoreDb">The best ranking score on the lattice: what a stated goal is paid for against.</param>
public sealed record JunctionTuneResult(
    JunctionTuneCandidate Current,
    JunctionTuneCandidate Best,
    bool Changed,
    IReadOnlyList<JunctionTuneCandidate> RunnersUp,
    IReadOnlyList<JunctionTuneAlignment> CurrentAfterDelay,
    IReadOnlyList<JunctionTuneAlignment> BestAfterDelay,
    int CandidatesEvaluated,
    double RankingBandLowHz,
    double RankingBandHighHz,
    IReadOnlyList<JunctionDriverSlopes> DriverSlopes,
    double? ClosestAcousticCostDb = null,
    double? BestSumScoreDb = null)
{
    /// <summary>Whether Best differs from the crossover on screen, won or not: what an explicit Apply changes.</summary>
    public bool Moves =>
        !(Current.LowerLowPass.Equals(Best.LowerLowPass) && Current.UpperHighPass.Equals(Best.UpperHighPass));
}

public sealed record JunctionProbeChains(DspChannelChain Lower, DspChannelChain Upper);

/// <summary>The variant carrying the chains as they stand is the baseline.</summary>
public sealed record JunctionProbeVariant(string Label, IReadOnlyList<JunctionProbeChains> Sides);

/// <summary>Read-only. <paramref name="SharedBandSides"/> is the only reading comparable across differing corners;
/// <paramref name="AfterDelay"/> re-runs alignment for this variant, since the tune's delays were set for the current chains.</summary>
public sealed record JunctionProbeEntry(
    string Label,
    CrossoverEdge? LowerLowPass,
    CrossoverEdge? UpperHighPass,
    double CornerHz,
    IReadOnlyList<JunctionTuneReading> Sides,
    IReadOnlyList<JunctionTuneReading> SharedBandSides,
    IReadOnlyList<JunctionTuneAlignment> AfterDelay,
    IReadOnlyList<JunctionProbePhase> Phase,
    double BandLowHz,
    double BandHighHz,
    string? Unavailable);

/// <summary>Comparable between entries of one probe only, not with the panel's gated junction phase. Null where phase is inconsistent.</summary>
public sealed record JunctionProbePhase(string Side, JunctionPhaseResult? Result);

public sealed record JunctionProbeResult(
    IReadOnlyList<JunctionProbeEntry> Entries,
    double SharedBandLowHz,
    double SharedBandHighHz);

/// <param name="InvertUpper">The resulting polarity, as in <see cref="JunctionTuneAlignment"/>.</param>
public sealed record JunctionDelayProbeCandidate(
    double ExtraDelayMs,
    bool InvertUpper,
    double ScoreDb,
    double LossDb,
    double DipDb,
    bool Chosen);

public sealed record JunctionDelayProbeSide(
    string Side,
    double BandLowHz,
    double BandHighHz,
    double SearchHalfWindowMs,
    IReadOnlyList<JunctionDelayProbeCandidate> Candidates,
    string? Unavailable);

/// <summary>Tunes one junction's facing edges on the coherent sum through the full current chains; deliberately not the wizard's
/// objective. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
public static class CrossoverJunctionTuner
{
    public const double DefaultKeepMarginDb = 0.5;
    public const double DipPenaltyWeight = 0.5;
    public const double RippleWeight = 1.0;

    // The acoustic slope target's constants; their provenance is in docs/tech/crossover-auto-setup.md#acoustic-slope-target.
    public const double DefaultSumSlackDb = 0.2;

    /// <summary>What the stated slope must gain, at the worst channel, before the crossover on screen is rewritten.</summary>
    public const double AcousticKeepMarginDb = 1.0;

    public const double AcousticReachedCostDb = 2.0;

    /// <summary>Softer than asked costs a quarter: a cut lands it, a skirt boost is what the EQ stage refuses.</summary>
    public const double AcousticSofterChargeFactor = 0.25;

    /// <summary>Slack before a driver's own fall counts as steeper than asked: both are line fits of curves.</summary>
    public const double SlopeReachToleranceDbPerOctave = 2.0;

    private const double AcousticChargeFloorDb = 24.0;

    private const double AcousticWindowOctaves = 2.0;

    private const int AcousticMinimumBins = 3;

    /// <summary>A wider step between plant points is a hole in the reading, not its spacing.</summary>
    private const double MaxWeightSpanOctaves = 1.0 / 3.0;

    /// <summary>Share of the charged region's weight dropped, worst first: a notch that narrow is the seat's.</summary>
    private const double AcousticTrimmedWeight = 0.2;

    public const int PracticalSlopeFloorDbPerOctave = 12;

    public const int RunnersUpReported = 3;

    /// <summary>Leading candidates the split-corner pass refines; each costs the whole offset ladder.</summary>
    public const int SplitRefinements = 4;

    public const int DelayProbeCandidatesReported = 5;

    // Sized to the ranking band's gate (~9 periods of its low edge plus delay and fades), capped at the post-check length.
    private const int MaxCropLength = 32_768;
    private const int MinCropLength = 8_192;
    private const int CropPrePeakSamples = 4_096;
    private const double CropGatePeriods = 10.0;
    private const double CropMarginSeconds = 0.08;

    // Thinned to 1/24 octave: each probe costs two chains and two gated FFTs per side.
    private static readonly double MinProbeRatio = Math.Pow(2.0, 1.0 / 24.0);

    /// <summary>Headroom over the ranking band's top that the ranking reads are decimated to. The gate is fixed in
    /// TIME (about nine periods of the band's low edge), so the FFT length is the measurement rate times that gate —
    /// and for a sub junction almost all of that rate is bandwidth the read throws away. Four times the band top
    /// leaves the anti-alias filter a full octave of transition and keeps every junction band inside Nyquist.</summary>
    private const double DecimationBandHeadroom = 4.0;

    // Below this there is nothing worth the filter: at a tweeter junction the band already fills the rate.
    private const int MinimumDecimationFactor = 2;

    // Half-length of the windowed-sinc anti-alias kernel, per output sample. 32 puts the stopband below -90 dB.
    private const int DecimationKernelHalfLength = 32;

    public static JunctionTuneResult Tune(
        IReadOnlyList<JunctionTuneSide> sides,
        JunctionTuneOptions options)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(sides);
        ArgumentNullException.ThrowIfNull(options);
        if (sides.Count == 0)
        {
            throw new ArgumentException("At least one side is required.", nameof(sides));
        }
        if (options.Families.Count == 0)
        {
            throw new ArgumentException("At least one family is required.", nameof(options));
        }
        if (!(options.MinCrossoverHz > 0) || !(options.MaxCrossoverHz >= options.MinCrossoverHz))
        {
            throw new ArgumentException("The corner window is invalid.", nameof(options));
        }
        foreach (JunctionTuneSide side in sides)
        {
            if (side.LowerImpulseResponse.Length == 0 || side.UpperImpulseResponse.Length == 0)
            {
                throw new ArgumentException($"Side {side.Name} has an empty response.", nameof(sides));
            }
            if (side.SampleRate <= 0)
            {
                throw new ArgumentException($"Side {side.Name} has no sample rate.", nameof(sides));
            }
        }

        // One crossover is written to every side, so the sides must agree on the one they run now.
        CrossoverEdge? currentLowPass = LowPassOf(sides[0].LowerChain);
        CrossoverEdge? currentHighPass = HighPassOf(sides[0].UpperChain);
        for (int i = 1; i < sides.Count; i++)
        {
            if (!SameFilter(LowPassOf(sides[i].LowerChain), currentLowPass) ||
                !SameFilter(HighPassOf(sides[i].UpperChain), currentHighPass))
            {
                throw new ArgumentException(
                    $"The {sides[0].Name} and {sides[i].Name} sides run different crossovers at this junction, " +
                    "and the tune writes one crossover for both: set them alike first (the side Lock keeps them so).");
            }
        }
        double currentHz = currentLowPass?.FrequencyHz
            ?? currentHighPass?.FrequencyHz
            ?? Math.Sqrt(options.MinCrossoverHz * options.MaxCrossoverHz);
        double nyquistHz = sides.Min(side => side.SampleRate) * 0.49;
        if (options.MinCrossoverHz >= nyquistHz)
        {
            throw new ArgumentException("The corner window sits above what was measured.", nameof(options));
        }

        // An octave outside the window and current corner, so the car's ripple is the same term for every candidate.
        (double rankingLowHz, double rankingHighHz) = (
            Math.Max(20, Math.Min(options.MinCrossoverHz, currentHz) / 2),
            Math.Min(Math.Min(20_000, nyquistHz), Math.Max(options.MaxCrossoverHz, currentHz) * 2));
        if (rankingHighHz <= rankingLowHz * 1.05)
        {
            throw new ArgumentException("The corner window leaves no band to read.", nameof(options));
        }

        int cropLength = Math.Clamp(
            CropPrePeakSamples + (int)Math.Ceiling(
                sides.Max(side => side.SampleRate) * (CropGatePeriods / rankingLowHz + CropMarginSeconds)),
            MinCropLength,
            MaxCropLength);
        var cropped = new (Complex[] Lower, Complex[] Upper)[sides.Count];
        for (int i = 0; i < sides.Count; i++)
        {
            Complex[][] pair = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
                [sides[i].LowerImpulseResponse, sides[i].UpperImpulseResponse],
                cropLength,
                CropPrePeakSamples);
            cropped[i] = (pair[0], pair[1]);
        }

        var detailWork = new Work(sides, cropped, options, nyquistHz, rankingLowHz, rankingHighHz);

        // Ranking runs decimated, the reported reads do not. Every candidate pays two gated FFTs per side whose
        // length is the measurement rate times a gate fixed in time, so a low junction at 96 kHz spends a 262144-point
        // transform to read a band that stops at 260 Hz. Own-band reads and the after-delay search stay at the
        // measured rate: those are a handful of calls, they are what the user reads, and the alignment search resolves
        // delay in samples. See docs/tech/crossover-auto-setup.md#decimated-ranking.
        Work rankingWork = BuildRankingWork(
            sides, cropped, options, nyquistHz, rankingLowHz, rankingHighHz) ?? detailWork;

        if (options.AcousticTarget != null)
        {
            var plants = new List<JunctionPlant>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                plants.Add(detailWork.PlantFor(i, rankingLowHz, rankingHighHz));
            }

            detailWork.Plants = plants;
            rankingWork.Plants = plants;
        }

        JunctionTuneCandidate? ranking = rankingWork.Evaluate(
            currentLowPass, currentHighPass, currentHz, replaceEdges: false, ownBand: false);
        JunctionTuneCandidate current = (ranking == null
                ? null
                : detailWork.ReadOwnBand(ranking))
            ?? throw new InvalidOperationException(
                "The junction's current crossover cannot be read: the band holds no usable bins.");

        // Own-band reads are costly, so only the reported few get them.
        List<(CrossoverEdge LowPass, CrossoverEdge HighPass)> probes = Probes(
            options, currentLowPass, currentHighPass);
        var evaluated = new JunctionTuneCandidate?[probes.Count];
        Parallel.For(0, probes.Count, index =>
        {
            (CrossoverEdge lowPass, CrossoverEdge highPass) = probes[index];
            evaluated[index] = rankingWork.Evaluate(
                lowPass, highPass, lowPass.FrequencyHz, replaceEdges: true, ownBand: false);
        });

        List<JunctionTuneCandidate> ranked = evaluated
            .Where(candidate => candidate != null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.RankingScoreDb)
            .ThenBy(candidate => Math.Abs(Math.Log(
                (candidate.LowerLowPass!.Value.FrequencyHz + candidate.UpperHighPass!.Value.FrequencyHz) /
                (2 * currentHz))))
            .ToList();
        if (ranked.Count == 0)
        {
            throw new InvalidOperationException(
                "No candidate could be read: the corner window admits no lattice frequency " +
                "or the band holds no usable bins.");
        }

        // Refined on the leading corners, as the wizard does. See docs/tech/crossover-auto-setup.md#junction-tuner.
        if (options.SplitCorners)
        {
            var split = new List<JunctionTuneCandidate>();
            foreach (JunctionTuneCandidate candidate in ranked.Take(SplitRefinements))
            {
                if (candidate.LowerLowPass is not { } low || candidate.UpperHighPass is not { } high)
                {
                    continue;
                }

                // Whole hertz can bring two offsets onto one pair of corners, or back onto the corner itself.
                var offered = new HashSet<(double Low, double High)>();
                foreach (double octaves in CrossoverAutoSetup.SplitOffsetOctaves)
                {
                    if (octaves == 0)
                    {
                        continue;
                    }

                    // Half the offset each way, so the junction itself does not move.
                    double spread = Math.Pow(2, octaves / 2);
                    CrossoverEdge lowEdge = low with { FrequencyHz = WholeHz(low.FrequencyHz / spread) };
                    CrossoverEdge highEdge = high with { FrequencyHz = WholeHz(high.FrequencyHz * spread) };
                    if (lowEdge.FrequencyHz < 20 ||
                        highEdge.FrequencyHz > nyquistHz ||
                        (lowEdge.Equals(low) && highEdge.Equals(high)) ||
                        !offered.Add((lowEdge.FrequencyHz, highEdge.FrequencyHz)))
                    {
                        continue;
                    }

                    JunctionTuneCandidate? read = rankingWork.Evaluate(
                        lowEdge,
                        highEdge,
                        Math.Sqrt(lowEdge.FrequencyHz * highEdge.FrequencyHz),
                        replaceEdges: true,
                        ownBand: false);
                    if (read != null)
                    {
                        split.Add(read);
                    }
                }
            }

            if (split.Count > 0)
            {
                ranked = ranked
                    .Concat(split)
                    .OrderBy(candidate => candidate.RankingScoreDb)
                    .ToList();
            }
        }

        double? closestAcousticCostDb = null;
        double? bestSumScoreDb = null;
        if (options.AcousticTarget != null)
        {
            foreach (JunctionTuneCandidate candidate in ranked.Where(candidate => candidate.AcousticReadInFull))
            {
                if (candidate.WorstAcousticCostDb is { } cost &&
                    (closestAcousticCostDb == null || cost < closestAcousticCostDb))
                {
                    closestAcousticCostDb = cost;
                }
            }

            bestSumScoreDb = ranked[0].RankingScoreDb;
            ranked = OrderForGoal(ranked, options.SumSlackDb);
        }

        List<JunctionTuneCandidate> reported = ranked
            .Take(1 + RunnersUpReported)
            .Select(candidate => detailWork.ReadOwnBand(candidate) ?? candidate)
            .ToList();

        // A shared-band win the user's own read-outs would not show is a win on paper.
        JunctionTuneCandidate best = reported[0];
        bool sumWins = best.RankingScoreDb < current.RankingScoreDb - options.KeepMarginDb &&
            best.ScoreDb <= current.ScoreDb;
        // Or it sums as well and draws the asked slope materially better.
        bool slopeWins = options.AcousticTarget != null &&
            best.AcousticReadInFull &&
            current.AcousticReadInFull &&
            best.WorstAcousticCostDb is { } bestCost &&
            current.WorstAcousticCostDb is { } currentCost &&
            bestCost < currentCost - AcousticKeepMarginDb &&
            best.RankingScoreDb <= current.RankingScoreDb + options.SumSlackDb &&
            best.ScoreDb <= current.ScoreDb + options.SumSlackDb;
        bool changed = (sumWins || slopeWins) &&
            best.Sides.Count > 0 &&
            !SameEdges(best, current);

        List<JunctionTuneAlignment> currentAfterDelay = detailWork.AfterDelay(current, replaceEdges: false);
        List<JunctionTuneAlignment> bestAfterDelay = detailWork.AfterDelay(best, replaceEdges: true);

        var driverSlopes = new List<JunctionDriverSlopes>();
        if (options.AcousticTarget is { } target)
        {
            double lowerHz = best.LowerLowPass?.FrequencyHz ?? best.UpperHighPass?.FrequencyHz ?? currentHz;
            double upperHz = best.UpperHighPass?.FrequencyHz ?? lowerHz;
            for (int i = 0; i < sides.Count; i++)
            {
                if (detailWork.DriverSlopes(i, target, lowerHz, upperHz) is { } slopes)
                {
                    driverSlopes.Add(slopes);
                }
            }
        }

        return new JunctionTuneResult(
            current,
            best,
            changed,
            reported.Skip(1).ToList(),
            currentAfterDelay,
            bestAfterDelay,
            ranked.Count,
            rankingLowHz,
            rankingHighHz,
            driverSlopes,
            closestAcousticCostDb,
            bestSumScoreDb);
    }

    /// <summary>Inside the corridor: candidates whose worst channel lands first, by average; then the nearest to
    /// landing; then those not read in full. Outside it the sum's own order stands.</summary>
    internal static List<JunctionTuneCandidate> OrderForGoal(
        IReadOnlyList<JunctionTuneCandidate> ranked, double sumSlackDb)
    {
        double admissible = ranked[0].RankingScoreDb + sumSlackDb;
        return ranked
            .Where(candidate => candidate.RankingScoreDb <= admissible)
            .OrderBy(candidate => candidate.AcousticGoalLands ? 0 : candidate.AcousticReadInFull ? 1 : 2)
            .ThenBy(candidate => (candidate.AcousticGoalLands
                ? candidate.AcousticCostDb
                : candidate.WorstAcousticCostDb) ?? double.PositiveInfinity)
            .ThenBy(candidate => candidate.RankingScoreDb)
            .Concat(ranked.Where(candidate => candidate.RankingScoreDb > admissible))
            .ToList();
    }

    public static bool WasAcousticTargetReached(double? costDb) =>
        costDb is { } cost && cost <= AcousticReachedCostDb;


    /// <summary>A <see cref="Work"/> reading the same crops at a rate just above the ranking band, or null when the
    /// band already fills the measured rate and there is nothing to throw away.</summary>
    private static Work? BuildRankingWork(
        IReadOnlyList<JunctionTuneSide> sides,
        (Complex[] Lower, Complex[] Upper)[] cropped,
        JunctionTuneOptions options,
        double nyquistHz,
        double rankingLowHz,
        double rankingHighHz)
    {
        int factor = int.MaxValue;
        foreach (JunctionTuneSide side in sides)
        {
            factor = Math.Min(
                factor,
                (int)Math.Floor(side.SampleRate / (DecimationBandHeadroom * rankingHighHz)));
        }

        if (factor < MinimumDecimationFactor)
        {
            return null;
        }

        var decimatedSides = new List<JunctionTuneSide>(sides.Count);
        var decimatedCrops = new (Complex[] Lower, Complex[] Upper)[sides.Count];
        double decimatedNyquist = double.MaxValue;
        for (int i = 0; i < sides.Count; i++)
        {
            int rate = sides[i].SampleRate / factor;
            if (rate <= 0)
            {
                return null;
            }

            decimatedCrops[i] = (
                Decimate(cropped[i].Lower, factor),
                Decimate(cropped[i].Upper, factor));
            if (decimatedCrops[i].Lower.Length < 8 || decimatedCrops[i].Upper.Length < 8)
            {
                return null;
            }

            decimatedSides.Add(sides[i] with { SampleRate = rate });
            decimatedNyquist = Math.Min(decimatedNyquist, rate * 0.49);
        }

        // The junction band never reaches the ranking top, and the headroom keeps that top well under the new
        // Nyquist, so the bands the reads use are the same ones the undecimated work would have used.
        return new Work(
            decimatedSides,
            decimatedCrops,
            options,
            Math.Min(nyquistHz, decimatedNyquist),
            rankingLowHz,
            rankingHighHz);
    }

    /// <summary>Anti-aliased decimation by a whole factor, in double: the repository keeps everything past the
    /// analysis boundary in double, and the app's rational-ratio converter is a float playback path.</summary>
    internal static Complex[] Decimate(Complex[] signal, int factor)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (factor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }
        if (factor == 1)
        {
            return signal;
        }

        int taps = DecimationKernelHalfLength * factor;
        var kernel = new double[2 * taps + 1];
        double cutoff = 1.0 / factor;
        double sum = 0;
        for (int i = -taps; i <= taps; i++)
        {
            double sinc = i == 0 ? cutoff : Math.Sin(Math.PI * cutoff * i) / (Math.PI * i);
            // Blackman: the stopband has to be under the measurement noise, not merely tidy.
            double position = (double)(i + taps) / (2 * taps);
            double window = 0.42
                - 0.5 * Math.Cos(Math.Tau * position)
                + 0.08 * Math.Cos(2 * Math.Tau * position);
            kernel[i + taps] = sinc * window;
            sum += kernel[i + taps];
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        int length = signal.Length / factor;
        var result = new Complex[length];
        for (int n = 0; n < length; n++)
        {
            int center = n * factor;
            int from = Math.Max(0, center - taps);
            int to = Math.Min(signal.Length - 1, center + taps);
            Complex accumulated = Complex.Zero;
            for (int m = from; m <= to; m++)
            {
                accumulated += kernel[m - center + taps] * signal[m];
            }

            result[n] = accumulated;
        }

        return result;
    }

    public static (double LowHz, double HighHz) JunctionBand(double cornerHz, double nyquistHz) =>
        (Math.Max(20, cornerHz / 2), Math.Min(Math.Min(20_000, nyquistHz), cornerHz * 2));

    /// <summary>Read-only: each variant read on its own band, on the shared band, and after its own best delay.</summary>
    public static JunctionProbeResult Probe(
        IReadOnlyList<JunctionTuneSide> sides,
        int processorSampleRateHz,
        IReadOnlyList<JunctionProbeVariant> variants)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(sides);
        ArgumentNullException.ThrowIfNull(variants);
        if (sides.Count == 0)
        {
            throw new ArgumentException("At least one side is required.", nameof(sides));
        }
        if (variants.Count == 0)
        {
            throw new ArgumentException("At least one variant is required.", nameof(variants));
        }
        foreach (JunctionProbeVariant variant in variants)
        {
            if (variant.Sides.Count != sides.Count)
            {
                throw new ArgumentException(
                    $"Variant '{variant.Label}' must carry one chain pair per side.", nameof(variants));
            }
        }

        double nyquistHz = sides.Min(side => side.SampleRate) * 0.49;
        // Both edges: a variant may hold corners apart on purpose (mid LP 3.6 kHz under tweeter HP 4.4 kHz).
        var corners = new List<double>();
        foreach (JunctionProbeVariant variant in variants)
        {
            foreach (JunctionProbeChains chains in variant.Sides)
            {
                corners.Add(CornerOf(chains));
                if (LowPassOf(chains.Lower) is { } low)
                {
                    corners.Add(low.FrequencyHz);
                }
                if (HighPassOf(chains.Upper) is { } high)
                {
                    corners.Add(high.FrequencyHz);
                }
            }
        }

        double sharedLowHz = Math.Max(20, corners.Min() / 2);
        double sharedHighHz = Math.Min(Math.Min(20_000, nyquistHz), corners.Max() * 2);
        if (sharedHighHz <= sharedLowHz * 1.05)
        {
            throw new ArgumentException(
                "The variants leave no band to read them on.", nameof(variants));
        }

        int cropLength = Math.Clamp(
            CropPrePeakSamples + (int)Math.Ceiling(
                sides.Max(side => side.SampleRate) * (CropGatePeriods / sharedLowHz + CropMarginSeconds)),
            MinCropLength,
            MaxCropLength);
        var cropped = new (Complex[] Lower, Complex[] Upper)[sides.Count];
        for (int i = 0; i < sides.Count; i++)
        {
            Complex[][] pair = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
                [sides[i].LowerImpulseResponse, sides[i].UpperImpulseResponse],
                cropLength,
                CropPrePeakSamples);
            cropped[i] = (pair[0], pair[1]);
        }

        var options = new JunctionTuneOptions(
            [CrossoverFilterFamily.LinkwitzRiley], null, sharedLowHz, sharedHighHz,
            IndependentSlopes: true, processorSampleRateHz);
        var work = new Work(sides, cropped, options, nyquistHz, sharedLowHz, sharedHighHz);

        var entries = new List<JunctionProbeEntry>(variants.Count);
        foreach (JunctionProbeVariant variant in variants)
        {
            double cornerHz = CornerOf(variant.Sides[0]);
            CrossoverEdge? lowPass = LowPassOf(variant.Sides[0].Lower);
            CrossoverEdge? highPass = HighPassOf(variant.Sides[0].Upper);
            (double bandLowHz, double bandHighHz) = JunctionBand(cornerHz, nyquistHz);
            IReadOnlyList<JunctionTuneReading>? own = work.ReadVariant(variant, bandLowHz, bandHighHz);
            IReadOnlyList<JunctionTuneReading>? shared = own == null
                ? null
                : work.ReadVariant(variant, sharedLowHz, sharedHighHz);
            entries.Add(own == null || shared == null
                ? new JunctionProbeEntry(
                    variant.Label, lowPass, highPass, cornerHz, [], [], [], [],
                    bandLowHz, bandHighHz, "the band holds no usable bins")
                : new JunctionProbeEntry(
                    variant.Label, lowPass, highPass, cornerHz, own, shared,
                    work.AfterDelayOf(variant, cornerHz, bandLowHz, bandHighHz),
                    work.PhaseOf(variant, cornerHz, bandLowHz, bandHighHz),
                    bandLowHz, bandHighHz, null));
        }

        return new JunctionProbeResult(entries, sharedLowHz, sharedHighHz);
    }

    // Between both edges (geometric middle) since they may be held apart on purpose.
    private static double CornerOf(JunctionProbeChains chains) =>
        (LowPassOf(chains.Lower)?.FrequencyHz, HighPassOf(chains.Upper)?.FrequencyHz) switch
        {
            ({ } low, { } high) => Math.Sqrt(low * high),
            ({ } low, null) => low,
            (null, { } high) => high,
            _ => 632.0
        };

    /// <summary>Read-only: per side, what Auto delay would pick for the upper channel and the rivals it weighed.</summary>
    public static IReadOnlyList<JunctionDelayProbeSide> ProbeAlignment(
        IReadOnlyList<JunctionTuneSide> sides,
        int processorSampleRateHz,
        int maxCandidates = DelayProbeCandidatesReported)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(sides);
        if (sides.Count == 0)
        {
            throw new ArgumentException("At least one side is required.", nameof(sides));
        }
        if (maxCandidates < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        }

        CrossoverEdge? lowPass = LowPassOf(sides[0].LowerChain);
        CrossoverEdge? highPass = HighPassOf(sides[0].UpperChain);
        double nyquistHz = sides.Min(side => side.SampleRate) * 0.49;
        double cornerHz = lowPass?.FrequencyHz ?? highPass?.FrequencyHz
            ?? throw new ArgumentException(
                "The junction has no crossover to read a band from.", nameof(sides));
        (double bandLowHz, double bandHighHz) = JunctionBand(cornerHz, nyquistHz);
        double halfWindowMs = CrossoverAutoSetup.PostCheckHalfWindowMs(cornerHz);

        int cropLength = Math.Clamp(
            CropPrePeakSamples + (int)Math.Ceiling(
                sides.Max(side => side.SampleRate) * (CropGatePeriods / bandLowHz + CropMarginSeconds)),
            MinCropLength,
            MaxCropLength);
        var result = new List<JunctionDelayProbeSide>(sides.Count);
        foreach (JunctionTuneSide side in sides)
        {
            Complex[][] pair = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
                [side.LowerImpulseResponse, side.UpperImpulseResponse], cropLength, CropPrePeakSamples);
            Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
                pair[0], side.LowerChain, side.SampleRate, processorSampleRateHz,
                out ValidSampleRange lowerRange);
            Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
                pair[1], side.UpperChain, side.SampleRate, processorSampleRateHz,
                out ValidSampleRange upperRange);
            IReadOnlyList<AlignmentCandidate> found = VirtualCrossoverAnalysis.FindAlignmentCandidates(
                upper, [lower], side.SampleRate, bandLowHz, bandHighHz,
                -halfWindowMs, halfWindowMs,
                priorDelayMs: 0,
                priorSigmaMs: halfWindowMs / 2.0,
                forcedPolarity: null,
                levelMatch: false,
                out IReadOnlyList<AlignmentCandidate> allOptima,
                gateAnchorSample: null,
                variableValidRange: upperRange,
                fixedValidRanges: [lowerRange]);
            if (found.Count == 0)
            {
                result.Add(new JunctionDelayProbeSide(
                    side.Name, bandLowHz, bandHighHz, halfWindowMs, [],
                    "no alignment candidate was found in the search window"));
                continue;
            }

            AlignmentCandidate chosen = AlignmentSelection.Select(found, 0);
            List<JunctionDelayProbeCandidate> reported = (allOptima.Count > 0 ? allOptima : found)
                .OrderByDescending(candidate => candidate.ScoreDb)
                .Take(maxCandidates)
                .Select(candidate => new JunctionDelayProbeCandidate(
                    candidate.DelayMs,
                    ResultingPolarity(side.UpperChain, candidate),
                    candidate.ScoreDb,
                    candidate.LossDb, candidate.DipDb,
                    candidate.DelayMs.Equals(chosen.DelayMs) &&
                        candidate.InvertPolarity == chosen.InvertPolarity))
                .ToList();
            if (!reported.Any(candidate => candidate.Chosen))
            {
                reported.Insert(0, new JunctionDelayProbeCandidate(
                    chosen.DelayMs, ResultingPolarity(side.UpperChain, chosen), chosen.ScoreDb,
                    chosen.LossDb, chosen.DipDb, Chosen: true));
            }

            result.Add(new JunctionDelayProbeSide(
                side.Name, bandLowHz, bandHighHz, halfWindowMs, reported, null));
        }

        return result;
    }

    /// <summary>A filter only steepens: a driver already falling faster than asked cannot be brought onto it. An
    /// unfitted figure (null) is not a refusal.</summary>
    public static bool IsReachable(double? driverDbPerOctave, double? askedDbPerOctave) =>
        driverDbPerOctave is not { } driver ||
        askedDbPerOctave is not { } asked ||
        Math.Abs(driver) <= Math.Abs(asked) + SlopeReachToleranceDbPerOctave;

    /// <summary>Each edge is asked at its own corner, as the EQ stage aims at it: a split pair judged at one shared
    /// corner would be charged for a slope it draws exactly.</summary>
    private static JunctionAcousticFit? AcousticFit(
        JunctionAcousticTarget target,
        double lowerCornerHz,
        double upperCornerHz,
        JunctionPlant plant,
        CrossoverEdge? lowerEdge,
        CrossoverEdge? upperEdge,
        int rateHz)
    {
        AcousticChannelFit? lower = ChannelFit(
            target, lowerCornerHz, plant.Lower, lowerEdge, rateHz, upper: false);
        AcousticChannelFit? upper = ChannelFit(
            target, upperCornerHz, plant.Upper, upperEdge, rateHz, upper: true);
        if (lower == null && upper == null)
        {
            return null;
        }

        return new JunctionAcousticFit(
            lower?.ChargeDb,
            upper?.ChargeDb,
            lower?.SlopeDbPerOctave,
            upper?.SlopeDbPerOctave,
            lower?.TargetSlopeDbPerOctave ?? upper?.TargetSlopeDbPerOctave);
    }

    /// <summary>Slopes are fall per octave away from the corner, positive.</summary>
    private sealed record AcousticChannelFit(
        double ChargeDb,
        double? SlopeDbPerOctave,
        double? TargetSlopeDbPerOctave);

    private static AcousticChannelFit? ChannelFit(
        JunctionAcousticTarget target,
        double cornerHz,
        IReadOnlyList<SignalPoint> plant,
        CrossoverEdge? candidate,
        int rateHz,
        bool upper)
    {
        if (!(cornerHz > 0) || plant.Count == 0)
        {
            return null;
        }

        var edge = new CrossoverEdge(target.Family, cornerHz, target.SlopeDbPerOctave);
        CrossoverSpec asked = upper
            ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)
            : new CrossoverSpec(CrossoverKind.LowPass, edge);
        CrossoverSpec? applied = candidate is { } value
            ? upper
                ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: value)
                : new CrossoverSpec(CrossoverKind.LowPass, value)
            : null;

        double[] weights = OctaveWeights(plant);

        // The level is free: the weighted median difference across the passband side, against the asked edge.
        var offsets = new List<(double Offset, double Weight)>();
        for (int i = 0; i < plant.Count; i++)
        {
            SignalPoint point = plant[i];
            if (!InAcousticWindow(point.X, cornerHz))
            {
                continue;
            }

            if (upper ? point.X > cornerHz : point.X < cornerHz)
            {
                offsets.Add((AchievedDb(point, applied, rateHz) - EdgeDb(asked, point.X, rateHz), weights[i]));
            }
        }

        if (offsets.Count < AcousticMinimumBins)
        {
            return null;
        }

        offsets.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        double half = 0.5 * offsets.Sum(item => item.Weight);
        double reference = offsets[^1].Offset;
        double run = 0;
        foreach ((double offset, double offsetWeight) in offsets)
        {
            run += offsetWeight;
            if (run >= half)
            {
                reference = offset;
                break;
            }
        }

        var charged = new List<(double Weight, double Deviation)>();
        var achieved = new MagnitudeSlopeFit();
        var wanted = new MagnitudeSlopeFit();
        for (int i = 0; i < plant.Count; i++)
        {
            SignalPoint point = plant[i];
            if (!InAcousticWindow(point.X, cornerHz))
            {
                continue;
            }

            // From the corner outwards, where filter orders differ most.
            if (upper ? point.X > cornerHz : point.X < cornerHz)
            {
                continue;
            }

            double askedDb = EdgeDb(asked, point.X, rateHz);
            if (askedDb < -AcousticChargeFloorDb)
            {
                continue;
            }

            double levelDb = AchievedDb(point, applied, rateHz) - reference;
            double weightHere = weights[i];
            charged.Add((weightHere, levelDb - askedDb));
            achieved.Add(point.X, levelDb, weightHere);
            wanted.Add(point.X, askedDb, weightHere);
        }

        if (charged.Count < AcousticMinimumBins)
        {
            return null;
        }

        charged.Sort((left, right) => Math.Abs(right.Deviation).CompareTo(Math.Abs(left.Deviation)));
        double region = charged.Sum(bin => bin.Weight);
        double trimmed = 0;
        double charge = 0;
        double weight = 0;
        foreach ((double binWeight, double deviation) in charged)
        {
            if (trimmed < AcousticTrimmedWeight * region)
            {
                trimmed += binWeight;
                continue;
            }

            charge += binWeight *
                (deviation < 0 ? -deviation : AcousticSofterChargeFactor * deviation);
            weight += binWeight;
        }

        if (!(weight > 0))
        {
            return null;
        }

        return new AcousticChannelFit(
            charge / weight,
            achieved.DbPerOctave is { } slope ? Math.Abs(slope) : null,
            wanted.DbPerOctave is { } askedSlope ? Math.Abs(askedSlope) : null);
    }

    /// <summary>The stretch of log frequency each point stands for: the plant is a log grid only where bins are dense,
    /// so neither a flat nor a 1/f weight treats the two sides of a junction alike.</summary>
    private static double[] OctaveWeights(IReadOnlyList<SignalPoint> curve)
    {
        var weights = new double[curve.Count];
        for (int i = 0; i < curve.Count; i++)
        {
            double below = i > 0
                ? Math.Min(Math.Log2(curve[i].X / curve[i - 1].X), MaxWeightSpanOctaves)
                : 0;
            double above = i < curve.Count - 1
                ? Math.Min(Math.Log2(curve[i + 1].X / curve[i].X), MaxWeightSpanOctaves)
                : 0;
            weights[i] = 0.5 * (below + above);
        }

        return weights;
    }

    private static bool InAcousticWindow(double frequencyHz, double cornerHz) =>
        frequencyHz > 0 && Math.Abs(Math.Log2(frequencyHz / cornerHz)) <= AcousticWindowOctaves;

    private static double AchievedDb(SignalPoint plant, CrossoverSpec? applied, int rateHz) =>
        applied == null ? plant.Y : plant.Y + EdgeDb(applied, plant.X, rateHz);

    private static double EdgeDb(CrossoverSpec spec, double frequencyHz, int rateHz) =>
        20 * Math.Log10(Math.Max(
            CrossoverFilter.Response(spec, frequencyHz, rateHz).Magnitude, 1e-12));

    /// <summary>1/24-octave buckets averaged in dB, the tonal target already taken out.</summary>
    private static List<SignalPoint> Thin(
        IReadOnlyList<SignalPoint> curve,
        double lowHz,
        double highHz,
        IReadOnlyList<SignalPoint>? targetCurveDb)
    {
        var thinned = new List<SignalPoint>();
        double bucketTop = 0;
        double levels = 0;
        double logs = 0;
        int count = 0;

        void Flush()
        {
            if (count > 0)
            {
                double frequencyHz = Math.Exp(logs / count);
                double levelDb = levels / count;
                if (targetCurveDb is { Count: > 0 })
                {
                    levelDb -= CurveSampling.InterpolateDbLog(targetCurveDb, frequencyHz, clampEnds: true);
                }

                thinned.Add(new SignalPoint(frequencyHz, levelDb));
            }

            levels = 0;
            logs = 0;
            count = 0;
        }

        foreach (SignalPoint point in curve.OrderBy(point => point.X))
        {
            if (!(point.X >= lowHz) || point.X > highHz || !double.IsFinite(point.Y))
            {
                continue;
            }

            if (count > 0 && point.X > bucketTop)
            {
                Flush();
            }

            if (count == 0)
            {
                bucketTop = point.X * MinProbeRatio;
            }

            levels += point.Y;
            logs += Math.Log(point.X);
            count++;
        }

        Flush();
        return thinned;
    }

    /// <summary>Takes the low-pass out, leaving a high-pass where the channel had both; everything else unchanged.</summary>
    public static DspChannelChain WithoutLowPass(DspChannelChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return chain.Crossover switch
        {
            { Kind: CrossoverKind.BandPass } spec => chain with
            {
                Crossover = new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: spec.HighPassEdge)
            },
            { Kind: CrossoverKind.LowPass } => chain with { Crossover = CrossoverSpec.Off },
            _ => chain
        };
    }

    public static DspChannelChain WithoutHighPass(DspChannelChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return chain.Crossover switch
        {
            { Kind: CrossoverKind.BandPass } spec => chain with
            {
                Crossover = new CrossoverSpec(CrossoverKind.LowPass, spec.LowPassEdge)
            },
            { Kind: CrossoverKind.HighPass } => chain with { Crossover = CrossoverSpec.Off },
            _ => chain
        };
    }

    /// <summary>Replaces the low-pass, adding it where there was none; everything else unchanged.</summary>
    public static DspChannelChain WithLowPass(DspChannelChain chain, CrossoverEdge lowPass)
    {
        ArgumentNullException.ThrowIfNull(chain);
        CrossoverSpec crossover = chain.Crossover ?? CrossoverSpec.Off;
        CrossoverSpec replaced = crossover.Kind switch
        {
            CrossoverKind.HighPass or CrossoverKind.BandPass =>
                new CrossoverSpec(CrossoverKind.BandPass, lowPass, crossover.HighPassEdge),
            _ => new CrossoverSpec(CrossoverKind.LowPass, lowPass)
        };
        return WithMovedReference(chain, lowPass, lowPassMoved: true) with
        {
            Crossover = replaced
        };
    }

    public static DspChannelChain WithHighPass(DspChannelChain chain, CrossoverEdge highPass)
    {
        ArgumentNullException.ThrowIfNull(chain);
        CrossoverSpec crossover = chain.Crossover ?? CrossoverSpec.Off;
        CrossoverSpec replaced = crossover.Kind switch
        {
            CrossoverKind.LowPass or CrossoverKind.BandPass =>
                new CrossoverSpec(CrossoverKind.BandPass, crossover.LowPassEdge, highPass),
            _ => new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: highPass)
        };
        return WithMovedReference(chain, highPass, lowPassMoved: false) with
        {
            Crossover = replaced
        };
    }

    // A phase rotation references one crossover corner (PhaseRotationSpec.ReferenceIsLowPass), so moving that corner must move the all-pass too,
    // or the search scores a response the device would not produce.
    private static DspChannelChain WithMovedReference(
        DspChannelChain chain,
        CrossoverEdge newEdge,
        bool lowPassMoved) =>
        !chain.PhaseRotation.IsTransparent &&
        chain.PhaseRotation.ReferenceIsLowPass == lowPassMoved
            ? chain with
            {
                PhaseRotation = chain.PhaseRotation with { ReferenceHz = newEdge.FrequencyHz }
            }
            : chain;

    // The search reads the already-inverted response, so its flip is relative; report the resulting polarity.
    private static bool ResultingPolarity(DspChannelChain upperChain, AlignmentCandidate candidate) =>
        upperChain.InvertPolarity ^ candidate.InvertPolarity;

    private static CrossoverEdge? LowPassOf(DspChannelChain chain) =>
        chain.Crossover is { Kind: CrossoverKind.LowPass or CrossoverKind.BandPass } spec
            ? spec.LowPassEdge
            : null;

    private static CrossoverEdge? HighPassOf(DspChannelChain chain) =>
        chain.Crossover is { Kind: CrossoverKind.HighPass or CrossoverKind.BandPass } spec
            ? spec.HighPassEdge
            : null;

    private static bool SameEdges(JunctionTuneCandidate a, JunctionTuneCandidate b) =>
        a.LowerLowPass.Equals(b.LowerLowPass) && a.UpperHighPass.Equals(b.UpperHighPass);

    // Ripple is read only by a Chebyshev; a figure left over from another family is no difference.
    private static bool SameFilter(CrossoverEdge? a, CrossoverEdge? b) =>
        a is { } left && b is { } right
            ? left.Family == right.Family &&
              left.FrequencyHz.Equals(right.FrequencyHz) &&
              left.SlopeDbPerOctave == right.SlopeDbPerOctave &&
              (left.Family != CrossoverFilterFamily.Chebyshev || left.RippleDb.Equals(right.RippleDb))
            : a == null && b == null;

    private static List<(CrossoverEdge LowPass, CrossoverEdge HighPass)> Probes(
        JunctionTuneOptions options, CrossoverEdge? currentLowPass, CrossoverEdge? currentHighPass)
    {
        var probes = new List<(CrossoverEdge, CrossoverEdge)>();
        double[] lattice = CrossoverAutoSetup.LatticePoints(options.MinCrossoverHz, options.MaxCrossoverHz);
        var corners = new List<double>();
        foreach (double frequency in lattice)
        {
            if (corners.Count == 0 || frequency >= corners[^1] * MinProbeRatio)
            {
                corners.Add(frequency);
            }
        }
        if (corners.Count > 0 && corners[^1] < lattice[^1])
        {
            corners.Add(lattice[^1]);
        }

        foreach (CrossoverFilterFamily family in options.Families.Distinct())
        {
            // The floor is the summation mode's; against a stated slope a soft edge is often what lands on it.
            List<int> slopes = CrossoverFilter.SupportedSlopes(family)
                .Where(slope => options.Slopes == null
                    ? options.AcousticTarget != null || slope >= PracticalSlopeFloorDbPerOctave
                    : options.Slopes.Contains(slope))
                .ToList();
            if (slopes.Count == 0)
            {
                continue;
            }

            double lowRipple = RippleFor(family, currentLowPass);
            double highRipple = RippleFor(family, currentHighPass);
            foreach (double corner in corners)
            {
                foreach (int lowerSlope in slopes)
                {
                    foreach (int upperSlope in options.IndependentSlopes ? slopes : [lowerSlope])
                    {
                        probes.Add((
                            new CrossoverEdge(family, corner, lowerSlope, lowRipple),
                            new CrossoverEdge(family, corner, upperSlope, highRipple)));
                    }
                }
            }
        }

        return probes;
    }

    /// <summary>The card and the processor take whole hertz.</summary>
    private static double WholeHz(double frequencyHz) => Math.Round(frequencyHz);

    private static double RippleFor(CrossoverFilterFamily family, CrossoverEdge? current) =>
        family == CrossoverFilterFamily.Chebyshev && current is { Family: CrossoverFilterFamily.Chebyshev } edge
            ? edge.RippleDb
            : 1.0;

    // Processed responses cached per edge: a channel's response depends on its own edge only, so slope combinations share it.
    private sealed class Work
    {
        private readonly IReadOnlyList<JunctionTuneSide> sides;
        private readonly (Complex[] Lower, Complex[] Upper)[] cropped;
        private readonly JunctionTuneOptions options;
        private readonly double nyquistHz;
        private readonly double rankingLowHz;
        private readonly double rankingHighHz;
        // Keyed by the chain record: identical chains share one run.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            (int Side, bool Upper, DspChannelChain Chain), (Complex[] Response, ValidSampleRange Range)> processed = new();

        /// <summary>Per side, set before ranking when an acoustic slope was stated; null leaves the acoustic term out.</summary>
        public IReadOnlyList<JunctionPlant>? Plants { get; set; }

        public Work(
            IReadOnlyList<JunctionTuneSide> sides,
            (Complex[] Lower, Complex[] Upper)[] cropped,
            JunctionTuneOptions options,
            double nyquistHz,
            double rankingLowHz,
            double rankingHighHz)
        {
            this.sides = sides;
            this.cropped = cropped;
            this.options = options;
            this.nyquistHz = nyquistHz;
            this.rankingLowHz = rankingLowHz;
            this.rankingHighHz = rankingHighHz;
        }

        public JunctionTuneCandidate? Evaluate(
            CrossoverEdge? lowPass, CrossoverEdge? highPass, double cornerHz, bool replaceEdges, bool ownBand)
        {
            (double bandLowHz, double bandHighHz) = JunctionBand(cornerHz, nyquistHz);
            if (bandHighHz <= bandLowHz * 1.05)
            {
                return null;
            }

            List<JunctionTuneReading>? ranking = ReadSides(
                lowPass, highPass, replaceEdges, rankingLowHz, rankingHighHz);
            if (ranking == null)
            {
                return null;
            }

            var candidate = new JunctionTuneCandidate(lowPass, highPass, [], ranking, bandLowHz, bandHighHz);
            return ownBand ? ReadOwnBand(candidate) : candidate;
        }

        public JunctionTuneCandidate? ReadOwnBand(JunctionTuneCandidate candidate)
        {
            List<JunctionTuneReading>? own = ReadSides(
                candidate.LowerLowPass, candidate.UpperHighPass, replaceEdges: true,
                candidate.BandLowHz, candidate.BandHighHz);
            return own == null ? null : candidate with { Sides = own };
        }

        public IReadOnlyList<JunctionTuneReading>? ReadVariant(
            JunctionProbeVariant variant, double bandLowHz, double bandHighHz)
        {
            if (bandHighHz <= bandLowHz * 1.05)
            {
                return null;
            }

            var readings = new List<JunctionTuneReading>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                JunctionTuneReading? reading = Read(
                    i, variant.Sides[i].Lower, variant.Sides[i].Upper, bandLowHz, bandHighHz);
                if (reading == null)
                {
                    return null;
                }

                readings.Add(reading);
            }

            return readings;
        }

        /// <summary>Uses the phase analysis's own window, not the sums' alignment window; comparable only within one probe.</summary>
        public List<JunctionProbePhase> PhaseOf(
            JunctionProbeVariant variant, double cornerHz, double bandLowHz, double bandHighHz)
        {
            var result = new List<JunctionProbePhase>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                (Complex[] lower, _) = Processed(i, upper: false, variant.Sides[i].Lower);
                (Complex[] upper, _) = Processed(i, upper: true, variant.Sides[i].Upper);
                result.Add(new JunctionProbePhase(
                    sides[i].Name,
                    JunctionPhaseAlignment.Analyze(
                        lower, upper, sides[i].SampleRate, cornerHz, bandLowHz, bandHighHz)));
            }

            return result;
        }

        public List<JunctionTuneAlignment> AfterDelayOf(
            JunctionProbeVariant variant, double cornerHz, double bandLowHz, double bandHighHz)
        {
            var result = new List<JunctionTuneAlignment>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                if (Align(i, variant.Sides[i].Lower, variant.Sides[i].Upper,
                    cornerHz, bandLowHz, bandHighHz) is { } alignment)
                {
                    result.Add(alignment);
                }
            }

            return result;
        }

        /// <summary>Every side read after re-aligning the upper channel for this candidate, as Auto delay will after a
        /// tune. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
        private List<JunctionTuneReading>? ReadSides(
            CrossoverEdge? lowPass, CrossoverEdge? highPass, bool replaceEdges,
            double bandLowHz, double bandHighHz)
        {
            double halfWindowMs = CrossoverAutoSetup.PostCheckHalfWindowMs(
                AlignmentCornerHz(lowPass, highPass, bandLowHz, bandHighHz));
            IReadOnlyList<JunctionSpectrumReading?>? joint = Joint
                ? ReadJointly(lowPass, highPass, replaceEdges, bandLowHz, bandHighHz, halfWindowMs)?.Readings
                : null;
            var readings = new List<JunctionTuneReading>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                DspChannelChain lowerChain = ChainFor(i, upper: false, lowPass, replaceEdges);
                DspChannelChain upperChain = ChainFor(i, upper: true, highPass, replaceEdges);
                JunctionTuneReading? reading = !Joint
                    ? ReadAligned(i, lowerChain, upperChain, bandLowHz, bandHighHz, halfWindowMs)
                    : joint?[i] is { } read
                        ? new JunctionTuneReading(sides[i].Name, read.LossDb, read.DipDb, read.RippleDb)
                        : Read(i, lowerChain, upperChain, bandLowHz, bandHighHz);
                if (reading == null)
                {
                    return null;
                }

                readings.Add(WithAcoustic(i, reading, lowPass, highPass));
            }

            return readings;
        }

        private bool Joint => options.OneAlignmentForAllSides && sides.Count > 1;

        private (IReadOnlyList<JunctionSpectrumReading?> Readings, AlignmentCandidate Alignment)? ReadJointly(
            CrossoverEdge? lowPass, CrossoverEdge? highPass, bool replaceEdges,
            double bandLowHz, double bandHighHz, double halfWindowMs)
        {
            var inputs = new List<JunctionAlignmentSide>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                (Complex[] lower, ValidSampleRange lowerRange) = Processed(
                    i, upper: false, ChainFor(i, upper: false, lowPass, replaceEdges));
                (Complex[] upper, ValidSampleRange upperRange) = Processed(
                    i, upper: true, ChainFor(i, upper: true, highPass, replaceEdges));
                inputs.Add(new JunctionAlignmentSide(upper, lower, sides[i].SampleRate, upperRange, lowerRange));
            }

            return VirtualCrossoverAnalysis.MeasureJointlyAlignedJunctionSpectra(
                inputs, bandLowHz, bandHighHz, halfWindowMs);
        }

        private JunctionTuneReading WithAcoustic(
            int side, JunctionTuneReading reading, CrossoverEdge? lowPass, CrossoverEdge? highPass)
        {
            if (options.AcousticTarget is not { } target || Plants is not { } plants)
            {
                return reading;
            }

            double lowerCornerHz = lowPass?.FrequencyHz ?? highPass?.FrequencyHz ?? 0;
            double upperCornerHz = highPass?.FrequencyHz ?? lowPass?.FrequencyHz ?? 0;
            return reading with
            {
                Acoustic = AcousticFit(
                    target,
                    lowerCornerHz,
                    upperCornerHz,
                    plants[side],
                    lowPass,
                    highPass,
                    options.ProcessorSampleRateHz)
            };
        }

        /// <summary>The plants' own fall through the region, no candidate edge applied.</summary>
        public JunctionDriverSlopes? DriverSlopes(
            int side, JunctionAcousticTarget target, double lowerCornerHz, double upperCornerHz)
        {
            if (Plants is not { } plants)
            {
                return null;
            }

            int rate = options.ProcessorSampleRateHz;
            return new JunctionDriverSlopes(
                sides[side].Name,
                ChannelFit(target, lowerCornerHz, plants[side].Lower, null, rate, upper: false)?.SlopeDbPerOctave,
                ChannelFit(target, upperCornerHz, plants[side].Upper, null, rate, upper: true)?.SlopeDbPerOctave);
        }

        public JunctionPlant PlantFor(int side, double lowHz, double highHz)
        {
            IReadOnlyList<SignalPoint>? lowerGiven = sides[side].LowerMagnitude;
            IReadOnlyList<SignalPoint>? upperGiven = sides[side].UpperMagnitude;
            if (lowerGiven != null && upperGiven != null)
            {
                return new JunctionPlant(
                    Thin(lowerGiven, lowHz, highHz, options.TargetCurveDb),
                    Thin(upperGiven, lowHz, highHz, options.TargetCurveDb));
            }

            // Without the PEQ, which is refitted after the tune; a correction FIR stays.
            (Complex[] lower, ValidSampleRange lowerRange) = Processed(
                side, upper: false, WithoutLowPass(sides[side].LowerChain) with { Peq = null });
            (Complex[] upper, ValidSampleRange upperRange) = Processed(
                side, upper: true, WithoutHighPass(sides[side].UpperChain) with { Peq = null });
            var read = new List<SignalPoint>();
            var readUpper = new List<SignalPoint>();
            if (VirtualCrossoverAnalysis.MeasureJunctionSpectrum(
                    upper, [lower], sides[side].SampleRate, lowHz, highHz,
                    upperRange, [lowerRange], out IReadOnlyList<JunctionLevelBin> bins) != null)
            {
                foreach (JunctionLevelBin bin in bins)
                {
                    read.Add(new SignalPoint(bin.FrequencyHz, bin.FixedDb));
                    readUpper.Add(new SignalPoint(bin.FrequencyHz, bin.VariableDb));
                }
            }

            return new JunctionPlant(
                Thin(lowerGiven ?? read, lowHz, highHz, options.TargetCurveDb),
                Thin(upperGiven ?? readUpper, lowHz, highHz, options.TargetCurveDb));
        }

        private JunctionTuneReading? Read(
            int side, DspChannelChain lowerChain, DspChannelChain upperChain,
            double bandLowHz, double bandHighHz)
        {
            (Complex[] lower, ValidSampleRange lowerRange) = Processed(side, upper: false, lowerChain);
            (Complex[] upper, ValidSampleRange upperRange) = Processed(side, upper: true, upperChain);
            JunctionSpectrumReading? reading = VirtualCrossoverAnalysis.MeasureJunctionSpectrum(
                upper, [lower], sides[side].SampleRate, bandLowHz, bandHighHz,
                upperRange, [lowerRange]);
            return reading == null
                ? null
                : new JunctionTuneReading(sides[side].Name, reading.LossDb, reading.DipDb, reading.RippleDb);
        }

        private JunctionTuneReading? ReadAligned(
            int side, DspChannelChain lowerChain, DspChannelChain upperChain,
            double bandLowHz, double bandHighHz, double halfWindowMs)
        {
            (Complex[] lower, ValidSampleRange lowerRange) = Processed(side, upper: false, lowerChain);
            (Complex[] upper, ValidSampleRange upperRange) = Processed(side, upper: true, upperChain);
            (JunctionSpectrumReading Reading, AlignmentCandidate Alignment)? aligned =
                VirtualCrossoverAnalysis.MeasureAlignedJunctionSpectrum(
                    upper, [lower], sides[side].SampleRate, bandLowHz, bandHighHz, halfWindowMs,
                    upperRange, [lowerRange]);
            return aligned is { Reading: var reading }
                ? new JunctionTuneReading(sides[side].Name, reading.LossDb, reading.DipDb, reading.RippleDb)
                // No delay evidence in the band: the reading at the current timing is all there is.
                : Read(side, lowerChain, upperChain, bandLowHz, bandHighHz);
        }

        // Same search and tie-breaks as the readings. Empty for a side with no candidate.
        public List<JunctionTuneAlignment> AfterDelay(JunctionTuneCandidate candidate, bool replaceEdges)
        {
            var result = new List<JunctionTuneAlignment>(sides.Count);
            double junctionHz = AlignmentCornerHz(
                candidate.LowerLowPass, candidate.UpperHighPass, candidate.BandLowHz, candidate.BandHighHz);
            if (Joint)
            {
                if (ReadJointly(
                        candidate.LowerLowPass, candidate.UpperHighPass, replaceEdges,
                        candidate.BandLowHz, candidate.BandHighHz,
                        CrossoverAutoSetup.PostCheckHalfWindowMs(junctionHz)) is { } joint)
                {
                    for (int i = 0; i < sides.Count; i++)
                    {
                        if (joint.Readings[i] is { } read)
                        {
                            result.Add(new JunctionTuneAlignment(
                                sides[i].Name,
                                joint.Alignment.DelayMs,
                                ResultingPolarity(
                                    ChainFor(i, upper: true, candidate.UpperHighPass, replaceEdges), joint.Alignment),
                                read.LossDb,
                                read.DipDb));
                        }
                    }
                }

                return result;
            }

            for (int i = 0; i < sides.Count; i++)
            {
                if (Align(
                    i,
                    ChainFor(i, upper: false, candidate.LowerLowPass, replaceEdges),
                    ChainFor(i, upper: true, candidate.UpperHighPass, replaceEdges),
                    junctionHz, candidate.BandLowHz, candidate.BandHighHz) is { } alignment)
                {
                    result.Add(alignment);
                }
            }

            return result;
        }

        private JunctionTuneAlignment? Align(
            int side, DspChannelChain lowerChain, DspChannelChain upperChain,
            double cornerHz, double bandLowHz, double bandHighHz)
        {
            double halfWindowMs = CrossoverAutoSetup.PostCheckHalfWindowMs(cornerHz);
            (Complex[] lower, ValidSampleRange lowerRange) = Processed(side, upper: false, lowerChain);
            (Complex[] upper, ValidSampleRange upperRange) = Processed(side, upper: true, upperChain);
            IReadOnlyList<AlignmentCandidate> found = VirtualCrossoverAnalysis.FindAlignmentCandidates(
                upper, [lower], sides[side].SampleRate, bandLowHz, bandHighHz,
                -halfWindowMs, halfWindowMs,
                priorDelayMs: 0,
                priorSigmaMs: halfWindowMs / 2.0,
                variableValidRange: upperRange,
                fixedValidRanges: [lowerRange]);
            if (found.Count == 0)
            {
                return null;
            }

            AlignmentCandidate chosen = AlignmentSelection.Select(found, 0);
            return new JunctionTuneAlignment(
                sides[side].Name, chosen.DelayMs, ResultingPolarity(upperChain, chosen),
                chosen.LossDb, chosen.DipDb);
        }

        /// <summary>The lower of two split corners, where group delay reaches furthest; shared by readings and report.</summary>
        private static double AlignmentCornerHz(
            CrossoverEdge? lowPass, CrossoverEdge? highPass, double bandLowHz, double bandHighHz) =>
            lowPass is { } low && highPass is { } high
                ? Math.Min(low.FrequencyHz, high.FrequencyHz)
                : lowPass?.FrequencyHz ?? highPass?.FrequencyHz ?? Math.Sqrt(bandLowHz * bandHighHz);

        private DspChannelChain ChainFor(int side, bool upper, CrossoverEdge? edge, bool replace)
        {
            DspChannelChain chain = upper ? sides[side].UpperChain : sides[side].LowerChain;
            return replace && edge is { } value
                ? upper ? WithHighPass(chain, value) : WithLowPass(chain, value)
                : chain;
        }

        private (Complex[] Response, ValidSampleRange Range) Processed(
            int side, bool upper, DspChannelChain chain) =>
            processed.GetOrAdd((side, upper, chain), key =>
            {
                JunctionTuneSide item = sides[key.Side];
                Complex[] response = VirtualCrossoverAnalysis.ApplyChain(
                    key.Upper ? cropped[key.Side].Upper : cropped[key.Side].Lower,
                    key.Chain, item.SampleRate, options.ProcessorSampleRateHz,
                    out ValidSampleRange range);
                return (response, range);
            });
    }
}
