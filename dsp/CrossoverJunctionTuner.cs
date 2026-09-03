using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// One side of the junction the tuner reads: the two channels' RAW measured
/// responses and the chains they currently run through — gain, delay,
/// polarity, crossover and PEQ, all of which the tuner keeps except the two
/// facing edges it searches. Both responses must share one sample rate; the
/// chains are applied at <see cref="JunctionTuneOptions.ProcessorSampleRateHz"/>
/// as the DSP would.
/// </summary>
public sealed record JunctionTuneSide(
    string Name,
    Complex[] LowerImpulseResponse,
    DspChannelChain LowerChain,
    Complex[] UpperImpulseResponse,
    DspChannelChain UpperChain,
    int SampleRate);

/// <summary>
/// What the tuner may choose from. <see cref="Slopes"/> null means every slope
/// the family offers at or above the wizard's practical floor (12 dB/oct);
/// <see cref="IndependentSlopes"/> off holds the two facing edges to one slope.
/// The corner window is snapped to the wizard's lattice.
/// </summary>
/// <param name="KeepMarginDb">
/// How much better (per side, on the score) a challenger must be before the
/// current crossover is replaced. A tuned junction is a decision the user made;
/// a few hundredths of a dB do not overrule it.
/// </param>
public sealed record JunctionTuneOptions(
    IReadOnlyList<CrossoverFilterFamily> Families,
    IReadOnlyList<int>? Slopes,
    double MinCrossoverHz,
    double MaxCrossoverHz,
    bool IndependentSlopes,
    int ProcessorSampleRateHz,
    double KeepMarginDb = CrossoverJunctionTuner.DefaultKeepMarginDb);

/// <summary>
/// One side's read of a candidate: the coherent sum at the CURRENT delays and
/// polarity (see <see cref="VirtualCrossoverAnalysis.MeasureJunctionSpectrum"/>)
/// over the candidate's own junction band — an octave each side of its corner,
/// the band the panel's Sum loss row reads a junction on — and the score built
/// from it: lower is better, 0 would be a lossless, flat junction. The ripple
/// is the sum's over that band, the room's own ripple included: what moves
/// between candidates is the crossover's doing, the level is the car's.
/// </summary>
public sealed record JunctionTuneReading(
    string Side,
    double LossDb,
    double DipDb,
    double RippleDb)
{
    /// <summary>
    /// The loss (as a positive figure), the dip's excess over it at half
    /// weight (a cancellation trough counts beyond its share of the average,
    /// as in the wizard's post-check), and the ripple of the sum.
    /// </summary>
    public double ScoreDb =>
        -LossDb +
        CrossoverJunctionTuner.DipPenaltyWeight * (LossDb - DipDb) +
        CrossoverJunctionTuner.RippleWeight * RippleDb;
}

/// <summary>
/// One crossover for the junction — the lower channel's low-pass and the upper
/// channel's high-pass — with its readings per side. A null edge in the
/// CURRENT candidate means that channel has no such edge today.
/// </summary>
/// <param name="Sides">
/// The readings on the candidate's OWN junction band, an octave each side of
/// its corner (<see cref="BandLowHz"/>–<see cref="BandHighHz"/>): what the
/// panel's Sum loss row and the package would print for it.
/// </param>
/// <param name="RankingSides">
/// The readings on the one band every candidate of the tune shares
/// (<see cref="JunctionTuneResult.RankingBandLowHz"/>–<see cref="JunctionTuneResult.RankingBandHighHz"/>):
/// what the candidates are ranked on. Two candidates read on two different
/// bands are not comparable — the car's own ripple differs between the
/// bands by more than a corner decides — so the ranking reads them all on
/// one, and the own-band readings say what the user will see.
/// </param>
public sealed record JunctionTuneCandidate(
    CrossoverEdge? LowerLowPass,
    CrossoverEdge? UpperHighPass,
    IReadOnlyList<JunctionTuneReading> Sides,
    IReadOnlyList<JunctionTuneReading> RankingSides,
    double BandLowHz,
    double BandHighHz)
{
    /// <summary>The mean of the sides' own-band scores.</summary>
    public double ScoreDb => Sides.Count == 0 ? double.PositiveInfinity : Sides.Average(side => side.ScoreDb);

    /// <summary>The mean of the sides' shared-band scores: what the candidates are ranked on.</summary>
    public double RankingScoreDb =>
        RankingSides.Count == 0 ? double.PositiveInfinity : RankingSides.Average(side => side.ScoreDb);
}

/// <summary>
/// What a junction would measure after the delay the production alignment
/// would pick for it — the extra delay on the UPPER channel, the polarity that
/// channel would END UP with, and the loss and dip there. Says how much of what
/// remains is timing's to take, which the crossover cannot.
/// </summary>
/// <param name="InvertUpper">
/// The upper channel's resulting polarity, not a flip of what it runs now: the
/// search works on the response the chain already inverted, and a reader who
/// took its relative answer for an absolute one would propose the opposite.
/// </param>
public sealed record JunctionTuneAlignment(
    string Side,
    double ExtraDelayMs,
    bool InvertUpper,
    double LossDb,
    double DipDb);

/// <summary>The outcome of one junction tune.</summary>
/// <param name="Changed">
/// Whether <see cref="Best"/> beats <see cref="Current"/> by the keep margin
/// on the shared band AND reads no worse on its own band. When false the
/// current crossover stands and <see cref="Best"/> is the challenger that did
/// not make it.
/// </param>
/// <param name="RunnersUp">The next best candidates after <see cref="Best"/>, best first.</param>
/// <param name="RankingBandLowHz">
/// The band every candidate was ranked on: an octave outside the corner
/// window and the current corner, inside the audio band and what was measured.
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
    double RankingBandHighHz);

/// <summary>
/// One side's chains for a probe variant: what the two channels of the junction
/// would run if this variant were applied. The measurements are the side's own,
/// so a variant is a chain change and nothing else.
/// </summary>
public sealed record JunctionProbeChains(DspChannelChain Lower, DspChannelChain Upper);

/// <summary>
/// One thing a probe was asked to read: a label for the reply to name it by,
/// and the chains it would run, one entry per side of the probe in the same
/// order. The variant that carries the chains as they stand is the baseline —
/// a probe reads it beside the others rather than assuming anything.
/// </summary>
public sealed record JunctionProbeVariant(string Label, IReadOnlyList<JunctionProbeChains> Sides);

/// <summary>
/// One variant as the junction measures under it, written nowhere.
/// <paramref name="Sides"/> is the reading on the variant's OWN junction band
/// (<paramref name="BandLowHz"/>–<paramref name="BandHighHz"/>), which is what
/// the panel and the package show for it; <paramref name="SharedBandSides"/> is
/// the reading on the one band every variant of the probe shares, the only one
/// variants whose corners differ may be compared on. <paramref name="AfterDelay"/>
/// is what the junction would measure once the alignment had been re-run for
/// THIS variant — the fair comparison, since the delays in the tune were set
/// for the tune as it stands.
/// </summary>
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

/// <summary>
/// One side's cross-phase read of a probe variant, over the same direct-sound
/// window the variant's sums were read on: the steady-state analysis of the
/// two processed responses. Comparable BETWEEN the entries of one probe — which
/// is what a before-and-after question asks — rather than against the package's
/// own junction phase, which the panel reads through its gate at a fixed
/// frequency-dependent window. Null where the pair's phase is not consistent
/// enough across the band to read.
/// </summary>
public sealed record JunctionProbePhase(string Side, JunctionPhaseResult? Result);

/// <summary>What a probe read: one entry per variant, in the order asked for.</summary>
public sealed record JunctionProbeResult(
    IReadOnlyList<JunctionProbeEntry> Entries,
    double SharedBandLowHz,
    double SharedBandHighHz);

/// <summary>
/// One delay the alignment search weighed at a junction: the extra delay on the
/// UPPER channel, its polarity, the score the search ranked it by and the loss
/// and dip it would leave. <paramref name="Chosen"/> marks the one the
/// production selection would take.
/// </summary>
/// <param name="InvertUpper">
/// The upper channel's resulting polarity, as in <see cref="JunctionTuneAlignment"/>.
/// </param>
public sealed record JunctionDelayProbeCandidate(
    double ExtraDelayMs,
    bool InvertUpper,
    double ScoreDb,
    double LossDb,
    double DipDb,
    bool Chosen);

/// <summary>One side's delay probe: the band searched, the window, and the candidates.</summary>
public sealed record JunctionDelayProbeSide(
    string Side,
    double BandLowHz,
    double BandHighHz,
    double SearchHalfWindowMs,
    IReadOnlyList<JunctionDelayProbeCandidate> Candidates,
    string? Unavailable);

/// <summary>
/// Tunes ONE junction of a tuned system: searches the lower channel's low-pass
/// and the upper channel's high-pass — corner, family, slopes — and scores each
/// candidate on the junction as the car would play it, the two channels'
/// measured responses through their whole current chains (delay, polarity,
/// gain and PEQ kept) summed coherently, on every side the pair is measured
/// on. Everything else the crossover wizard decides — the other junctions, the
/// gains, the chain order — is left exactly as it is; a crossover is one
/// electrical filter, so one result is meant for both sides.
/// <para>
/// This is deliberately not the wizard's objective. The wizard reads
/// magnitudes under an ideal-alignment assumption, anchors on 24 dB/oct and
/// levels the channels, which is right for a blank tune and wrong for a
/// finished one: on a finished tune the phase the two drivers actually put
/// into the junction at their current delays is the whole question, and a
/// steeper slope that narrows the band where a ragged excess phase can
/// interfere is a legitimate answer the magnitude cannot see. So the score is
/// the coherent sum's loss and dip plus its ripple, with no preference for
/// any slope, and the current crossover keeps its place unless a challenger
/// clearly beats it (<see cref="JunctionTuneOptions.KeepMarginDb"/>).
/// </para>
/// </summary>
public static class CrossoverJunctionTuner
{
    public const double DefaultKeepMarginDb = 0.5;
    public const double DipPenaltyWeight = 0.5;
    public const double RippleWeight = 1.0;

    /// <summary>
    /// The gentlest slope the search considers on its own, as the wizard's:
    /// a 6 dB/oct edge protects nothing. A request may still name it.
    /// </summary>
    public const int PracticalSlopeFloorDbPerOctave = 12;

    /// <summary>
    /// How many runners-up the result lists after the best.
    /// </summary>
    public const int RunnersUpReported = 3;

    /// <summary>How many delays a delay probe reports per side, best score first.</summary>
    public const int DelayProbeCandidatesReported = 5;

    // The direct-sound crop the readings run on: the gated spectra only ever
    // read the direct sound near the peak, so the capture tail is cost without
    // information — and the chains, run once per edge over the whole crop,
    // are the tune's costly half. The crop is sized to the ranking band's
    // gate (about nine periods of its low edge, with room for a chain delay
    // and the fades) and capped at the wizard's post-check length.
    private const int MaxCropLength = 32_768;
    private const int MinCropLength = 8_192;
    private const int CropPrePeakSamples = 4_096;
    private const double CropGatePeriods = 10.0;
    private const double CropMarginSeconds = 0.08;

    // Adjacent corner probes closer than this ratio are thinned out: the 50 Hz
    // lattice above 1 kHz is finer than any crossover decision, and every
    // probe costs two chains and two gated FFTs per side.
    private static readonly double MinProbeRatio = Math.Pow(2.0, 1.0 / 24.0);

    /// <summary>
    /// Tunes the junction. <paramref name="sides"/> holds every side the pair is
    /// measured on (one for a mono junction, two for a stereo one).
    /// </summary>
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

        // The current facing edges, read off the first side: a crossover is one
        // filter for both sides, and the panel writes it that way.
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

        // One band for the ranking: an octave outside the whole window and the
        // current corner, so every candidate's overlap region is inside it
        // and the car's own ripple is the same term for all of them.
        (double rankingLowHz, double rankingHighHz) = (
            Math.Max(20, Math.Min(options.MinCrossoverHz, currentHz) / 2),
            Math.Min(Math.Min(20_000, nyquistHz), Math.Max(options.MaxCrossoverHz, currentHz) * 2));
        if (rankingHighHz <= rankingLowHz * 1.05)
        {
            throw new ArgumentException("The corner window leaves no band to read.", nameof(options));
        }

        // The direct-sound crop, once per side; every candidate's chains run
        // on it. The crop keeps the two channels' shared time frame.
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

        var work = new Work(sides, cropped, options, nyquistHz, rankingLowHz, rankingHighHz);
        JunctionTuneCandidate current = work.Evaluate(
                currentLowPass, currentHighPass, currentHz, replaceEdges: false, ownBand: true)
            ?? throw new InvalidOperationException(
                "The junction's current crossover cannot be read: the band holds no usable bins.");

        // The probes are ranked on the shared band alone; their own-band read
        // is taken afterwards for the few that are reported, since a gated
        // read is the costly half of a probe.
        List<(CrossoverEdge LowPass, CrossoverEdge HighPass)> probes = Probes(
            options, currentLowPass, currentHighPass);
        var evaluated = new JunctionTuneCandidate?[probes.Count];
        Parallel.For(0, probes.Count, index =>
        {
            (CrossoverEdge lowPass, CrossoverEdge highPass) = probes[index];
            evaluated[index] = work.Evaluate(
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

        List<JunctionTuneCandidate> reported = ranked
            .Take(1 + RunnersUpReported)
            .Select(candidate => work.ReadOwnBand(candidate) ?? candidate)
            .ToList();

        // The margin on the shared band, and the challenger's own-band read no
        // worse than the current's: a win the user's own read-outs would not
        // show is a win on paper.
        JunctionTuneCandidate best = reported[0];
        bool changed = best.RankingScoreDb < current.RankingScoreDb - options.KeepMarginDb &&
            best.Sides.Count > 0 &&
            best.ScoreDb <= current.ScoreDb &&
            !SameEdges(best, current);

        List<JunctionTuneAlignment> currentAfterDelay = work.AfterDelay(current, replaceEdges: false);
        List<JunctionTuneAlignment> bestAfterDelay = work.AfterDelay(best, replaceEdges: true);
        return new JunctionTuneResult(
            current,
            best,
            changed,
            reported.Skip(1).ToList(),
            currentAfterDelay,
            bestAfterDelay,
            ranked.Count,
            rankingLowHz,
            rankingHighHz);
    }

    /// <summary>
    /// The band a junction with its corner at <paramref name="cornerHz"/> is
    /// read on: an octave each side, inside the audio band and what was
    /// measured — the panel's own junction band.
    /// </summary>
    public static (double LowHz, double HighHz) JunctionBand(double cornerHz, double nyquistHz) =>
        (Math.Max(20, cornerHz / 2), Math.Min(Math.Min(20_000, nyquistHz), cornerHz * 2));

    /// <summary>
    /// Reads a junction under each of the given variants and writes NOTHING:
    /// the answer to "what would these do", for a reply to read before it
    /// proposes anything — a crossover it is considering, a PEQ bank it wants
    /// to try, or the tune as it stands as the baseline beside them. Each
    /// variant is read twice — on its own junction band (an octave each side of
    /// its corner, what the panel and the package show for it) and on one band
    /// every variant shares, the only reading variants whose corners differ may
    /// be compared on — and each is given the delay the production alignment
    /// would pick for it, since the delays in the tune were set for the tune as
    /// it stands and judging a candidate on them is not a fair comparison.
    /// </summary>
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
        var corners = new List<double>();
        foreach (JunctionProbeVariant variant in variants)
        {
            foreach (JunctionProbeChains chains in variant.Sides)
            {
                corners.Add(CornerOf(chains));
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
            // The first side's edges name the variant: a crossover is one
            // filter for both sides, and a variant that changes only a PEQ
            // carries the edges it already had.
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

    // The corner a chain pair hands over at: the lower channel's low-pass, else
    // the upper's high-pass, else the geometric middle of the audio band —
    // which only happens where neither channel is filtered at all.
    private static double CornerOf(JunctionProbeChains chains) =>
        LowPassOf(chains.Lower)?.FrequencyHz ?? HighPassOf(chains.Upper)?.FrequencyHz ?? 632.0;

    /// <summary>
    /// What an alignment search would find at this junction as it stands, and
    /// writes NOTHING: per side, the delay and polarity the production selection
    /// would pick for the UPPER channel and the rival local optima it weighed,
    /// with the loss each would leave. The answer to "what would Auto delay do
    /// here", junction by junction, without moving anything.
    /// </summary>
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
            // The pick always appears, even when the score order cut it: it is
            // the one figure the reader is after.
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

    /// <summary>
    /// The lower channel's chain with its low-pass replaced by
    /// <paramref name="lowPass"/> (added where it had none), everything else as
    /// it was. What the panel writes when a tune is applied.
    /// </summary>
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
        return chain with { Crossover = replaced };
    }

    /// <summary>
    /// The upper channel's chain with its high-pass replaced by
    /// <paramref name="highPass"/> (added where it had none).
    /// </summary>
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
        return chain with { Crossover = replaced };
    }

    // The polarity the upper channel would END UP with. The search reads the
    // response the chain has already inverted, so its answer is a flip of THAT:
    // reported as it stands, a channel running inverted whose best alignment is
    // to stop being inverted would read "invert it", which is the opposite of
    // what a reply should propose.
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

    // Every (corner, family, lower slope, upper slope) the options admit, on the
    // wizard's lattice thinned to about 1/24 octave. Both edges share one
    // corner and one family — a junction is one crossover — and the slopes are
    // free per edge only when the options say so.
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
        // The window's top is always probed: thinning must not drop the very
        // edge the request asked for.
        if (corners.Count > 0 && corners[^1] < lattice[^1])
        {
            corners.Add(lattice[^1]);
        }

        foreach (CrossoverFilterFamily family in options.Families.Distinct())
        {
            List<int> slopes = CrossoverFilter.SupportedSlopes(family)
                .Where(slope => options.Slopes == null
                    ? slope >= PracticalSlopeFloorDbPerOctave
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

    // A Chebyshev edge keeps the ripple the channel already has; every other
    // family ignores the field and takes the default.
    private static double RippleFor(CrossoverFilterFamily family, CrossoverEdge? current) =>
        family == CrossoverFilterFamily.Chebyshev && current is { Family: CrossoverFilterFamily.Chebyshev } edge
            ? edge.RippleDb
            : 1.0;

    // One tune's working set: the sides, their crops, and a cache of each
    // channel's processed response per edge — a lower channel's response
    // depends on its low-pass alone, so the slope combinations of one corner
    // share it, and the chains (the costly part, biquad cascades over the
    // whole crop) run once per edge rather than once per pair.
    private sealed class Work
    {
        private readonly IReadOnlyList<JunctionTuneSide> sides;
        private readonly (Complex[] Lower, Complex[] Upper)[] cropped;
        private readonly JunctionTuneOptions options;
        private readonly double nyquistHz;
        private readonly double rankingLowHz;
        private readonly double rankingHighHz;
        // Keyed by the CHAIN, which is a record: two variants that end up
        // running the same chain on the same channel share one run, and the
        // chains are the costly half of a probe or a tune.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            (int Side, bool Upper, DspChannelChain Chain), (Complex[] Response, ValidSampleRange Range)> processed = new();

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

        // The candidate's readings on every side: each channel through its
        // chain with the candidate edge in place of its own, then the pair
        // summed at the chains' delays and polarities — over the shared
        // ranking band, and over the candidate's own band (an octave each side
        // of its corner) when asked. Null when any side's band cannot be read.
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

        // The candidate with its own-band readings filled in; null when a side's
        // own band cannot be read.
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

        /// <summary>
        /// One probe variant's readings over the given band, one per side; null
        /// when a side's band cannot be read.
        /// </summary>
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

        /// <summary>
        /// One probe variant's cross-phase read per side, over the same window
        /// the sums were read on.
        /// </summary>
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

        /// <summary>What one probe variant would measure after its own best delay.</summary>
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

        private JunctionTuneReading? Read(
            int side, CrossoverEdge? lowPass, CrossoverEdge? highPass, bool replaceEdges,
            double bandLowHz, double bandHighHz) =>
            Read(
                side,
                ChainFor(side, upper: false, lowPass, replaceEdges),
                ChainFor(side, upper: true, highPass, replaceEdges),
                bandLowHz,
                bandHighHz);

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

        // What the junction would measure after the delay the production
        // alignment search would pick for the UPPER channel — the same search
        // and the same tie-breaks as the wizard's post-check, around the
        // current timing. Empty for a side the search finds nothing on.
        public List<JunctionTuneAlignment> AfterDelay(JunctionTuneCandidate candidate, bool replaceEdges)
        {
            var result = new List<JunctionTuneAlignment>(sides.Count);
            double junctionHz = candidate.LowerLowPass?.FrequencyHz
                ?? candidate.UpperHighPass?.FrequencyHz
                ?? Math.Sqrt(candidate.BandLowHz * candidate.BandHighHz);
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

        // The chain a side's channel runs with the candidate edge in place of
        // its own; the chain as it stands where the edge is null or the caller
        // is reading the current crossover. A replacement that lands on the
        // edge the chain already had produces an equal chain, so the two read
        // through the same cache entry.
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
