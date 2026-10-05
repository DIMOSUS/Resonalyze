namespace Resonalyze.Dsp;

/// <param name="ReferenceGainDb">What the branch move does to the reference side's junction; negative is a loss.</param>
internal sealed record StereoBranchReading(
    double DeltaMs,
    bool Flip,
    double ReferenceGainDb,
    double FarGainDb)
{
    public double MeanGainDb => 0.5 * (ReferenceGainDb + FarGainDb);
}

/// <summary>Whether a junction's two sides agree about which lobe it sits on. A reference side that settles a near-tie
/// commits the far side too, which pays for it. See docs/tech/auto-alignment.md#stereo-branch-check.</summary>
internal static class StereoJunctionBranch
{
    /// <summary>The far side must gain at least this for the move to be worth disturbing a settled junction.</summary>
    public const double FarGainDb = 0.30;

    /// <summary>What the reference side may lose. Near zero on purpose: the near-listener junction is not for sale,
    /// and this pass exists for branches the reference could not tell apart, not for trades.</summary>
    public const double ReferenceLossDb = 0.10;

    /// <summary>Worth reporting even when declined: a far side that wanted the other branch is a tuning hint.</summary>
    public const double NoteworthyFarGainDb = 0.15;

    /// <summary>Best branch move of those probed, or null where none helps the far side at all.</summary>
    /// <param name="score">Side, delta and flip to the junction's dip-penalized score.</param>
    public static StereoBranchReading? Read(
        Func<bool, double, bool, double> score,
        double halfPeriodMs,
        double refineStepMs = 0.1,
        bool wholePeriod = false)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfPeriodMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refineStepMs);

        double referenceBase = score(false, 0, false);
        double farBase = score(true, 0, false);
        if (!double.IsFinite(referenceBase) || !double.IsFinite(farBase))
        {
            return null;
        }

        // The branch is one lobe wide, so the delta that serves both sides is not always the one that serves the far
        // side most: the feasible set is searched first, and only an empty one falls back to the far side's own best.
        StereoBranchReading? feasible = null;
        StereoBranchReading? best = null;
        // The flip partner sits half a period either way; the refinement covers a coarse arrival's slack. The step
        // never exceeds an eighth of the half period: where the eighth is the step the partner itself is probed,
        // and where the 0.1 ms cap is, the grid is already far finer than the lobe.
        double stepMs = Math.Min(refineStepMs, halfPeriodMs / 8);
        // A whole period keeps the relation, so it is probed unflipped.
        bool flip = !wholePeriod;
        double moveMs = wholePeriod ? 2 * halfPeriodMs : halfPeriodMs;
        foreach (double center in new[] { -moveMs, moveMs })
        {
            for (double delta = center - halfPeriodMs / 4;
                delta <= center + halfPeriodMs / 4 + 1e-9;
                delta += stepMs)
            {
                double reference = score(false, delta, flip);
                double far = score(true, delta, flip);
                if (!double.IsFinite(reference) || !double.IsFinite(far))
                {
                    continue;
                }

                var reading = new StereoBranchReading(
                    delta, flip, reference - referenceBase, far - farBase);
                if (best == null || reading.FarGainDb > best.FarGainDb)
                {
                    best = reading;
                }

                if (reading.ReferenceGainDb > -ReferenceLossDb &&
                    (feasible == null || reading.FarGainDb > feasible.FarGainDb))
                {
                    feasible = reading;
                }
            }
        }

        return feasible ?? best;
    }

    /// <summary>The reading re-read at the better of the two DSP ticks around its delta: the processor plays the
    /// grid, so everything downstream — the re-render, the log, the alignment — must judge a delay it can play.
    /// The better tick is the one the scan itself would prefer: feasible for the reference side first, then the
    /// larger far gain.</summary>
    public static StereoBranchReading Quantize(
        StereoBranchReading reading,
        Func<bool, double, bool, double> score,
        double gridMs = 0.01)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridMs);

        double referenceBase = score(false, 0, false);
        double farBase = score(true, 0, false);
        double lowerMs = Math.Round(Math.Floor(reading.DeltaMs / gridMs) * gridMs, 2);
        double upperMs = Math.Round(Math.Ceiling(reading.DeltaMs / gridMs) * gridMs, 2);
        StereoBranchReading? best = null;
        foreach (double delta in new[] { lowerMs, upperMs }.Distinct())
        {
            double reference = score(false, delta, reading.Flip);
            double far = score(true, delta, reading.Flip);
            if (!double.IsFinite(reference) || !double.IsFinite(far))
            {
                continue;
            }

            var candidate = new StereoBranchReading(
                delta, reading.Flip, reference - referenceBase, far - farBase);
            bool candidateFeasible = candidate.ReferenceGainDb > -ReferenceLossDb;
            bool bestFeasible = best != null && best.ReferenceGainDb > -ReferenceLossDb;
            if (best == null ||
                (candidateFeasible && !bestFeasible) ||
                (candidateFeasible == bestFeasible && candidate.FarGainDb > best.FarGainDb))
            {
                best = candidate;
            }
        }

        return best ?? reading with { DeltaMs = Math.Round(reading.DeltaMs, 2) };
    }

    /// <summary>What a whole-period move must show in the direct sound's coherence at the junction: the far side
    /// comes into step, by the lobe check's own floor and gulf, and the reference side does not fall out of it.</summary>
    public static bool WavefrontsBack(
        double farBefore, double farAfter, double referenceBefore, double referenceAfter) =>
        farAfter >= DirectLobeWitness.MinimumR &&
        farAfter - farBefore > DirectLobeWitness.LobeAdvantage &&
        referenceBefore - referenceAfter <= DirectLobeWitness.LobeAdvantage;

    /// <summary>The far side gains plainly and the reference side is not made to pay for it.</summary>
    public static bool Adopt(StereoBranchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading.FarGainDb > FarGainDb &&
            reading.ReferenceGainDb > -ReferenceLossDb;
    }
}
