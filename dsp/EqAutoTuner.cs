using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// Fits PEQ bands plus a preamp so that source + curve approximates a target: greedy bells where the
/// residual is worst, optionally preceded by a shelf stage. See docs/tech/eq-auto-tuner.md#greedy-fit.
/// </summary>
/// <remarks>All-pass bands are never fitted (they are flat). Callers replace the whole bank with the result.</remarks>
public static class EqAutoTuner
{
    public sealed record Options
    {
        public int MaxBands { get; init; } = EqualizationCurve.MaxBandCount;

        public double MinFrequencyHz { get; init; } = 20;
        public double MaxFrequencyHz { get; init; } = 20_000;

        public double BandGainMinDb { get; init; } = -15;
        public double BandGainMaxDb { get; init; } = 6;

        public double QMin { get; init; } = 0.5;
        public double QMax { get; init; } = 10;

        public double PreampMinDb { get; init; } = -30;
        public double PreampMaxDb { get; init; } = 30;

        /// <summary>
        /// Ceiling on total EQ gain (preamp + bands) at any frequency; applied to the preamp AFTER bands are placed,
        /// so with boosts allowed prefer pinning the preamp. Unbounded by default; a clip-safe cuts-only caller passes 0.
        /// </summary>
        public double TotalGainMaxDb { get; init; } = double.PositiveInfinity;

        public double StopResidualDb { get; init; } = 0.5;

        /// <summary>Octaves sterilised around each placed band so it is not re-nibbled; small so clustered peaks each get a band.</summary>
        public double MinBandSpacingOctaves { get; init; } = 0.1;

        /// <summary>Span skipped around a boost capped by headroom, so the budget is not wasted on an unrecoverable deficit.</summary>
        public double SaturatedBlockOctaves { get; init; } = 1.0;

        public int GridSize { get; init; } = 256;

        /// <summary>Sample rate of the DSP that realises the fitted RBJ biquads.</summary>
        public double SampleRateHz { get; init; } = 48_000;

        /// <summary>Places only cuts. Off here; the EQ Wizard defaults it on (boosting a cabin's nulls does harm).</summary>
        public bool CutsOnlyMode { get; init; }

        /// <summary>Per-frequency boost reliability policy; consulted only when <see cref="CutsOnlyMode"/> is off.</summary>
        public EqBoostabilityMask.Options BoostMask { get; init; } = new();

        /// <summary>
        /// Max cumulative boost a boost band's skirt may pour into a masked-off bin (the mask only clears the centre).
        /// +infinity disables the guard.
        /// </summary>
        public double ForbiddenRegionMaxBoostDb { get; init; } = 0.5;

        /// <summary>Lets the fit place a low and a high shelf before the bell pass. See docs/tech/eq-auto-tuner.md#shelf-stage.</summary>
        public bool AllowShelves { get; init; }
    }

    // Narrow peaks get narrow bands, broad trends wide ones; filtered to [QMin, QMax] at run time.
    private static readonly double[] CandidateQ =
        { 0.5, 0.7, 1.0, 1.4, 2.0, 2.8, 4.0, 5.6, 8.0, 10.0 };

    // Cuts-only charge for the over-cut this band ADDS below target (past a free zone), never the pre-existing depth.
    // See docs/tech/eq-auto-tuner.md#cuts-only-over-correction-penalty.
    private const double CutsOnlyOverCorrectionWeight = 25.0;
    private const double CutsOnlyOverCutFreeDb = 1.0;

    // Shelf policy; see docs/tech/eq-auto-tuner.md#shelf-stage for every number below.

    private static readonly PeqBandType[] ShelfDirections =
        { PeqBandType.LowShelf, PeqBandType.HighShelf };

    // Capped at 0.7: above 1/sqrt(2) an RBJ shelf overshoots, and a cutting shelf would boost. QMax does not apply to knees.
    private static readonly double[] ShelfCandidateQ = { 0.3, 0.4, 0.5, 0.7 };

    // Untouched range required on the quiet side, so a shelf is not a preamp wearing a filter slot.
    private const double ShelfSettledMarginOctaves = 2.0;

    private const double ShelfPlateauSpanOctaves = 1.0;

    private const double ShelfPlateauMarginOctaves = 0.5;

    private const double ShelfPlateauUsableFraction = 0.75;

    private const int ShelfCornersPerOctave = 3;

    private const double ShelfMinGainDb = 0.5;

    private const double ShelfSlotWorthDb = 0.01;

    /// <summary>
    /// Fits a curve so that source + curve best matches target; curves need not share frequency points.
    /// <paramref name="coherence"/> (Hz, γ²) is optional and only gates boosts.
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

        // Preamp alignment: mean error with boosts; max error in cuts-only so no point starts below target.
        // The ceiling is pre-applied so bands fit at the realised level. See docs/tech/eq-auto-tuner.md#preamp-alignment.
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

        bool[] boostAllowed;
        if (opt.CutsOnlyMode)
        {
            boostAllowed = new bool[n];
        }
        else
        {
            double[]? coherenceGrid = ResampleCoherence(coherence, grid);
            boostAllowed = EqBoostabilityMask.ComputeBoostAllowed(
                grid, sourceDb, valid, coherenceGrid, opt.BoostMask);
        }

        // Pre-computed so a candidate costs one biquad build; same arithmetic as DigitalEqualizationResponse.MagnitudeDbAt.
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
        var eqSum = new double[n];
        // Running total of BELLS only: both boost guards read it, so a shelf's plateau does not lock bells out. See docs/tech/eq-auto-tuner.md#shelf-stage.
        var bellSum = new double[n];
        var blocked = new bool[n];

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
    /// The next bell the greedy pass would place, without applying it (the shelf stage calls it on scratch state).
    /// </summary>
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

            // Skip the forbidden deficit but stop at the first boost-allowed point, so a null's reliable shoulders still get bands.
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
                // Bells' own summed boost stays under the ceiling; a shelf under it is not counted (TotalGainMaxDb bounds the total).
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
                BlockAround(blocked, grid, peakIndex, opt.SaturatedBlockOctaves);
                continue;
            }

            double frequencyHz = Math.Round(grid[peakIndex]);

            // A boost's skirt may not push cumulative boost past ForbiddenRegionMaxBoostDb at any masked-off bin.
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
                BlockAround(blocked, grid, peakIndex, opt.MinBandSpacingOctaves);
                continue;
            }

            BlockAround(
                blocked,
                grid,
                peakIndex,
                boostHeadroomLimited ? opt.SaturatedBlockOctaves : opt.MinBandSpacingOctaves);
            return (new PeqBand(frequencyHz, bestQ, gainDb), bestScore);
        }
    }

    // Score of a finished fit. Cuts-only charge is on what the BANDS pulled below target, not the depth itself.
    // See docs/tech/eq-auto-tuner.md#ranking-finished-fits.
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

    // Runs the bell pass to exhaustion on scratch copies; the band count shows whether the budget constrained it.
    private static (double Score, int Bands) TrialFit(
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
        int bands = 0;
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

            bands++;
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

        return (FinalScore(opt, fit, trialResidual, trialEqSum), bands);
    }

    private sealed record FitGrid(
        IReadOnlyList<double> Hz,
        Complex[] Z1,
        Complex[] Z2,
        bool[] Valid,
        bool[] BoostAllowed,
        int ValidCount);

    // The fit's objective for bells and shelves alike. spillBase is null when the skirt guard does not apply (cuts, shelves).
    private static double ScoreBand(
        PeqBand band,
        Options opt,
        FitGrid fit,
        double[] residual,
        double[]? spillBase,
        double[] contribution,
        out bool spillsIntoForbidden)
    {
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
    /// Places at most one low and one high shelf before the bell pass. See docs/tech/eq-auto-tuner.md#shelf-stage.
    /// </summary>
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
            // Every viable candidate is judged on a finished fit against no shelf; cheaper rules failed. See docs/tech/eq-auto-tuner.md#shelf-stage.
            int remaining = opt.MaxBands - bands.Count;
            (double Score, int Bands) withoutShelf = TrialFit(
                opt, fit, qCandidates, residual, eqSum, bellSum, remaining);

            PeqBand chosen = default;
            double chosenFinal = withoutShelf.Score;
            bool found = false;
            foreach (PeqBandType type in ShelfDirections)
            {
                if (type == PeqBandType.LowShelf ? lowPlaced : highPlaced)
                {
                    continue;
                }

                foreach (PeqBand candidate in
                    ShelfCandidates(type, opt, fit, residual, contribution))
                {
                    ScoreBand(
                        candidate,
                        opt,
                        fit,
                        residual,
                        spillBase: null,
                        candidateContribution,
                        out _);
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

                    (double Score, int Bands) withShelf = TrialFit(
                        opt,
                        fit,
                        qCandidates,
                        shelfResidual,
                        shelfEqSum,
                        bellSum,
                        remaining - 1);

                    // Seeded with the no-shelf score, so this also refuses every candidate that does not beat placing none.
                    if (withShelf.Score >= chosenFinal)
                    {
                        continue;
                    }

                    // A shelf that does not shorten the fit must improve it visibly (ShelfSlotWorthDb).
                    if (withShelf.Bands + 1 >= withoutShelf.Bands &&
                        Math.Sqrt(withoutShelf.Score) - Math.Sqrt(withShelf.Score) <
                            ShelfSlotWorthDb)
                    {
                        continue;
                    }

                    found = true;
                    chosen = candidate;
                    chosenFinal = withShelf.Score;
                    Array.Copy(candidateContribution, chosenContribution, n);
                }
            }

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

    // Best gain at each corner and knee; corner/knee are judged on a finished fit, gain on the single-band objective.
    private static List<PeqBand> ShelfCandidates(
        PeqBandType type,
        Options opt,
        FitGrid fit,
        double[] residual,
        double[] contribution)
    {
        var candidates = new List<PeqBand>();
        double[] qCandidates = ShelfCandidateQ.Where(q => q >= opt.QMin).ToArray();
        if (qCandidates.Length == 0)
        {
            return candidates;
        }

        IReadOnlyList<double>? corners = ShelfCorners(fit, type);
        if (corners == null)
        {
            return candidates;
        }

        int minTenths = (int)Math.Round(opt.BandGainMinDb * 10);
        int maxTenths = (int)Math.Round(
            (opt.CutsOnlyMode ? Math.Min(0, opt.BandGainMaxDb) : opt.BandGainMaxDb) * 10);
        int deadTenths = (int)Math.Round(ShelfMinGainDb * 10);
        bool searchesBoosts = maxTenths >= deadTenths;

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
                PeqBand best = default;
                double bestScore = double.MaxValue;
                bool found = false;
                for (int tenths = minTenths; tenths <= maxTenths; tenths += 10)
                {
                    if (!IsSearchableGain(tenths, deadTenths, cutPlateau, boostPlateau))
                    {
                        continue;
                    }

                    ConsiderShelf(
                        new PeqBand(frequencyHz, q, tenths / 10.0, type),
                        opt,
                        fit,
                        residual,
                        contribution,
                        ref best,
                        ref bestScore,
                        ref found);
                }

                if (!found)
                {
                    continue;
                }

                PeqBand coarse = best;
                int coarseTenths = (int)Math.Round(coarse.GainDb * 10);
                for (int tenths = coarseTenths - 10; tenths <= coarseTenths + 10; tenths++)
                {
                    if (tenths < minTenths || tenths > maxTenths ||
                        !IsSearchableGain(tenths, deadTenths, cutPlateau, boostPlateau))
                    {
                        continue;
                    }

                    ConsiderShelf(
                        coarse with { GainDb = tenths / 10.0 },
                        opt,
                        fit,
                        residual,
                        contribution,
                        ref best,
                        ref bestScore,
                        ref found);
                }

                candidates.Add(best);
            }
        }

        return candidates;
    }

    private static void ConsiderShelf(
        PeqBand band,
        Options opt,
        FitGrid fit,
        double[] residual,
        double[] contribution,
        ref PeqBand best,
        ref double bestScore,
        ref bool found)
    {
        double score = ScoreBand(
            band, opt, fit, residual, spillBase: null, contribution, out _);
        if (found && score >= bestScore)
        {
            return;
        }

        found = true;
        best = band;
        bestScore = score;
    }

    // Built per direction: the quiet-side and plateau margins sit on opposite sides for low and high shelves.
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

    private static bool IsSearchableGain(
        int tenths,
        int deadTenths,
        bool cutPlateau,
        bool boostPlateau) =>
        Math.Abs(tenths) >= deadTenths && (tenths < 0 ? cutPlateau : boostPlateau);

    // Two points minimum whatever the grid size: one point is a bin, not a plateau.
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

    // Blocks the contiguous forbidden boost run around center, stopping at boost-allowed points; always blocks center.
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

    // Null curve means every point reliable; out-of-range frequencies hold the nearest value.
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
            // No end clamp: points outside the measured range read NaN and are excluded.
            result[i] = CurveSampling.InterpolateDbLog(points, grid[i], clampEnds: false);
        }

        return result;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);
}
