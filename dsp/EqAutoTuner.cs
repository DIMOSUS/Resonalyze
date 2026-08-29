using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// Automatically fits a set of PEQ bands (plus a preamp) so that adding the
/// resulting <see cref="EqualizationCurve"/> to a measured source curve approximates
/// a target curve. The number of bands is chosen automatically: the fit adds bands
/// greedily where the residual error is largest and stops once the remaining error
/// is negligible or the band budget is exhausted.
/// </summary>
/// <remarks>
/// <para>
/// The bell is the fit's ordinary band, and with <see cref="Options.AllowShelves"/>
/// off it is the only shape it places: the greedy pass puts a bell where the residual
/// is worst, which is right for a resonance and wrong for a trend. A shelved target —
/// a car curve is bass-lifted and tilted down — leaves whole octaves at one end of the
/// residual off-target, and a stack of bells approximates that badly: slots spent on a
/// trend, and skirts ringing between the centres.
/// </para>
/// <para>
/// Switching shelves on adds one stage in front of the greedy pass, which may place a
/// low and a high shelf. Candidates are scored by the same objective the bells are
/// scored by, and the winner is kept only if FINISHING the fit with it lands closer to
/// the target than finishing it without — both runs made on scratch state, so a shelf
/// that would cost more than the slot it takes is simply never applied.
/// </para>
/// <para>
/// All-pass bands are never fitted, whatever the options say: an all-pass is flat, so
/// a magnitude error can never ask for one. Callers replace the whole bank with the
/// result, so bands dialled in by hand — shelves included — do not survive a re-fit.
/// </para>
/// </remarks>
public static class EqAutoTuner
{
    public sealed record Options
    {
        /// <summary>Upper bound on the number of bands produced.</summary>
        public int MaxBands { get; init; } = EqualizationCurve.MaxBandCount;

        public double MinFrequencyHz { get; init; } = 20;
        public double MaxFrequencyHz { get; init; } = 20_000;

        /// <summary>Per-band gain limits (a band cut/boost is clamped to this range).</summary>
        public double BandGainMinDb { get; init; } = -15;
        public double BandGainMaxDb { get; init; } = 6;

        public double QMin { get; init; } = 0.5;
        public double QMax { get; init; } = 10;

        public double PreampMinDb { get; init; } = -30;
        public double PreampMaxDb { get; init; } = 30;

        /// <summary>
        /// Ceiling on the TOTAL EQ gain (preamp + summed bands) at any
        /// frequency. A positive preamp stacked under boost bands is a
        /// clipping DSP profile, and handing one out leaves the UI reporting the
        /// damage as negative headroom afterwards. The preamp is capped after
        /// the bands are placed, so the fitted shape stays and the curve
        /// honestly sits below an unreachable target instead.
        /// Unbounded by default: as a pure curve fit the preamp legitimately
        /// carries the level difference between arbitrarily referenced source
        /// and target; a caller producing a clip-safe cuts-only profile passes 0.
        /// With boosts allowed, prefer pinning the preamp instead
        /// (<see cref="PreampMinDb"/> == <see cref="PreampMaxDb"/>): this cap is
        /// applied AFTER the bands are placed, so under it a fit that boosts is
        /// realised below the level its bands were placed against — the whole
        /// curve drops by the peak boost.
        /// </summary>
        public double TotalGainMaxDb { get; init; } = double.PositiveInfinity;

        /// <summary>
        /// Stop adding bands once the largest remaining error is below this many dB.
        /// </summary>
        public double StopResidualDb { get; init; } = 0.5;

        /// <summary>
        /// The footprint (in octaves) sterilised around each placed band, so the fit
        /// does not re-nibble the very same peak it just corrected. Kept small on
        /// purpose: a cluster of narrow peaks spaced wider than this each still gets its
        /// own band, which a coarse spacing would prevent. A boost pinned at the boost
        /// ceiling instead blocks the wider <see cref="SaturatedBlockOctaves"/> span.
        /// </summary>
        public double MinBandSpacingOctaves { get; init; } = 0.1;

        /// <summary>
        /// When a boost is limited by the remaining boost headroom (the correction
        /// is larger than allowed), this much of the spectrum around it is skipped,
        /// so the band budget is not wasted nibbling at an unrecoverable deficit
        /// (for example a low-frequency roll-off that cannot be EQ'd flat).
        /// </summary>
        public double SaturatedBlockOctaves { get; init; } = 1.0;

        /// <summary>Number of logarithmically spaced points the fit works on.</summary>
        public int GridSize { get; init; } = 256;

        /// <summary>Sample rate of the DSP that will realize the fitted RBJ biquads.</summary>
        public double SampleRateHz { get; init; } = 48_000;

        /// <summary>
        /// When true, the fit places only cut bands — it never boosts. Boosting a
        /// reflective cabin's response is where an auto EQ does harm (filling an
        /// interference null wastes headroom and a band on a dip that does not survive a
        /// small mic move); cutting the peaks and leaving level on the table is the
        /// conservative correction, which is why the car-tuning EQ Wizard defaults it on.
        /// It defaults OFF here so the general curve fitter stays unconstrained; even
        /// off, boosts are gated to reliable regions by <see cref="BoostMask"/> (high
        /// coherence, not inside a narrow deep null) and capped by <see
        /// cref="BandGainMaxDb"/>.
        /// </summary>
        public bool CutsOnlyMode { get; init; }

        /// <summary>
        /// The reliability policy that decides, per frequency, whether a boost band may
        /// be placed there — consulted only when <see cref="CutsOnlyMode"/> is off.
        /// </summary>
        public EqBoostabilityMask.Options BoostMask { get; init; } = new();

        /// <summary>
        /// The most cumulative boost a fit may add at a masked-off bin (a low-coherence
        /// bin or a narrow deep null). The reliability mask only clears a boost band's
        /// CENTRE; a wide, low-Q boost centred on a reliable point can still pour several
        /// dB into an adjacent forbidden region through its skirt — quietly filling the
        /// very null the mask meant to protect. Any candidate whose placement would push
        /// the total boost at a forbidden bin past this is rejected, so the fit narrows
        /// the band (or withholds it) instead. Consulted only for boosts; +infinity
        /// restores the unguarded behaviour (skirt spill allowed).
        /// </summary>
        public double ForbiddenRegionMaxBoostDb { get; init; } = 0.5;

        /// <summary>
        /// Lets the fit place a low and a high shelf in front of the greedy bell pass.
        /// Off by default, so a caller that says nothing gets the bells-only curve
        /// earlier versions produced.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A shelf is the honest shape for the ends of a car correction — the target
        /// itself is a bass shelf plus a downward tilt — and one of them replaces the
        /// three or four bells a trend otherwise costs, along with the ringing their
        /// skirts leave between the centres. The stage is deliberately narrow: at most
        /// one shelf per direction, a knee capped at the steepest that still rises
        /// monotonically, and acceptance only when the rest of the fit, run to
        /// exhaustion both ways, ends closer to the target with the shelf than without
        /// it. Weaker rules were tried first and measured: an absolute "improves the
        /// residual by X" bar, and beating the single bell the shelf displaces. Both
        /// spent slots on shelves that left the finished fit worse, because a shelf acts
        /// across octaves and changes what every later band has left to do.
        /// </para>
        /// <para>
        /// A BOOSTING shelf is deliberately treated differently from a boosting bell by
        /// the reliability mask. <see cref="ForbiddenRegionMaxBoostDb"/> exists to stop
        /// a band aimed at a reliable centre from quietly pouring gain into an adjacent
        /// null through its SKIRT. A shelf has no skirt in that sense — its plateau IS
        /// the correction, and it necessarily passes over whatever nulls that end of the
        /// range holds — so applying the per-bin guard to one would refuse every
        /// boosting shelf that could ever be proposed. A shelf is gated on being
        /// JUSTIFIED instead: at least <see cref="ShelfPlateauUsableFraction"/> of its
        /// plateau must be boost-allowed before a positive gain is even searched, and
        /// the gain itself is still bounded by <see cref="BandGainMaxDb"/>. The bells
        /// that follow keep the per-bin guard measured against THEIR own boost alone,
        /// so a shelf's level does not lock them out of the region it covers.
        /// </para>
        /// </remarks>
        public bool AllowShelves { get; init; }
    }

    // Quality factors tried for each band; the one that lowers the residual error
    // the most is kept, so narrow peaks get narrow bands and broad trends get wide
    // bands. Filtered to the configured [QMin, QMax] range at run time.
    private static readonly double[] CandidateQ =
        { 0.5, 0.7, 1.0, 1.4, 2.0, 2.8, 4.0, 5.6, 8.0, 10.0 };

    // Cuts-only over-correction penalty when choosing a band's Q. Over-cutting pushes a
    // point BELOW the target, where no later cut can lift it back (only a forbidden boost
    // could). Each candidate is charged for the over-cut this band ADDS at a point — the
    // part of its own cut that lands below the target — with the first
    // CutsOnlyOverCutFreeDb of it free. Charging the band's own contribution (never the
    // pre-existing depth) matters: a real response weaves below the target all over, and
    // any penalty that scales with the depth a point ALREADY had makes every wide Q lose
    // to the narrowest one — that shredded a smooth response into a swarm of Q=10 slivers
    // whose notch-comb looked worse than no EQ. With the free zone, a moderately wide
    // skirt grazing a neighbouring dip by up to 1 dB costs nothing, so the residual RMS
    // alone picks the band width; only a skirt that digs several dB below the target (the
    // visible gouge) is weighted up and loses to a tighter band. Under-correction (above
    // target) is always freely fixable and unpenalised.
    private const double CutsOnlyOverCorrectionWeight = 25.0;
    private const double CutsOnlyOverCutFreeDb = 1.0;

    // Shelf policy (Options.AllowShelves). Every number here is about "is there really
    // a trend at that end of the range": a shelf that is wrong is wrong across octaves,
    // so none of these may be decided by a single point.

    // The directions a shelf may take, in the order the stage searches them. Both are
    // scored every round and the better one wins, so the order is not a preference.
    private static readonly PeqBandType[] ShelfDirections =
        { PeqBandType.LowShelf, PeqBandType.HighShelf };

    // Knees a fitted shelf may take, in the one decimal the slot strips keep — a Q the
    // strips would round is a shelf that is not the one that was scored. Capped at 0.7
    // BY CONSTRUCTION rather than by the caller's QMax: above 1/sqrt(2) an RBJ shelf
    // overshoots its own gain before settling on it, and an overshoot on a CUT is a
    // boost — which cuts-only mode promises never to produce and which the reliability
    // mask may well have refused at that frequency. Measured over the whole corner and
    // gain ladder at 48 kHz, the most a cutting shelf lifts anywhere is 0.0000 dB at
    // Q 0.70 and 0.0002 at 0.71, then 0.15 at 0.8, 0.90 at 1.0 and 2.69 at 1.4 — the
    // boundary is exactly where the algebra says it is. QMax bounds how NARROW a BELL
    // may be and says nothing about a knee, so it is not applied here; QMin is, because
    // it is what the strips accept.
    private static readonly double[] ShelfCandidateQ = { 0.3, 0.4, 0.5, 0.7 };

    // How much of the fitting range must lie on a shelf's UNAFFECTED side — below a
    // high shelf, above a low one. This is what separates a shelf from a level change:
    // a high shelf sitting an octave off the bottom of the range lifts practically
    // everything, which is a preamp wearing a filter slot, and the fit reached for
    // exactly that when the mask refused it the shelf it actually wanted. Two octaves
    // of untouched range is the price of calling the band a shelf.
    private const double ShelfSettledMarginOctaves = 2.0;

    // And how much must lie on the side it DOES act on, so there is a plateau to decide
    // about rather than a corner hanging off the end of the range.
    private const double ShelfPlateauSpanOctaves = 1.0;

    // Where that plateau starts, measured outwards from the corner. Half an octave out,
    // a shelf has reached 57% of its gain at the widest knee it may take and 78% at the
    // narrowest — enough of it, at every Q, for the span beyond to be what the shelf is
    // really deciding about rather than part of its transition.
    private const double ShelfPlateauMarginOctaves = 0.5;

    // How much of that plateau must carry usable data before the shelf may be fitted:
    // measured points for a cut, boost-ALLOWED points for a boost. Three quarters, not
    // a bare majority — measured, at three fifths the fit answered a mask that refused
    // to lift an incoherent top by lifting nearly the whole range instead, through a
    // shelf whose plateau cleared that bar by a single point.
    private const double ShelfPlateauUsableFraction = 0.75;

    // Corners tried per octave. A knee is broad: a third of an octave is already finer
    // than the choice can be told apart at.
    private const int ShelfCornersPerOctave = 3;

    // Below this a shelf is not worth a slot. The search steps gain in the same tenth
    // of a dB the strips keep.
    private const double ShelfMinGainDb = 0.5;

    /// <summary>
    /// Fits an equalization curve so that <paramref name="source"/> + curve best
    /// matches <paramref name="target"/>. Both curves are (Hz, dB) and need not share
    /// the same frequency points; they are resampled onto a common logarithmic grid.
    /// <paramref name="coherence"/> is an optional (Hz, γ²) curve used only to gate
    /// boosts to reliable regions (see <see cref="Options.BoostMask"/>); passing null
    /// leaves boosting masked by null-detection and the fitting band alone.
    /// </summary>
    public static EqualizationCurve Tune(
        IReadOnlyList<SignalPoint> source,
        IReadOnlyList<SignalPoint> target,
        Options? options = null,
        IReadOnlyList<SignalPoint>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Options opt = options ?? new Options();
        if (!double.IsFinite(opt.SampleRateHz) || opt.SampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Sample rate must be positive.");
        }

        double maxFrequency = Math.Min(opt.MaxFrequencyHz, opt.SampleRateHz * 0.49);
        if (maxFrequency <= opt.MinFrequencyHz)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The fitting range must lie below the DSP Nyquist frequency.");
        }
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(
            opt.MinFrequencyHz,
            maxFrequency,
            opt.GridSize);
        int n = grid.Count;

        double[] sourceDb = Resample(source, grid);
        double[] targetDb = Resample(target, grid);

        // Error only where both curves have data; resampling yields NaN elsewhere.
        var error = new double[n];
        var valid = new bool[n];
        int validCount = 0;
        double errorSum = 0;
        double maxError = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (double.IsFinite(sourceDb[i]) && double.IsFinite(targetDb[i]))
            {
                error[i] = targetDb[i] - sourceDb[i];
                valid[i] = true;
                errorSum += error[i];
                maxError = Math.Max(maxError, error[i]);
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return new EqualizationCurve(Array.Empty<PeqBand>());
        }

        // The preamp absorbs the broadband level difference; bands fit the shape. The
        // right broadband level depends on which way the bands can move the source:
        //
        //  - Boosts allowed: bands correct in both directions, so centre the residual on
        //    the MEAN error and let bands fan out symmetrically.
        //  - Cuts only: bands can only pull the source DOWN. A preamp below the largest
        //    error would drop a point beneath the target, where no cut can lift it back —
        //    it just leaves that point further off. So align to the point where the
        //    source is LEAST above the target (the maximum error): this absorbs only the
        //    excess every point shares, and stays at the ceiling whenever any point is
        //    already at or below the target (nothing there can be pulled down). Rounding
        //    up keeps every point at or above the aligned target, so it stays cuttable.
        //
        // The ceiling is pre-applied here (not only in the post-band clamp) so the bands
        // fit against the same level the curve is finally realised at; in cuts-only the
        // band peak is 0, so 0 (a broadband boost) is the ceiling — cuts-only must never
        // lift the curve, whatever the level difference or an unbounded TotalGainMaxDb.
        double preamp;
        if (opt.CutsOnlyMode)
        {
            double cutsCeiling = double.IsFinite(opt.TotalGainMaxDb)
                ? Math.Min(0.0, Math.Min(opt.PreampMaxDb, opt.TotalGainMaxDb))
                : Math.Min(0.0, opt.PreampMaxDb);
            preamp = Clamp(Math.Ceiling(maxError), opt.PreampMinDb, cutsCeiling);
        }
        else
        {
            preamp = Clamp(Math.Round(errorSum / validCount), opt.PreampMinDb, opt.PreampMaxDb);
        }

        var residual = new double[n];
        for (int i = 0; i < n; i++)
        {
            residual[i] = valid[i] ? error[i] - preamp : 0;
        }

        double[] qCandidates = CandidateQ
            .Where(q => q >= opt.QMin && q <= opt.QMax)
            .ToArray();
        if (qCandidates.Length == 0)
        {
            qCandidates = new[] { Clamp(1.0, opt.QMin, opt.QMax) };
        }

        // Per-point boostability: cuts are always allowed, boosts only where the mode
        // and the reliability mask permit. In cuts-only mode nothing may be boosted, so
        // the mask (and the coherence resample it would need) is skipped entirely.
        bool[] boostAllowed;
        if (opt.CutsOnlyMode)
        {
            boostAllowed = new bool[n]; // all false
        }
        else
        {
            double[]? coherenceGrid = ResampleCoherence(coherence, grid);
            boostAllowed = EqBoostabilityMask.ComputeBoostAllowed(
                grid, sourceDb, valid, coherenceGrid, opt.BoostMask);
        }

        // The unit-circle points every candidate's digital response is evaluated at.
        // Pre-computed once so a candidate costs one biquad build plus a complex
        // division per point, instead of rebuilding the same biquad at every frequency:
        // the arithmetic is exactly what DigitalEqualizationResponse.MagnitudeDbAt
        // performs, which is why the fit still sees the response the DSP will realize.
        var z1 = new Complex[n];
        var z2 = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            z1[i] = Complex.Exp(new Complex(0, -Math.Tau * grid[i] / opt.SampleRateHz));
            z2[i] = z1[i] * z1[i];
        }

        var fit = new FitGrid(grid, z1, z2, valid, boostAllowed, validCount);

        var bands = new List<PeqBand>();
        var contribution = new double[n];
        var bestContribution = new double[n];
        // Running EQ magnitude of the placed bands, used to cap the cumulative boost.
        var eqSum = new double[n];
        // The same running total for the BELLS alone. Both boost guards read this
        // rather than eqSum, because both exist to stop BELLS from piling on each
        // other, and a shelf is not a pile: its plateau is a deliberate correction of a
        // whole end of the range. Charging the bells for it locks them out of every
        // region a shelf covers — measured: a +6 dB bass shelf leaves zero headroom
        // across the bass, every resonance under it is refused for want of it, and the
        // finished fit is worse than the one with no shelf at all. Identical to eqSum
        // whenever no shelf was placed, so a bells-only fit is unchanged. See
        // Options.AllowShelves.
        var bellSum = new double[n];
        // Frequencies excluded from further bands (already corrected as far as they
        // can be, or an unrecoverable boost deficit).
        var blocked = new bool[n];

        // Shelves first, and only where the residual has a trend at one end worth the
        // slot: the greedy pass below then works on what they left, so a bell is never
        // spent re-correcting a tilt a shelf already took out.
        if (opt.AllowShelves)
        {
            PlaceShelves(
                opt, fit, qCandidates, residual, eqSum, bellSum, bands);
        }

        while (bands.Count < opt.MaxBands)
        {
            (PeqBand Band, double Score)? bell = NextBell(
                opt,
                fit,
                qCandidates,
                residual,
                eqSum,
                bellSum,
                blocked,
                contribution,
                bestContribution);
            if (bell == null)
            {
                break;
            }

            bands.Add(bell.Value.Band);
            for (int i = 0; i < n; i++)
            {
                if (valid[i])
                {
                    residual[i] -= bestContribution[i];
                    eqSum[i] += bestContribution[i];
                    bellSum[i] += bestContribution[i];
                }
            }
        }

        // Digital-clipping guard: cap the preamp so preamp + the summed band
        // boost never exceeds TotalGainMaxDb anywhere. Cuts leave bandPeak at
        // 0, so a positive preamp survives only up to the ceiling itself.
        if (double.IsFinite(opt.TotalGainMaxDb))
        {
            double bandPeak = 0;
            for (int i = 0; i < n; i++)
            {
                if (valid[i])
                {
                    bandPeak = Math.Max(bandPeak, eqSum[i]);
                }
            }
            preamp = Clamp(
                Math.Min(preamp, Math.Floor(opt.TotalGainMaxDb - bandPeak)),
                opt.PreampMinDb,
                opt.PreampMaxDb);
        }

        return new EqualizationCurve(bands, preamp);
    }

    /// <summary>
    /// The next bell the greedy pass would place against this residual, with its
    /// contribution left in <paramref name="bestContribution"/> — or null when there is
    /// nothing left it can do.
    /// </summary>
    /// <remarks>
    /// Everything the pass decides about ONE band lives here: which peak to correct,
    /// whether the mode or the reliability mask forbids it, how much boost headroom is
    /// left, which Q fits best, and what footprint the choice sterilises. It stops short
    /// of APPLYING the band, which is what lets the shelf stage ask what a slot would
    /// buy as a bell without spending it — that call passes scratch copies of
    /// <paramref name="blocked"/> and of the two contribution buffers, and throws the
    /// answer away.
    /// </remarks>
    private static (PeqBand Band, double Score)? NextBell(
        Options opt,
        FitGrid fit,
        double[] qCandidates,
        double[] residual,
        double[] eqSum,
        double[] bellSum,
        bool[] blocked,
        double[] contribution,
        double[] bestContribution)
    {
        IReadOnlyList<double> grid = fit.Hz;
        int n = grid.Count;
        while (true)
        {
            int peakIndex = IndexOfLargestResidual(residual, fit.Valid, blocked);
            if (peakIndex < 0 || Math.Abs(residual[peakIndex]) < opt.StopResidualDb)
            {
                return null;
            }

            double desired = residual[peakIndex];

            // A boost the mode or the reliability mask forbids here is not fitted at
            // all: skip the contiguous FORBIDDEN deficit around it, but stop at the
            // first boost-allowed point so the reliable shoulders of a wide dip whose
            // core is a null still get their own bands.
            if (desired > 0 && !fit.BoostAllowed[peakIndex])
            {
                BlockForbiddenBoostRun(
                    blocked, residual, fit.BoostAllowed, fit.Valid, peakIndex);
                continue;
            }

            double gainDb;
            bool boostHeadroomLimited = false;
            if (desired > 0)
            {
                // Limit the boost so the bells' own summed gain here never exceeds the
                // boost ceiling; a roll-off that needs +30 dB gets one capped band, not
                // a stack of them. A shelf under it is not part of that stack (see the
                // bellSum declaration), so a fit that placed one can boost past
                // BandGainMaxDb in total — which is the caller's to bound through
                // TotalGainMaxDb, and the EQ Wizard's to report as headroom.
                double headroom = opt.BandGainMaxDb - bellSum[peakIndex];
                double allowed = Math.Min(desired, Math.Max(0, headroom));
                boostHeadroomLimited = allowed < desired - 0.05;
                gainDb = Math.Round(allowed, 1);
            }
            else
            {
                gainDb = Math.Round(Math.Max(desired, opt.BandGainMinDb), 1);
            }

            if (Math.Abs(gainDb) < 0.05)
            {
                // Nothing useful can be done here (boost headroom exhausted); skip a
                // span around it so the remaining budget helps elsewhere.
                BlockAround(blocked, grid, peakIndex, opt.SaturatedBlockOctaves);
                continue;
            }

            double frequencyHz = Math.Round(grid[peakIndex]);

            // Pick the Q that minimises the residual RMS after this band is applied. A
            // boost band additionally may not push the cumulative boost past
            // ForbiddenRegionMaxBoostDb at any masked-off bin: its centre cleared the
            // mask, but a wide skirt must not fill an adjacent null or low-coherence
            // region. Candidates that would are discarded from the search.
            bool isBoost = gainDb > 0;
            double bestQ = qCandidates[0];
            double bestScore = double.MaxValue;
            bool anyCandidateFits = false;
            foreach (double candidate in qCandidates)
            {
                double q = Math.Round(candidate, 1);
                double score = ScoreBand(
                    new PeqBand(frequencyHz, q, gainDb),
                    opt,
                    fit,
                    residual,
                    isBoost ? bellSum : null,
                    contribution,
                    out bool spillsIntoForbidden);
                if (spillsIntoForbidden)
                {
                    continue;
                }

                anyCandidateFits = true;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestQ = q;
                    Array.Copy(contribution, bestContribution, n);
                }
            }

            if (!anyCandidateFits)
            {
                // Even the narrowest Q would over-fill a masked bin through its skirt;
                // don't boost across it. Sterilise a small footprint and move on so the
                // budget helps elsewhere.
                BlockAround(blocked, grid, peakIndex, opt.MinBandSpacingOctaves);
                continue;
            }

            // Sterilise only a narrow footprint around this band's centre — enough to
            // stop it re-nibbling the very same peak, but far smaller than the old
            // fixed spacing so a cluster of narrow peaks each keeps its own band. A
            // boost pinned at the headroom limit blocks the wider saturated span, since
            // that whole region genuinely cannot improve further.
            BlockAround(
                blocked,
                grid,
                peakIndex,
                boostHeadroomLimited ? opt.SaturatedBlockOctaves : opt.MinBandSpacingOctaves);
            return (new PeqBand(frequencyHz, bestQ, gainDb), bestScore);
        }
    }

    // How good a FINISHED fit is: the mean squared distance from the target, plus — in
    // cuts-only — the same charge the band search makes for landing BELOW it, since a
    // point pushed under the target can no longer be lifted back. Ranks the two
    // candidate fits the shelf lookahead runs, so the decision is made on the state they
    // end in rather than on the single band that starts them.
    // The charge is on what the BANDS did, exactly as it is per band: eqSum is how far
    // this fit pulled the point down, and only the part of that landing under the target
    // is charged. Charging the depth itself instead — every point that ended below the
    // target, whatever put it there — lets a pre-existing deficit the preamp could not
    // absorb dominate the number, and the comparison then turns on a constant both
    // candidates share. Measured: it rejected at a four-band budget the very shelf it
    // had accepted at three, on a response where the shelf was plainly the better fit.
    private static double FinalScore(
        Options opt,
        FitGrid fit,
        double[] residual,
        double[] eqSum)
    {
        double sumSquares = 0;
        for (int i = 0; i < residual.Length; i++)
        {
            if (!fit.Valid[i])
            {
                continue;
            }

            double r = residual[i];
            sumSquares += r * r;
            if (opt.CutsOnlyMode && eqSum[i] < 0)
            {
                double added = Math.Min(-eqSum[i], Math.Max(0, r));
                if (added > CutsOnlyOverCutFreeDb)
                {
                    double over = added - CutsOnlyOverCutFreeDb;
                    sumSquares += CutsOnlyOverCorrectionWeight * over * over;
                }
            }
        }

        return sumSquares / fit.ValidCount;
    }

    // Runs the greedy bell pass to exhaustion on scratch copies of the state and returns
    // the score of what it ends with. Nothing the caller owns is touched, so this is how
    // the shelf stage asks what the rest of the fit would look like either way.
    private static double TrialFit(
        Options opt,
        FitGrid fit,
        double[] qCandidates,
        double[] residual,
        double[] eqSum,
        double[] bellSum,
        int budget)
    {
        int n = fit.Hz.Count;
        var trialResidual = (double[])residual.Clone();
        var trialEqSum = (double[])eqSum.Clone();
        var trialBellSum = (double[])bellSum.Clone();
        var blocked = new bool[n];
        var contribution = new double[n];
        var bestContribution = new double[n];
        for (int placed = 0; placed < budget; placed++)
        {
            if (NextBell(
                opt,
                fit,
                qCandidates,
                trialResidual,
                trialEqSum,
                trialBellSum,
                blocked,
                contribution,
                bestContribution) == null)
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                if (fit.Valid[i])
                {
                    trialResidual[i] -= bestContribution[i];
                    trialEqSum[i] += bestContribution[i];
                    trialBellSum[i] += bestContribution[i];
                }
            }
        }

        return FinalScore(opt, fit, trialResidual, trialEqSum);
    }

    /// <summary>
    /// The fixed part of a fit: the logarithmic frequency grid, the unit-circle points
    /// the digital responses are evaluated at, and which of those points carry data a
    /// band may be judged on at all (<see cref="Valid"/>) or boosted at
    /// (<see cref="BoostAllowed"/>).
    /// </summary>
    private sealed record FitGrid(
        IReadOnlyList<double> Hz,
        Complex[] Z1,
        Complex[] Z2,
        bool[] Valid,
        bool[] BoostAllowed,
        int ValidCount);

    // The fit's objective, and the one place a candidate band becomes numbers: fills
    // contribution with the band's dB contribution at every valid grid point and
    // returns the mean squared residual that would remain, plus the cuts-only
    // over-correction charge described above the constants. Bells and shelves are both
    // scored through here, so a shelf is never accepted on a softer test than the bell
    // whose slot it takes.
    //
    // spillBase is the running boost the skirt guard measures this candidate against,
    // or null when the guard does not apply — a cut, which can never fill a null, or a
    // shelf, whose plateau is the correction rather than a skirt (see
    // Options.AllowShelves).
    private static double ScoreBand(
        PeqBand band,
        Options opt,
        FitGrid fit,
        double[] residual,
        double[]? spillBase,
        double[] contribution,
        out bool spillsIntoForbidden)
    {
        // A degenerate band contributes nothing anywhere; PeqBiquad would still hand
        // back coefficients, so the check DigitalEqualizationResponse makes per point is
        // made once here instead.
        bool transparent = band.IsTransparent;
        BiquadCoefficients coefficients = transparent
            ? default
            : PeqBiquad.Compute(band, opt.SampleRateHz);

        double sumSquares = 0;
        spillsIntoForbidden = false;
        for (int i = 0; i < fit.Hz.Count; i++)
        {
            if (!fit.Valid[i])
            {
                continue;
            }

            double c = transparent
                ? 0
                : 20.0 * Math.Log10(Math.Max(
                    BiquadResponse.Evaluate(coefficients, fit.Z1[i], fit.Z2[i]).Magnitude,
                    double.Epsilon));
            contribution[i] = c;
            if (spillBase != null && !fit.BoostAllowed[i] &&
                spillBase[i] + c > opt.ForbiddenRegionMaxBoostDb)
            {
                spillsIntoForbidden = true;
            }

            // In cuts-only, charge the part of this band's own cut that lands below the
            // target, past the free zone. See the constants above for why the charge is
            // on the band's contribution, never the depth the point already had.
            double r = residual[i] - c;
            sumSquares += r * r;
            if (opt.CutsOnlyMode && c < 0)
            {
                double added = Math.Min(-c, Math.Max(0, r));
                if (added > CutsOnlyOverCutFreeDb)
                {
                    double over = added - CutsOnlyOverCutFreeDb;
                    sumSquares += CutsOnlyOverCorrectionWeight * over * over;
                }
            }
        }

        return sumSquares / fit.ValidCount;
    }

    /// <summary>
    /// Places at most one low and one high shelf against the residual, before the
    /// greedy bell pass ever sees it, updating <paramref name="residual"/>,
    /// <paramref name="eqSum"/> and <paramref name="bands"/> in place.
    /// </summary>
    /// <remarks>
    /// One round per direction. Each searches every direction not yet placed, keeps the
    /// single best candidate across both, and applies it only if that candidate beats
    /// the bell the greedy pass would put in the same slot. Running the second round
    /// against the residual the first one left is what lets a bass shelf and a treble
    /// shelf describe one tilt together, instead of both being fitted to the same slope.
    /// The stage never marks a frequency blocked: a shelf sterilises nothing, and the
    /// bells are meant to work on top of it.
    /// </remarks>
    private static void PlaceShelves(
        Options opt,
        FitGrid fit,
        double[] qCandidates,
        double[] residual,
        double[] eqSum,
        double[] bellSum,
        List<PeqBand> bands)
    {
        int n = fit.Hz.Count;
        var contribution = new double[n];
        var candidateContribution = new double[n];
        var chosenContribution = new double[n];
        var shelfResidual = new double[n];
        var shelfEqSum = new double[n];
        bool lowPlaced = false;
        bool highPlaced = false;
        for (int round = 0;
            round < ShelfDirections.Length && bands.Count < opt.MaxBands;
            round++)
        {
            // Whether a shelf is worth its slot is decided by finishing the fit BOTH
            // ways on scratch state and comparing where the two end up. Nothing cheaper
            // survived measurement: a shelf that lowers the objective on its own, or
            // that beats the single bell it displaces, can still leave the whole fit
            // worse, because it acts across octaves and changes what every later band
            // has left to correct. Both weaker rules were tried and both spent a slot
            // on a shelf that cost more than it bought.
            //
            // EVERY direction still open is taken that far, and the finished score is
            // what ranks them. Ranking the two by their single-band score first and
            // only asking the winner would let a low shelf that reads better before the
            // bells are placed suppress a high shelf that finishes closer, and would
            // let one direction's rejection end the stage with the other never asked.
            // The baseline is the same for both, so it is run once.
            int remaining = opt.MaxBands - bands.Count;
            double withoutShelf = TrialFit(
                opt, fit, qCandidates, residual, eqSum, bellSum, remaining);

            PeqBand chosen = default;
            double chosenFinal = withoutShelf;
            bool found = false;
            foreach (PeqBandType type in ShelfDirections)
            {
                if (type == PeqBandType.LowShelf ? lowPlaced : highPlaced)
                {
                    continue;
                }

                // Which corner, knee and gain that direction offers is still settled by
                // the single-band objective, the same one the bells pick their Q with.
                // Ranking every corner x knee x gain by a finished fit instead is three
                // orders of magnitude more work — a full greedy pass per candidate,
                // roughly 1300 of them per direction per round, against the two runs
                // here — so the shortlist is one candidate and the shape of the
                // approximation is the same one the greedy pass already makes.
                (PeqBand Band, double Score)? candidate = BestShelf(
                    type,
                    opt,
                    fit,
                    residual,
                    contribution,
                    candidateContribution);
                if (candidate == null)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    shelfResidual[i] = residual[i];
                    shelfEqSum[i] = eqSum[i];
                    if (fit.Valid[i])
                    {
                        shelfResidual[i] -= candidateContribution[i];
                        shelfEqSum[i] += candidateContribution[i];
                    }
                }

                double withShelf = TrialFit(
                    opt,
                    fit,
                    qCandidates,
                    shelfResidual,
                    shelfEqSum,
                    bellSum,
                    remaining - 1);

                // Seeded with the no-shelf score, so this one comparison both ranks the
                // directions and refuses a shelf that does not beat placing none.
                if (withShelf >= chosenFinal)
                {
                    continue;
                }

                found = true;
                chosen = candidate.Value.Band;
                chosenFinal = withShelf;
                Array.Copy(candidateContribution, chosenContribution, n);
            }

            // A round in which no direction finishes ahead of the bells ends the stage:
            // nothing was applied, so the next round would search the very same residual
            // and lose again.
            if (!found)
            {
                return;
            }

            bands.Add(chosen);
            for (int i = 0; i < n; i++)
            {
                if (fit.Valid[i])
                {
                    residual[i] -= chosenContribution[i];
                    eqSum[i] += chosenContribution[i];
                }
            }

            if (chosen.Type == PeqBandType.LowShelf)
            {
                lowPlaced = true;
            }
            else
            {
                highPlaced = true;
            }
        }
    }

    // The best shelf of one direction against the current residual, with its
    // contribution left in winnerContribution; null when the fitting range cannot hold a
    // corner of that direction, when no candidate has a plateau to stand on, or when the
    // caller's QMin excludes every knee a shelf may take.
    private static (PeqBand Band, double Score)? BestShelf(
        PeqBandType type,
        Options opt,
        FitGrid fit,
        double[] residual,
        double[] contribution,
        double[] winnerContribution)
    {
        double[] qCandidates = ShelfCandidateQ.Where(q => q >= opt.QMin).ToArray();
        if (qCandidates.Length == 0)
        {
            return null;
        }

        IReadOnlyList<double>? corners = ShelfCorners(fit, type);
        if (corners == null)
        {
            return null;
        }

        // Gain is searched in the tenth of a dB the strips keep, coarsely first and then
        // refined around the winner: the level is what decides whether a shelf
        // over-corrects and is worth resolving, while its corner and knee are broad
        // choices the ladders above already cover.
        int minTenths = (int)Math.Round(opt.BandGainMinDb * 10);
        int maxTenths = (int)Math.Round(
            (opt.CutsOnlyMode ? Math.Min(0, opt.BandGainMaxDb) : opt.BandGainMaxDb) * 10);
        int deadTenths = (int)Math.Round(ShelfMinGainDb * 10);
        bool searchesBoosts = maxTenths >= deadTenths;

        PeqBand best = default;
        double bestScore = double.MaxValue;
        bool found = false;
        bool bestCutPlateau = false;
        bool bestBoostPlateau = false;
        foreach (double cornerHz in corners)
        {
            double frequencyHz = Math.Round(cornerHz);
            if (frequencyHz < 1)
            {
                continue;
            }

            bool cutPlateau = HasUsablePlateau(fit, type, frequencyHz, boosting: false);
            bool boostPlateau = searchesBoosts &&
                HasUsablePlateau(fit, type, frequencyHz, boosting: true);
            if (!cutPlateau && !boostPlateau)
            {
                continue;
            }

            foreach (double q in qCandidates)
            {
                for (int tenths = minTenths; tenths <= maxTenths; tenths += 10)
                {
                    if (!IsSearchableGain(tenths, deadTenths, cutPlateau, boostPlateau))
                    {
                        continue;
                    }

                    if (ConsiderShelf(
                        new PeqBand(frequencyHz, q, tenths / 10.0, type),
                        opt,
                        fit,
                        residual,
                        contribution,
                        winnerContribution,
                        ref best,
                        ref bestScore,
                        ref found))
                    {
                        bestCutPlateau = cutPlateau;
                        bestBoostPlateau = boostPlateau;
                    }
                }
            }
        }

        if (!found)
        {
            return null;
        }

        // The coarse ladder walks in whole dB, so the true level sits within one step of
        // the winner. Corner and knee are kept: a tenth of a dB does not change which of
        // those was right.
        PeqBand coarse = best;
        int coarseTenths = (int)Math.Round(coarse.GainDb * 10);
        for (int tenths = coarseTenths - 10; tenths <= coarseTenths + 10; tenths++)
        {
            if (tenths < minTenths || tenths > maxTenths ||
                !IsSearchableGain(tenths, deadTenths, bestCutPlateau, bestBoostPlateau))
            {
                continue;
            }

            ConsiderShelf(
                coarse with { GainDb = tenths / 10.0 },
                opt,
                fit,
                residual,
                contribution,
                winnerContribution,
                ref best,
                ref bestScore,
                ref found);
        }

        return (best, bestScore);
    }

    // The corners of one direction worth trying: every one that leaves
    // ShelfSettledMarginOctaves of the fitting range untouched on the shelf's quiet side
    // and ShelfPlateauSpanOctaves of it under the plateau. The two are different numbers
    // sitting on opposite sides for the two directions, so the ladder is built per
    // direction rather than once — a high shelf at 10 kHz is an ordinary car correction,
    // and one symmetric margin wide enough to keep a high shelf off the bottom of the
    // range would have excluded it. Null when the range is too narrow to hold either.
    private static IReadOnlyList<double>? ShelfCorners(FitGrid fit, PeqBandType type)
    {
        bool low = type == PeqBandType.LowShelf;
        double lowestHz = fit.Hz[0] *
            Math.Pow(2, low ? ShelfPlateauSpanOctaves : ShelfSettledMarginOctaves);
        double highestHz = fit.Hz[^1] /
            Math.Pow(2, low ? ShelfSettledMarginOctaves : ShelfPlateauSpanOctaves);
        if (highestHz <= lowestHz || lowestHz < 1)
        {
            return null;
        }

        int count = Math.Clamp(
            (int)Math.Round(Math.Log2(highestHz / lowestHz) * ShelfCornersPerOctave) + 1,
            2,
            64);
        return EqualizationCurve.LogFrequencyGrid(lowestHz, highestHz, count);
    }

    // Scores one shelf candidate and keeps it when it beats the running best, returning
    // whether it did. The skirt guard is deliberately not enforced here — a shelf's
    // plateau is the correction, not spill; see Options.AllowShelves.
    private static bool ConsiderShelf(
        PeqBand band,
        Options opt,
        FitGrid fit,
        double[] residual,
        double[] contribution,
        double[] winnerContribution,
        ref PeqBand best,
        ref double bestScore,
        ref bool found)
    {
        double score = ScoreBand(
            band, opt, fit, residual, spillBase: null, contribution, out _);
        if (found && score >= bestScore)
        {
            return false;
        }

        found = true;
        best = band;
        bestScore = score;
        Array.Copy(contribution, winnerContribution, contribution.Length);
        return true;
    }

    // A gain the shelf search may try at this corner: clear of the dead zone that is not
    // worth a slot, and on a side the plateau justifies — a cut needs measured data
    // under it, a boost needs data the reliability mask cleared.
    private static bool IsSearchableGain(
        int tenths,
        int deadTenths,
        bool cutPlateau,
        bool boostPlateau) =>
        Math.Abs(tenths) >= deadTenths && (tenths < 0 ? cutPlateau : boostPlateau);

    // Whether the span beyond a shelf's knee, on the shelf's own side, carries enough
    // data to decide a correction of that whole end of the range: measured points for a
    // cut, boost-allowed points for a boost (see Options.AllowShelves). Two points is
    // the floor whatever the grid size — one point is a bin, not a plateau.
    private static bool HasUsablePlateau(
        FitGrid fit,
        PeqBandType type,
        double cornerHz,
        bool boosting)
    {
        bool low = type == PeqBandType.LowShelf;
        int total = 0;
        int usable = 0;
        for (int i = 0; i < fit.Hz.Count; i++)
        {
            double octaves = Math.Log2(fit.Hz[i] / cornerHz);
            bool inPlateau = low
                ? octaves <= -ShelfPlateauMarginOctaves
                : octaves >= ShelfPlateauMarginOctaves;
            if (!inPlateau)
            {
                continue;
            }

            total++;
            if (boosting ? fit.BoostAllowed[i] : fit.Valid[i])
            {
                usable++;
            }
        }

        return total >= 2 && usable >= total * ShelfPlateauUsableFraction;
    }

    // Marks the contiguous run of masked-off boost-wanting points (residual > 0 AND
    // not boost-allowed) around center as blocked, so a forbidden region is skipped
    // without touching the adjacent cut valleys. It stops at the first boost-allowed
    // point on each side: a wide correctable dip with a narrow null at its floor keeps
    // its reliable shoulders available for correction — only the forbidden core is
    // dropped. Always blocks at least center, guaranteeing the loop makes progress.
    private static void BlockForbiddenBoostRun(
        bool[] blocked,
        double[] residual,
        bool[] boostAllowed,
        bool[] valid,
        int center)
    {
        blocked[center] = true;
        for (int i = center - 1;
            i >= 0 && valid[i] && residual[i] > 0 && !boostAllowed[i];
            i--)
        {
            blocked[i] = true;
        }

        for (int i = center + 1;
            i < residual.Length && valid[i] && residual[i] > 0 && !boostAllowed[i];
            i++)
        {
            blocked[i] = true;
        }
    }

    // Resamples an optional (Hz, γ²) coherence curve onto the fitting grid. A missing
    // curve yields null (the mask then treats every point as reliable); a frequency
    // outside the curve holds the nearest coherence value.
    private static double[]? ResampleCoherence(
        IReadOnlyList<SignalPoint>? coherence,
        IReadOnlyList<double> grid)
    {
        if (coherence == null || coherence.Count == 0)
        {
            return null;
        }

        var result = new double[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            result[i] = CurveSampling.InterpolateDbLog(coherence, grid[i], clampEnds: true);
        }

        return result;
    }

    private static void BlockAround(
        bool[] blocked,
        IReadOnlyList<double> grid,
        int center,
        double octaves)
    {
        double centerHz = grid[center];
        for (int i = 0; i < grid.Count; i++)
        {
            if (Math.Abs(Math.Log2(grid[i] / centerHz)) <= octaves)
            {
                blocked[i] = true;
            }
        }
    }

    private static int IndexOfLargestResidual(
        double[] residual,
        bool[] valid,
        bool[] blocked)
    {
        int index = -1;
        double largest = 0;
        for (int i = 0; i < residual.Length; i++)
        {
            if (valid[i] && !blocked[i] && Math.Abs(residual[i]) > largest)
            {
                largest = Math.Abs(residual[i]);
                index = i;
            }
        }

        return index;
    }

    private static double[] Resample(
        IReadOnlyList<SignalPoint> points,
        IReadOnlyList<double> grid)
    {
        var result = new double[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            // No end clamp: points outside the measured range read NaN and are
            // excluded from the fit.
            result[i] = CurveSampling.InterpolateDbLog(points, grid[i], clampEnds: false);
        }

        return result;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);
}
