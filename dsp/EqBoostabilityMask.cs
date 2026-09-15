namespace Resonalyze.Dsp;

/// <summary>Per grid point, may a BOOST be centred here? Refuses narrow deep nulls and low-coherence bins; cuts are never gated.</summary>
public static class EqBoostabilityMask
{
    public sealed record Options
    {
        /// <summary>Boost refused below this γ²; applied only when coherence is supplied.</summary>
        public double CoherenceFloor { get; init; } = 0.5;

        public double NullDepthDb { get; init; } = 6.0;

        /// <summary>Recovery window per side; a monotonic roll-off recovers on one side only and is NOT a null.</summary>
        public double NullHalfWidthOctaves { get; init; } = 0.25;
    }

    /// <summary>Null or non-finite coherence counts as reliable (null detection only).</summary>
    public static bool[] ComputeBoostAllowed(
        IReadOnlyList<double> gridHz,
        IReadOnlyList<double> magnitudeDb,
        IReadOnlyList<bool> valid,
        IReadOnlyList<double>? coherence,
        Options options)
    {
        ArgumentNullException.ThrowIfNull(gridHz);
        ArgumentNullException.ThrowIfNull(magnitudeDb);
        ArgumentNullException.ThrowIfNull(valid);
        ArgumentNullException.ThrowIfNull(options);

        int n = gridHz.Count;
        var allowed = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (!valid[i] || !double.IsFinite(magnitudeDb[i]))
            {
                allowed[i] = false;
                continue;
            }

            bool coherentEnough = coherence == null ||
                !double.IsFinite(coherence[i]) ||
                coherence[i] >= options.CoherenceFloor;
            allowed[i] = coherentEnough &&
                !IsInNarrowDeepNull(gridHz, magnitudeDb, valid, i, options);
        }

        return allowed;
    }

    private static bool IsInNarrowDeepNull(
        IReadOnlyList<double> gridHz,
        IReadOnlyList<double> magnitudeDb,
        IReadOnlyList<bool> valid,
        int index,
        Options options)
    {
        double here = magnitudeDb[index];
        double leftRise = MaxRiseWithin(
            gridHz, magnitudeDb, valid, index, here, step: -1, options.NullHalfWidthOctaves);
        double rightRise = MaxRiseWithin(
            gridHz, magnitudeDb, valid, index, here, step: +1, options.NullHalfWidthOctaves);
        return leftRise >= options.NullDepthDb && rightRise >= options.NullDepthDb;
    }

    // Non-finite neighbours are skipped, not treated as a barrier.
    private static double MaxRiseWithin(
        IReadOnlyList<double> gridHz,
        IReadOnlyList<double> magnitudeDb,
        IReadOnlyList<bool> valid,
        int index,
        double reference,
        int step,
        double halfWidthOctaves)
    {
        double centerHz = gridHz[index];
        double maxRise = 0;
        for (int i = index + step; i >= 0 && i < gridHz.Count; i += step)
        {
            if (Math.Abs(Math.Log2(gridHz[i] / centerHz)) > halfWidthOctaves)
            {
                break;
            }

            if (!valid[i] || !double.IsFinite(magnitudeDb[i]))
            {
                continue;
            }

            maxRise = Math.Max(maxRise, magnitudeDb[i] - reference);
        }

        return maxRise;
    }
}
