namespace Resonalyze.Dsp;

/// <summary>
/// Decides, per frequency-grid point, whether an automatic equalizer may place a
/// <b>boost</b> band there. Boosting the wrong place is the dangerous EQ move in a
/// reflective car cabin: a deep, narrow interference null cannot be filled by EQ
/// (the boost just burns amplifier headroom and wastes a band on a dip that returns
/// the moment the mic moves), and a low-coherence bin is not a repeatable acoustic
/// feature worth boosting at all. Cuts are always safe — they only remove energy —
/// so they are never gated here; this mask exists purely to keep boosts honest.
/// </summary>
public static class EqBoostabilityMask
{
    public sealed record Options
    {
        /// <summary>
        /// A boost is disallowed where the measured coherence γ² is below this. Only
        /// applied when the caller supplies coherence; a source without it is gated by
        /// the null detector and the fitting band alone.
        /// </summary>
        public double CoherenceFloor { get; init; } = 0.5;

        /// <summary>
        /// A dip counts as a null when the magnitude recovers by at least this many dB
        /// on BOTH sides within <see cref="NullHalfWidthOctaves"/>.
        /// </summary>
        public double NullDepthDb { get; init; } = 6.0;

        /// <summary>
        /// How far to each side (in octaves) the recovery must happen for a dip to be
        /// "narrow". A monotonic roll-off recovers on only one side within this window,
        /// so it is deliberately NOT treated as a null (it is the fitting band's and the
        /// boost-headroom cap's job, not the mask's).
        /// </summary>
        public double NullHalfWidthOctaves { get; init; } = 0.25;
    }

    /// <summary>
    /// Returns a per-point flag: may a boost band be centred at this grid point?
    /// <paramref name="coherence"/> is optional (γ² aligned to <paramref name="gridHz"/>);
    /// a null argument, or a non-finite entry, is treated as reliable so a source
    /// carrying no coherence degrades to null-detection-only masking.
    /// </summary>
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

    // A point sits in a narrow deep null when, scanning outward within the half-width
    // window, the magnitude climbs at least NullDepthDb above it on BOTH sides. A
    // monotonic roll-off climbs on only one side and is therefore spared.
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

    // The greatest amount (dB) the magnitude rises above the reference while scanning
    // from index in one direction, stopping once the octave window is left or the grid
    // ends. Invalid/non-finite neighbours are skipped, not treated as a barrier.
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
