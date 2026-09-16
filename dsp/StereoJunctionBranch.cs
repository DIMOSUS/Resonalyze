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
        double refineStepMs = 0.1)
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
        // never exceeds an eighth of the half period, so the partner itself is always probed, whatever the junction.
        double stepMs = Math.Min(refineStepMs, halfPeriodMs / 8);
        foreach (double center in new[] { -halfPeriodMs, halfPeriodMs })
        {
            for (double delta = center - halfPeriodMs / 4;
                delta <= center + halfPeriodMs / 4 + 1e-9;
                delta += stepMs)
            {
                double reference = score(false, delta, true);
                double far = score(true, delta, true);
                if (!double.IsFinite(reference) || !double.IsFinite(far))
                {
                    continue;
                }

                var reading = new StereoBranchReading(
                    delta, true, reference - referenceBase, far - farBase);
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

    /// <summary>The far side gains plainly and the reference side is not made to pay for it.</summary>
    public static bool Adopt(StereoBranchReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading.FarGainDb > FarGainDb &&
            reading.ReferenceGainDb > -ReferenceLossDb;
    }
}
