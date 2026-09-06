using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The <b>Lock</b> beside the side radios of the Virtual DSP: while it is on, a
/// crossover or polarity edit made on the side the controls show is written onto the
/// other side of the same pair, so a symmetric tune stays symmetric without an L→R
/// after every corner moved.
/// </summary>
/// <remarks>
/// <para>
/// It works by difference rather than by hooking the editors. The panel funnels every
/// change through its autosave, and the lock keeps a snapshot of the locked group of
/// BOTH sides of every pair as they stood at the previous <see cref="Follow"/> (or
/// when the pair first came under the lock). A unit that differs from its snapshot on
/// the shown side has moved since then and is copied across. The lock is for the
/// hand on the knob, which only ever reaches the shown side; a run that writes both
/// sides itself hands its result to <see cref="Remember"/> before the save, and
/// nothing of it is carried. That is a rule, not a heuristic, because a difference
/// cannot tell such a run from a hand edit: the auto-delay may flip the shown side's
/// polarity and KEEP the hidden one; a junction tune or the crossover wizard writes
/// one edge onto both sides, and a hidden side that already held it looks untouched
/// while the shown side's whole crossover — its other edge included — would be
/// carried over the hidden side's own; an AI import's rows name their sides and the
/// dialog showed exactly those; and an undo read as a difference could carry a
/// restored value onto a side the import never touched. (A guard that only carried
/// when the hidden side had NOT moved in the same step was tried and failed exactly
/// on the "already held it" case, so the panel names every such run instead.)
/// </para>
/// <para>
/// Whatever differed between the sides when the lock was engaged keeps differing until
/// that unit is next touched: engaging copies nothing, which is the "from the moment it
/// is ticked" the tooltip promises, and a user whose sides disagree can still see both
/// disagreeing before deciding which one to type over.
/// </para>
/// <para>
/// Two units, deliberately. The crossover — its kind and both edges — moves as ONE: a
/// lock that carried only the corner the user turned would leave the hidden side's
/// other corner, or its kind, where it was, and the two crossovers unequal after an
/// edit meant to equalize them. Polarity moves on its own, so moving a corner does not
/// flip the other side. Gain, delay, the phase angle and the PEQ are not locked: each
/// aligns a driver against its own side's level and geometry, the same reason the L→R
/// dialog leaves them unticked by default.
/// </para>
/// </remarks>
internal sealed class VirtualCrossoverSideLock
{
    // The crossover as one value, so "did it move" and "write it across" read all
    // three fields together; the record equality is what makes a moved corner show.
    private readonly record struct Crossover(
        CrossoverKind Kind,
        CrossoverEdge HighPass,
        CrossoverEdge LowPass)
    {
        public static Crossover Of(VirtualCrossoverChannelSettings settings) =>
            new(settings.CrossoverKind, settings.HighPassEdge, settings.LowPassEdge);

        public void WriteTo(VirtualCrossoverChannelSettings settings)
        {
            settings.CrossoverKind = Kind;
            settings.HighPassEdge = HighPass;
            settings.LowPassEdge = LowPass;
        }
    }

    private readonly record struct Snapshot(Crossover Crossover, bool Inverted)
    {
        public static Snapshot Of(VirtualCrossoverChannelSettings settings) =>
            new(Crossover.Of(settings), settings.InvertPolarity);
    }

    // Keyed by the pair OBJECT: a loaded session binds new pair objects, which is
    // exactly when the old snapshots stop meaning anything. The pair class has no
    // value equality, so the default comparer is reference identity already.
    private readonly Dictionary<
        VirtualCrossoverChannelPairSettings, (Snapshot Left, Snapshot Right)> snapshots = new();

    public bool Engaged { get; private set; }

    /// <summary>
    /// Turns the lock on over these pairs, copying nothing: the sides are remembered
    /// as they stand, and only what moves from here on is carried across.
    /// </summary>
    public void Engage(IEnumerable<VirtualCrossoverChannelPairSettings> pairs)
    {
        Engaged = true;
        Remember(pairs);
    }

    public void Release()
    {
        Engaged = false;
        snapshots.Clear();
    }

    /// <summary>
    /// Remembers the pairs as they stand, carrying nothing. Called after the panel
    /// rebinds its blocks to other pair objects (a loaded session, a reset) — without
    /// it the first edit after a load would meet an unknown pair, be recorded as its
    /// starting state, and never be mirrored — and after a run that decided both sides
    /// itself, whose "keep" for the hidden side a difference cannot tell from a hand
    /// that only reached the shown one.
    /// </summary>
    public void Remember(IEnumerable<VirtualCrossoverChannelPairSettings> pairs)
    {
        snapshots.Clear();
        if (!Engaged)
        {
            return;
        }

        foreach (VirtualCrossoverChannelPairSettings pair in pairs)
        {
            snapshots[pair] = Take(pair);
        }
    }

    /// <summary>
    /// Carries what moved on the shown side since the previous call onto the hidden
    /// side of each stereo pair, and remembers both sides as they now stand. A pair
    /// seen for the first time is only remembered; one no longer listed is forgotten.
    /// </summary>
    /// <returns>Whether anything was written to a hidden side.</returns>
    public bool Follow(IEnumerable<VirtualCrossoverChannelPairSettings> pairs, bool shownRight)
    {
        if (!Engaged)
        {
            return false;
        }

        bool wrote = false;
        var current = new Dictionary<VirtualCrossoverChannelPairSettings, (Snapshot Left, Snapshot Right)>();
        foreach (VirtualCrossoverChannelPairSettings pair in pairs)
        {
            // A mono pair has one settings set, so there is nothing to keep in step;
            // its physical right side is still remembered, so that turning Mono off
            // later starts from what that side actually holds.
            if (!pair.Mono && snapshots.TryGetValue(pair, out (Snapshot Left, Snapshot Right) before))
            {
                wrote |= Carry(pair, before, shownRight);
            }

            current[pair] = Take(pair);
        }

        snapshots.Clear();
        foreach (KeyValuePair<VirtualCrossoverChannelPairSettings, (Snapshot Left, Snapshot Right)> entry in current)
        {
            snapshots[entry.Key] = entry.Value;
        }

        return wrote;
    }

    private static bool Carry(
        VirtualCrossoverChannelPairSettings pair,
        (Snapshot Left, Snapshot Right) before,
        bool shownRight)
    {
        VirtualCrossoverChannelSettings shown = pair.SideFor(shownRight);
        VirtualCrossoverChannelSettings hidden = pair.SideFor(!shownRight);
        Snapshot shownBefore = shownRight ? before.Right : before.Left;
        Snapshot shownNow = Snapshot.Of(shown);
        Snapshot hiddenNow = Snapshot.Of(hidden);

        bool wrote = false;
        if (shownNow.Crossover != shownBefore.Crossover &&
            hiddenNow.Crossover != shownNow.Crossover)
        {
            shownNow.Crossover.WriteTo(hidden);
            wrote = true;
        }

        if (shownNow.Inverted != shownBefore.Inverted &&
            hiddenNow.Inverted != shownNow.Inverted)
        {
            hidden.InvertPolarity = shownNow.Inverted;
            wrote = true;
        }

        return wrote;
    }

    // The PHYSICAL sides, mono routing ignored, for the same reason the channel keeps
    // a physical accessor: through the routed one a mono pair's right side is
    // unreachable, and its snapshot would silently become a copy of the left.
    private static (Snapshot Left, Snapshot Right) Take(VirtualCrossoverChannelPairSettings pair) =>
        (Snapshot.Of(pair.Left), Snapshot.Of(pair.Right));
}
