using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>The pair's raw responses (one sample rate) and current chains; the tuner keeps everything except the two facing edges.</summary>
/// <param name="LowerMagnitude">
/// The channel's magnitude through its chain WITH THE FACING EDGE TAKEN OUT — the plant a candidate edge multiplies.
/// A spatial average where the channel has one, since that is the curve the EQ stage will work on later; null leaves
/// the tuner to read it off the gated impulse response, which is the same curve where there is no average.
/// Read only when an acoustic slope is stated; the coherent sum always comes from the impulse responses.
/// </param>
public sealed record JunctionTuneSide(
    string Name,
    Complex[] LowerImpulseResponse,
    DspChannelChain LowerChain,
    Complex[] UpperImpulseResponse,
    DspChannelChain UpperChain,
    int SampleRate,
    IReadOnlyList<SignalPoint>? LowerMagnitude = null,
    IReadOnlyList<SignalPoint>? UpperMagnitude = null);

/// <summary>
/// The acoustic crossover asked for at this junction: what <c>driver × filter</c> should look like, not what the
/// filter is. A filter can only steepen a driver, so a fall of the driver's own that is already steeper than this
/// cannot be reached — see <see cref="CrossoverJunctionTuner.IsReachable"/>.
/// </summary>
public sealed record JunctionAcousticTarget(CrossoverFilterFamily Family, int SlopeDbPerOctave);

/// <summary>
/// How one side's two acoustic shapes compare with the stated acoustic crossover over the handover region.
/// <see cref="ChargeDb"/> is what the score pays (asymmetric: too steep costs four times too soft, since only a cut
/// is free to the EQ stage); <see cref="ResidualDb"/> keeps the sign, so a reader can say which way the EQ must work.
/// Slopes are fitted the same way for the target and for the achieved curves, so they are comparable with each other
/// and not with the nameplate figure.
/// </summary>
public sealed record JunctionAcousticFit(
    double ChargeDb,
    double ResidualDb,
    double? LowerSlopeDbPerOctave,
    double? UpperSlopeDbPerOctave,
    double? TargetSlopeDbPerOctave);

/// <summary>One side's two plants: each channel's magnitude with the facing edge out of the chain, thinned to the
/// corner lattice's own resolution. The level is arbitrary; only shapes are read off them.</summary>
public sealed record JunctionPlant(
    IReadOnlyList<SignalPoint> Lower,
    IReadOnlyList<SignalPoint> Upper);

/// <summary>The drivers' own fall through the handover region, with the facing crossover edge taken out of the chain
/// and everything else left as it runs. This is the physics the stated slope has to live with.</summary>
public sealed record JunctionDriverSlopes(
    string Side,
    double? LowerDbPerOctave,
    double? UpperDbPerOctave);

/// <summary><see cref="Slopes"/> null = every family slope at or above 12 dB/oct, or with an acoustic target every slope
/// the family has.</summary>
/// <param name="KeepMarginDb">Per-side score margin a challenger needs to replace the user's current crossover.</param>
/// <param name="AcousticTarget">Null = the tuner judges the sum alone, as it always has.</param>
/// <param name="TargetCurveDb">
/// The full-range target the EQ stage will aim at, in dB against frequency (a house curve; null = flat). It is taken
/// OUT of the plant before any shape is judged, because the goal for a channel is target × acoustic crossover: left
/// in, a house curve's own tilt through the handover would be read as the driver's acoustic slope.
/// </param>
/// <param name="SumSlackDb">
/// How much summation a stated acoustic slope may spend. The slope never enters the score: candidates within this
/// much of the best summation are the ones it chooses between, so "the sum is the judge" is literally true.
/// </param>
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
    bool SplitCorners = false);

/// <summary>Coherent sum at the current delays and polarity over a band; lower score is better. Ripple includes the room's own.</summary>
public sealed record JunctionTuneReading(
    string Side,
    double LossDb,
    double DipDb,
    double RippleDb,
    JunctionAcousticFit? Acoustic = null)
{
    /// <summary>Loss, plus the dip's excess at half weight (as in the wizard post-check), plus ripple. The stated
    /// acoustic slope is NOT in here: it picks between candidates this score already calls equivalent.</summary>
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

    /// <summary>Average charge against the stated acoustic slope on the band candidates are ranked on, or null when
    /// none was stated or no side could be read against it.</summary>
    public double? AcousticCostDb
    {
        get
        {
            double total = 0;
            int read = 0;
            foreach (JunctionTuneReading side in RankingSides)
            {
                if (side.Acoustic is { } fit)
                {
                    total += fit.ChargeDb;
                    read++;
                }
            }

            return read > 0 ? total / read : null;
        }
    }

    /// <summary>The worst side's charge, for the report: one electrical filter serves both sides, and an average can
    /// be bought by making one of them worse. Read beside <see cref="AcousticCostDb"/>, which is what chooses.</summary>
    public double? WorstAcousticCostDb
    {
        get
        {
            double? worst = null;
            foreach (JunctionTuneReading side in RankingSides)
            {
                if (side.Acoustic is { } fit && (worst == null || fit.ChargeDb > worst))
                {
                    worst = fit.ChargeDb;
                }
            }

            return worst;
        }
    }
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
/// <param name="DriverSlopes">Read at the winner's corner, and empty unless an acoustic slope was stated.</param>
/// <param name="ClosestAcousticCostDb">
/// The least a stated acoustic slope could have been missed by, over EVERY candidate on the lattice and before the
/// summation corridor takes any of them away. It is the best this SEARCH SPACE can do — the corner window, the
/// allowed families and slopes and what the processor can run, not the driver alone. Read beside the chosen
/// candidate's own cost: materially worse means the slope was within reach and the summation would not pay for it.
/// Null unless a slope was stated.
/// </param>
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
    double? ClosestAcousticCostDb = null)
{
    /// <summary>Whether the best candidate is a different crossover from the one on screen, won or not. The keep
    /// margin behind <see cref="Changed"/> is advice; this is what an explicit Apply would change.</summary>
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

    /// <summary>
    /// How much summation a stated acoustic slope may spend: candidates within this much of the best summation are
    /// the ones it chooses between. A corridor rather than a weight in the score, because a weight lets a handsome
    /// slope buy a dip at some exchange rate nobody can name, while this figure is measurable on the battery and
    /// says what it means. See docs/tech/crossover-auto-setup.md#acoustic-slope-target.
    /// </summary>
    public const double DefaultSumSlackDb = 0.2;

    /// <summary>What the stated slope must gain before the user's own crossover is rewritten for its sake: a decibel
    /// of average deviation across the skirt. Less than that is not a reason to move a filter somebody chose.</summary>
    public const double AcousticKeepMarginDb = 1.0;

    /// <summary>Average deviation at which a stated acoustic slope counts as drawn rather than missed. The verdict is
    /// read off the lattice (what any allowed filter could do), never off a slope regression.</summary>
    public const double AcousticReachedCostDb = 2.0;


    /// <summary>Softer than asked costs a quarter of steeper than asked: the EQ stage lands the rest with CUTS, which
    /// it is free to make, while a skirt boost is what it refuses. Not free, or the search would buy the softest
    /// filter on offer and spend the bank's slots.</summary>
    public const double AcousticSofterChargeFactor = 0.25;

    /// <summary>Slack before a driver's own fall counts as steeper than the stated slope. Both sides of that
    /// comparison are straight-line fits of curved things, so a decibel or two per octave is not a disagreement.</summary>
    public const double SlopeReachToleranceDbPerOctave = 2.0;

    /// <summary>Charged from the corner outwards to here. Deeper is a stopband: the measurement is noise there and
    /// the EQ stage would not touch it either. The floor goes into the battery's sweep.</summary>
    private const double AcousticChargeFloorDb = 24.0;

    /// <summary>
    /// How wide a feature the acoustic term is allowed to see, as a moving average over log frequency. The objective
    /// keeps its OWN resolution rather than borrowing the display's smoothing, or the answer would change with a
    /// combo box; and it is wider than the grid the arithmetic runs on, because a narrow spatial notch must not cost
    /// what a systematic slope error over half an octave costs, while a slope fit still needs points to stand on.
    /// A sixth of an octave for now — the battery's to confirm or move.
    /// </summary>
    private const double AcousticSmoothingOctaves = 1.0 / 6.0;

    /// <summary>Octaves either side of the corner that hold the level reference and the charged region; the asked
    /// edge is past the floor beyond them, so the target is never evaluated there.</summary>
    private const double AcousticWindowOctaves = 2.0;

    private const int AcousticMinimumBins = 3;

    /// <summary>
    /// How much of the charged region's weight is dropped, worst deviation first, before the charge is averaged. A
    /// fifth: narrower than that and a feature is the seat rather than the crossover — and no filter on the lattice
    /// could answer it anyway — while a slope that is systematically wrong covers the region and pays in full.
    /// The battery's to confirm or move, together with <see cref="AcousticSmoothingOctaves"/>.
    /// </summary>
    private const double AcousticTrimmedWeight = 0.2;

    public const int PracticalSlopeFloorDbPerOctave = 12;

    public const int RunnersUpReported = 3;

    /// <summary>Leading candidates the split-corner pass is tried on. Four: a split refines a corner the sweep
    /// already likes, and every extra one costs the whole offset ladder.</summary>
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

        // A crossover is one filter for both sides.
        CrossoverEdge? currentLowPass = LowPassOf(sides[0].LowerChain);
        CrossoverEdge? currentHighPass = HighPassOf(sides[0].UpperChain);
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

        // The plant each candidate edge multiplies: the channel through its chain with the facing edge taken out,
        // thinned to the corner lattice's own resolution. Read once here rather than per candidate, and taken from
        // the caller's spatial average where there is one, since that is the curve the EQ stage will work on.
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

        // Split corners are a coordinate of their own, refined on the corners the sweep settled - the wizard's own
        // arrangement (docs/tech/crossover-auto-setup.md#split-corners). As a second lattice dimension it would
        // square the candidate count; refined on the few that lead, it costs a couple of dozen reads.
        if (options.SplitCorners)
        {
            var split = new List<JunctionTuneCandidate>();
            foreach (JunctionTuneCandidate candidate in ranked.Take(SplitRefinements))
            {
                if (candidate.LowerLowPass is not { } low || candidate.UpperHighPass is not { } high)
                {
                    continue;
                }

                // Rounding brings neighbouring offsets onto one pair of corners, and the smallest of them back
                // onto the corner itself, which is the candidate this pass is refining.
                var offered = new HashSet<(double Low, double High)>();
                foreach (double octaves in CrossoverAutoSetup.SplitOffsetOctaves)
                {
                    if (octaves == 0)
                    {
                        continue;
                    }

                    // Positive holds the corners apart, which takes a bump off the junction; negative overlaps them,
                    // which fills a dip. Half the offset each way, so the junction itself does not move.
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

        // A stated slope chooses INSIDE the corridor the summation leaves: everything within SumSlackDb of the best
        // sum is equivalent as a junction, so the slope decides between those, and a candidate outside the corridor
        // is never reached however well it draws the asked edge.
        double? closestAcousticCostDb = null;
        if (options.AcousticTarget != null)
        {
            // How close the lattice can get at all, before anything is set aside: that answers "can these drivers do
            // it", which a slope regression can only guess at.
            foreach (JunctionTuneCandidate candidate in ranked)
            {
                if (candidate.AcousticCostDb is { } cost &&
                    (closestAcousticCostDb == null || cost < closestAcousticCostDb))
                {
                    closestAcousticCostDb = cost;
                }

            }

            double admissible = ranked[0].RankingScoreDb + options.SumSlackDb;
            ranked = ranked
                .Where(candidate => candidate.RankingScoreDb <= admissible)
                .OrderBy(candidate => candidate.AcousticCostDb ?? double.PositiveInfinity)
                .ThenBy(candidate => candidate.RankingScoreDb)
                .Concat(ranked.Where(candidate => candidate.RankingScoreDb > admissible))
                .ToList();
        }

        List<JunctionTuneCandidate> reported = ranked
            .Take(1 + RunnersUpReported)
            .Select(candidate => detailWork.ReadOwnBand(candidate) ?? candidate)
            .ToList();

        // A shared-band win the user's own read-outs would not show is a win on paper.
        JunctionTuneCandidate best = reported[0];
        bool sumWins = best.RankingScoreDb < current.RankingScoreDb - options.KeepMarginDb &&
            best.ScoreDb <= current.ScoreDb;
        // Or the junction is as good as it was and the asked slope is materially better drawn: that is what the mode
        // is for, and without this an equal-summation answer could never replace the crossover on screen.
        bool slopeWins = options.AcousticTarget != null &&
            best.AcousticCostDb is { } bestCost &&
            current.AcousticCostDb is { } currentCost &&
            bestCost < currentCost - AcousticKeepMarginDb &&
            best.RankingScoreDb <= current.RankingScoreDb + options.SumSlackDb &&
            best.ScoreDb <= current.ScoreDb + options.SumSlackDb;
        bool changed = (sumWins || slopeWins) &&
            best.Sides.Count > 0 &&
            !SameEdges(best, current);

        List<JunctionTuneAlignment> currentAfterDelay = detailWork.AfterDelay(current, replaceEdges: false);
        List<JunctionTuneAlignment> bestAfterDelay = detailWork.AfterDelay(best, replaceEdges: true);

        // Read once, at the winner's corner: what the drivers do by themselves there is what says whether the stated
        // slope was reachable at all, and it is a handful of reads rather than a term in the search.
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
            closestAcousticCostDb);
    }

    /// <summary>Whether a stated acoustic slope counts as drawn, given the best a candidate could do: two decibels of
    /// average deviation across the skirt is as near as a discrete filter menu is asked to come.</summary>
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

    /// <summary>Whether the stated acoustic slope is reachable on a driver that already falls this fast by itself:
    /// a filter multiplies, so it can only make the fall steeper. Both figures are fits of curved things, hence the
    /// tolerance; an unfitted figure (null) is not a refusal.</summary>
    public static bool IsReachable(double? driverDbPerOctave, double? askedDbPerOctave) =>
        driverDbPerOctave is not { } driver ||
        askedDbPerOctave is not { } asked ||
        Math.Abs(driver) <= Math.Abs(asked) + SlopeReachToleranceDbPerOctave;

    /// <summary>The side's two shapes against the asked edges, averaged over whichever of them could be read.</summary>
    /// <remarks>
    /// Each edge is asked at its OWN corner, as the goal is written and as the EQ stage then aims at it
    /// (<c>VirtualDspEqHandoff.GoalCrossoverFor</c>). A split pair judged at one shared corner would read the
    /// far edge as steeper than it is, and charge it for a slope it draws exactly.
    /// </remarks>
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

        // A side whose passband is outside the read band carries no reference and is left out rather than guessed at.
        double charge = lower == null ? upper!.ChargeDb
            : upper == null ? lower.ChargeDb
            : 0.5 * (lower.ChargeDb + upper.ChargeDb);
        double residual = lower == null ? upper!.ResidualDb
            : upper == null ? lower.ResidualDb
            : 0.5 * (lower.ResidualDb + upper.ResidualDb);
        return new JunctionAcousticFit(
            charge,
            residual,
            lower?.SlopeDbPerOctave,
            upper?.SlopeDbPerOctave,
            lower?.TargetSlopeDbPerOctave ?? upper?.TargetSlopeDbPerOctave);
    }

    /// <summary>Slopes are FALL per octave away from the corner (positive), fitted the same way for the asked edge and
    /// for the achieved curve, so the two compare with each other rather than with a nameplate figure.</summary>
    private sealed record AcousticChannelFit(
        double ChargeDb,
        double ResidualDb,
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

        // The level is free, so it is taken out first: the MEDIAN of what the two curves differ by across the
        // passband side. A median rather than a mean or an envelope percentile — one broad bump in the passband
        // should not shift the whole comparison and turn a curable residual into "too steep" — and against the asked
        // edge rather than against flat, since a shallow edge droops well inside its own corner.
        // No passband read, no verdict: the side is left alone rather than guessed at.
        var offsets = new List<double>();
        foreach (SignalPoint point in plant)
        {
            if (!InAcousticWindow(point.X, cornerHz))
            {
                continue;
            }

            if (upper ? point.X > cornerHz : point.X < cornerHz)
            {
                offsets.Add(AchievedDb(point, applied, rateHz) - EdgeDb(asked, point.X, rateHz));
            }
        }

        if (offsets.Count < AcousticMinimumBins)
        {
            return null;
        }

        offsets.Sort();
        double reference = offsets.Count % 2 == 1
            ? offsets[offsets.Count / 2]
            : 0.5 * (offsets[offsets.Count / 2 - 1] + offsets[offsets.Count / 2]);
        var charged = new List<(double Weight, double Deviation)>();
        var achieved = new MagnitudeSlopeFit();
        var wanted = new MagnitudeSlopeFit();
        foreach (SignalPoint point in plant)
        {
            if (!InAcousticWindow(point.X, cornerHz))
            {
                continue;
            }

            // From the corner OUTWARDS: the decibels just past it are where the orders differ most, and they are
            // also where the EQ stage's no-boost region begins, so cutting the charge short there would blind the
            // search to exactly the part it cannot have fixed later.
            if (upper ? point.X > cornerHz : point.X < cornerHz)
            {
                continue;
            }

            double askedDb = EdgeDb(asked, point.X, rateHz);
            if (askedDb < -AcousticChargeFloorDb)
            {
                continue;
            }

            // Steeper than asked reads BELOW the asked edge and only a skirt boost would fix it, which the EQ stage
            // refuses; softer reads above it and a cut lands it.
            double levelDb = AchievedDb(point, applied, rateHz) - reference;
            double weightHere = 1.0 / point.X;
            charged.Add((weightHere, levelDb - askedDb));
            achieved.Add(point.X, levelDb, weightHere);
            wanted.Add(point.X, askedDb, weightHere);
        }

        if (charged.Count < AcousticMinimumBins)
        {
            return null;
        }

        // The worst fifth of the region's weight is dropped before averaging. Smoothing alone would not do this: a
        // mean integrates, so a deep narrow notch keeps its decibel-octaves whatever resolution it is read at. A
        // feature that narrow is the seat's doing and the crossover cannot fix it, while a wrong slope covers the
        // whole region and survives the trim — which is the distinction the charge has to make.
        charged.Sort((left, right) => Math.Abs(right.Deviation).CompareTo(Math.Abs(left.Deviation)));
        double region = charged.Sum(bin => bin.Weight);
        double trimmed = 0;
        double charge = 0;
        double residual = 0;
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
            residual += binWeight * deviation;
            weight += binWeight;
        }

        if (!(weight > 0))
        {
            return null;
        }

        return new AcousticChannelFit(
            charge / weight,
            residual / weight,
            achieved.DbPerOctave is { } slope ? Math.Abs(slope) : null,
            wanted.DbPerOctave is { } askedSlope ? Math.Abs(askedSlope) : null);
    }

    private static bool InAcousticWindow(double frequencyHz, double cornerHz) =>
        frequencyHz > 0 && Math.Abs(Math.Log2(frequencyHz / cornerHz)) <= AcousticWindowOctaves;

    /// <summary>The plant with the candidate edge on it — the acoustic response the device would produce.</summary>
    private static double AchievedDb(SignalPoint plant, CrossoverSpec? applied, int rateHz) =>
        applied == null ? plant.Y : plant.Y + EdgeDb(applied, plant.X, rateHz);

    private static double EdgeDb(CrossoverSpec spec, double frequencyHz, int rateHz) =>
        20 * Math.Log10(Math.Max(
            CrossoverFilter.Response(spec, frequencyHz, rateHz).Magnitude, 1e-12));

    /// <summary>One point per sixth of the corner lattice's step (1/24 octave), levels averaged in dB: a slope is a
    /// trend, and the room's bin-to-bin ripple is not part of it. Also what keeps the per-candidate arithmetic small.
    /// The full-range target is subtracted here, so everything downstream reads shapes with the tonal goal already
    /// out of them.</summary>
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
        return Smooth(thinned);
    }

    /// <summary>A moving average of <see cref="AcousticSmoothingOctaves"/> over log frequency, on the thinned grid.</summary>
    private static List<SignalPoint> Smooth(List<SignalPoint> curve)
    {
        if (curve.Count < 3)
        {
            return curve;
        }

        double halfWidth = AcousticSmoothingOctaves / 2;
        var smoothed = new List<SignalPoint>(curve.Count);
        for (int i = 0; i < curve.Count; i++)
        {
            double centre = Math.Log2(curve[i].X);
            double total = 0;
            int count = 0;
            for (int j = i; j >= 0 && centre - Math.Log2(curve[j].X) <= halfWidth; j--)
            {
                total += curve[j].Y;
                count++;
            }
            for (int j = i + 1; j < curve.Count && Math.Log2(curve[j].X) - centre <= halfWidth; j++)
            {
                total += curve[j].Y;
                count++;
            }

            smoothed.Add(new SignalPoint(curve[i].X, total / count));
        }

        return smoothed;
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
            // The floor is the summation mode's: a 6 dB/oct edge protects nothing there. Against a stated acoustic
            // slope the driver's own fall is the protection, and a soft edge is often exactly what lands on it.
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

    /// <summary>A corner the user can be shown and a processor can be given: the channel card states the
    /// frequency with no decimals, so a fractional edge would be a filter the panel displays as one number
    /// and runs as another.</summary>
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

            var ranking = new List<JunctionTuneReading>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                JunctionTuneReading? reading = Read(i, lowPass, highPass, replaceEdges, rankingLowHz, rankingHighHz);
                if (reading == null)
                {
                    return null;
                }

                ranking.Add(reading);
            }

            var candidate = new JunctionTuneCandidate(lowPass, highPass, [], ranking, bandLowHz, bandHighHz);
            return ownBand ? ReadOwnBand(candidate) : candidate;
        }

        public JunctionTuneCandidate? ReadOwnBand(JunctionTuneCandidate candidate)
        {
            var own = new List<JunctionTuneReading>(sides.Count);
            for (int i = 0; i < sides.Count; i++)
            {
                JunctionTuneReading? reading = Read(
                    i, candidate.LowerLowPass, candidate.UpperHighPass, replaceEdges: true,
                    candidate.BandLowHz, candidate.BandHighHz);
                if (reading == null)
                {
                    return null;
                }

                own.Add(reading);
            }

            return candidate with { Sides = own };
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

        /// <summary>
        /// A candidate's reading, taken AFTER the delay and polarity the upper channel would be re-aligned to for
        /// it. A junction tune is followed by re-aligning the delays, and each slope moves the phase through the
        /// handover by its own group delay: read at the delays set for the crossover on screen, every other
        /// candidate - a softer one above all - would be charged for a misalignment the next Auto delay removes,
        /// and the search would keep returning the crossover the delays were set for. The current crossover is
        /// read the same way, so the comparison stays fair.
        /// </summary>
        private JunctionTuneReading? Read(
            int side, CrossoverEdge? lowPass, CrossoverEdge? highPass, bool replaceEdges,
            double bandLowHz, double bandHighHz)
        {
            double junctionHz = AlignmentCornerHz(lowPass, highPass, bandLowHz, bandHighHz);
            JunctionTuneReading? reading = ReadAligned(
                side,
                ChainFor(side, upper: false, lowPass, replaceEdges),
                ChainFor(side, upper: true, highPass, replaceEdges),
                bandLowHz,
                bandHighHz,
                CrossoverAutoSetup.PostCheckHalfWindowMs(junctionHz));
            if (reading == null || options.AcousticTarget is not { } target || Plants is not { } plants)
            {
                return reading;
            }

            // The asked edges are drawn at the CANDIDATE's corners: LR24 at 500 Hz and LR24 at 700 Hz are both
            // answers to "acoustic LR24", and where the handover sits is what the corner window and the sum decide.
            // Each edge at its own corner, so a split pair is asked what its goal will say. The candidate multiplies
            // the plant arithmetically, exactly as the device will.
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

        /// <summary>What the channels do through this region by themselves — the plant, with the facing edge out of
        /// the chain and everything else (PEQ, FIR, the opposite edge) left as it runs. A filter only steepens, so
        /// this is the physics a stated slope has to live with.</summary>
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

        /// <summary>The side's two plants: the caller's own magnitude curves where it has them (a spatial average is
        /// what the EQ stage will work on), else read off the gated impulse responses with the facing edges out.</summary>
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

            // The PEQ comes off: the bank is refitted right after this tune, and choosing a filter against a bank
            // that is about to vanish makes the answer depend on the tune's history. A correction FIR stays - the EQ
            // stage does not rewrite that - and the facing edge comes off because it is the variable.
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

        // Same search and tie-breaks as the wizard post-check. Empty for a side with no candidate.
        public List<JunctionTuneAlignment> AfterDelay(JunctionTuneCandidate candidate, bool replaceEdges)
        {
            var result = new List<JunctionTuneAlignment>(sides.Count);
            double junctionHz = AlignmentCornerHz(
                candidate.LowerLowPass, candidate.UpperHighPass, candidate.BandLowHz, candidate.BandHighHz);
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

        /// <summary>The corner a re-alignment window is drawn for: the lower of two split corners, where the group
        /// delay and so the reach of a re-alignment is larger. One rule for the readings and for the delay line that
        /// reports them, so the delay the report names is the one the figures were read at.</summary>
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
