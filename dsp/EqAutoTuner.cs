namespace Resonalyze.Dsp;

/// <summary>
/// Automatically fits a set of peaking PEQ bands (plus a preamp) so that adding the
/// resulting <see cref="EqualizationCurve"/> to a measured source curve approximates
/// a target curve. The number of bands is chosen automatically: the fit adds bands
/// greedily where the residual error is largest and stops once the remaining error
/// is negligible or the band budget is exhausted.
/// </summary>
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
        /// clipping DSP profile — the fit used to hand one out and let the UI
        /// report the damage as a negative headroom afterwards. The preamp is
        /// capped after the bands are placed, so the fitted shape stays and
        /// the curve honestly sits below an unreachable target instead.
        /// Unbounded by default: as a pure curve fit the preamp legitimately
        /// carries the level difference between arbitrarily referenced source
        /// and target; a caller producing a profile for a real DSP passes 0.
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
    }

    // Quality factors tried for each band; the one that lowers the residual error
    // the most is kept, so narrow peaks get narrow bands and broad trends get wide
    // bands. Filtered to the configured [QMin, QMax] range at run time.
    private static readonly double[] CandidateQ =
        { 0.5, 0.7, 1.0, 1.4, 2.0, 2.8, 4.0, 5.6, 8.0, 10.0 };

    // How much more an over-correction counts than an equal under-correction when
    // choosing a band's Q in cuts-only mode. Over-cutting pushes a point BELOW the target,
    // where no later cut can lift it back (only a forbidden boost could), so a wide band
    // that shaves a broad peak by gouging a broad adjacent shoulder well below the target
    // (a visible hole in the response) loses to a tighter band that leaves the far side of
    // the peak for another band. It is a soft penalty, not a veto: cutting a narrow peak
    // is still worth the shallow dip its skirt leaves in an adjacent valley, because the
    // heavily weighted term only dominates once a WIDE skirt drops a broad span far below
    // the target. Under-correction (still above target) stays freely fixable and keeps
    // unit weight.
    private const double CutsOnlyOverCorrectionWeight = 25.0;

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

        var bands = new List<PeqBand>();
        var contribution = new double[n];
        var bestContribution = new double[n];
        // Running EQ magnitude of the placed bands, used to cap the cumulative boost.
        var eqSum = new double[n];
        // Frequencies excluded from further bands (already corrected as far as they
        // can be, or an unrecoverable boost deficit).
        var blocked = new bool[n];
        while (bands.Count < opt.MaxBands)
        {
            int peakIndex = IndexOfLargestResidual(residual, valid, blocked);
            if (peakIndex < 0 || Math.Abs(residual[peakIndex]) < opt.StopResidualDb)
            {
                break;
            }

            double desired = residual[peakIndex];

            // A boost the mode or the reliability mask forbids here is not fitted at
            // all: skip the contiguous FORBIDDEN deficit around it, but stop at the
            // first boost-allowed point so the reliable shoulders of a wide dip whose
            // core is a null still get their own bands.
            if (desired > 0 && !boostAllowed[peakIndex])
            {
                BlockForbiddenBoostRun(blocked, residual, boostAllowed, valid, peakIndex);
                continue;
            }

            double gainDb;
            bool boostHeadroomLimited = false;
            if (desired > 0)
            {
                // Limit the boost so the total EQ gain here never exceeds the boost
                // ceiling; a roll-off that needs +30 dB gets one capped band, not a
                // stack of them.
                double headroom = opt.BandGainMaxDb - eqSum[peakIndex];
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
            double bestRms = double.MaxValue;
            bool anyCandidateFits = false;
            foreach (double candidate in qCandidates)
            {
                double q = Math.Round(candidate, 1);
                var band = new PeqBand(frequencyHz, q, gainDb);
                double sumSquares = 0;
                bool spillsIntoForbidden = false;
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i])
                    {
                        continue;
                    }

                    double c = DigitalEqualizationResponse.MagnitudeDbAt(
                        band, grid[i], opt.SampleRateHz);
                    contribution[i] = c;
                    if (isBoost && !boostAllowed[i] &&
                        eqSum[i] + c > opt.ForbiddenRegionMaxBoostDb)
                    {
                        spillsIntoForbidden = true;
                    }

                    // In cuts-only, a point this band cuts (c < 0) past the target
                    // (r > 0: corrected below target) is weighted up, so a wide skirt that
                    // gouges a broad shoulder below the target loses to a tighter band.
                    double r = residual[i] - c;
                    sumSquares += opt.CutsOnlyMode && c < 0 && r > 0
                        ? CutsOnlyOverCorrectionWeight * r * r
                        : r * r;
                }

                if (spillsIntoForbidden)
                {
                    continue;
                }

                anyCandidateFits = true;
                double rms = sumSquares / validCount;
                if (rms < bestRms)
                {
                    bestRms = rms;
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

            bands.Add(new PeqBand(frequencyHz, bestQ, gainDb));
            for (int i = 0; i < n; i++)
            {
                if (valid[i])
                {
                    residual[i] -= bestContribution[i];
                    eqSum[i] += bestContribution[i];
                }
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
