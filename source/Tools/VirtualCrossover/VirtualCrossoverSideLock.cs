using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The Virtual DSP side Lock: crossover, polarity and FIR-crossover edits on the shown side are mirrored onto the hidden side,
/// detected by difference against snapshots. Runs that write both sides must call <see cref="Remember"/>.
/// See docs/tech/virtual-dsp-session-file.md#side-lock.
/// </summary>
internal sealed class VirtualCrossoverSideLock
{
    // Record equality over kind and both edges: the crossover moves as one unit.
    private readonly record struct Crossover(
        CrossoverKind Kind,
        CrossoverEdge HighPass,
        CrossoverEdge LowPass,
        JunctionAcousticTarget? AcousticHighPass,
        JunctionAcousticTarget? AcousticLowPass)
    {
        public static Crossover Of(VirtualCrossoverChannelSettings settings) =>
            new(
                settings.CrossoverKind,
                settings.HighPassEdge,
                settings.LowPassEdge,
                // The acoustic wish rides with the filter: it says what this crossover should sound like, and a
                // crossover is one electrical filter for both sides.
                settings.AcousticHighPass,
                settings.AcousticLowPass);

        public void WriteTo(VirtualCrossoverChannelSettings settings)
        {
            settings.CrossoverKind = Kind;
            settings.HighPassEdge = HighPass;
            settings.LowPassEdge = LowPass;
            settings.AcousticHighPass = AcousticHighPass;
            settings.AcousticLowPass = AcousticLowPass;
        }
    }

    // FirFilter has no value equality; the kernel compares by instance.
    private readonly record struct FirStage(
        FirFilter? Kernel,
        string? SourceName,
        FirCrossoverDesign? Design)
    {
        public static FirStage Of(VirtualCrossoverChannelSettings settings) =>
            new(settings.Fir, settings.FirSourceName, settings.FirDesign);

        public void WriteTo(VirtualCrossoverChannelSettings settings)
        {
            settings.Fir = Kernel;
            settings.FirSourceName = SourceName;
            settings.FirDesign = Design;
        }
    }

    private readonly record struct Snapshot(Crossover Crossover, bool Inverted, FirStage Fir)
    {
        public static Snapshot Of(VirtualCrossoverChannelSettings settings) =>
            new(Crossover.Of(settings), settings.InvertPolarity, FirStage.Of(settings));
    }

    // Keyed by pair object (reference identity): a loaded session binds new pairs, invalidating old snapshots.
    private readonly Dictionary<
        VirtualCrossoverChannelPairSettings, (Snapshot Left, Snapshot Right)> snapshots = new();

    public bool Engaged { get; private set; }

    /// <summary>Turns the lock on, copying nothing; only later moves are carried.</summary>
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

    /// <summary>Remembers pairs without carrying: after a rebind, and after a run that decided both sides itself.</summary>
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

    /// <summary>Carries shown-side moves since the previous call onto hidden sides and remembers both sides.</summary>
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
            // Mono: nothing to mirror, but the physical right side is remembered for a later Mono-off.
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

        if (shownNow.Fir != shownBefore.Fir &&
            hiddenNow.Fir != shownNow.Fir &&
            CarriesFir(shownBefore.Fir, shownNow.Fir, hiddenNow.Fir))
        {
            shownNow.Fir.WriteTo(hidden);
            wrote = true;
        }

        return wrote;
    }

    // Carried: anything -> designed (unless hidden is imported); designed -> cleared (onto a hidden crossover).
    // Never carried: imported -> cleared, anything -> imported.
    private static bool CarriesFir(FirStage before, FirStage now, FirStage hidden)
    {
        if (IsImported(hidden))
        {
            return false;
        }

        if (IsCrossover(now))
        {
            return true;
        }

        return now.Kernel == null && IsCrossover(before) && IsCrossover(hidden);

        static bool IsCrossover(FirStage stage) => stage.Kernel != null && stage.Design != null;

        static bool IsImported(FirStage stage) => stage.Kernel != null && stage.Design == null;
    }

    // Physical sides: through mono routing the right snapshot would silently copy the left.
    private static (Snapshot Left, Snapshot Right) Take(VirtualCrossoverChannelPairSettings pair) =>
        (Snapshot.Of(pair.Left), Snapshot.Of(pair.Right));
}
