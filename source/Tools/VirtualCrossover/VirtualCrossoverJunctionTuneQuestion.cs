using Resonalyze.Dsp;

namespace Resonalyze;

internal enum JunctionTuneStatusTone
{
    Muted,
    Warning,
    Error,
    Success
}

/// <summary>The question Tune junction asks, as its fields show it: the junction and its corner window (remembered per
/// junction), the families, slopes and mode, the acoustic goal, the budget; and the answer standing for it. Every change
/// retires the answer, so Apply never stands for a question nobody asked. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
internal sealed class VirtualCrossoverJunctionTuneQuestion
{
    /// <summary>What the goal may cost against the best sum; measured in docs/tech/crossover-auto-setup.md#measured-on-the-battery.</summary>
    public const double DefaultSumBudgetDb = 1.0;

    public const string Again = "The question changed — search again.";
    public const string NothingYet = "Nothing searched yet.";
    public const string NoFamily = "Tick at least one filter family to search.";
    public const string NoJunction = "This view has no junction with two measured blocks.";

    /// <summary>As the crossover wizard offers them: from 12 dB/oct.</summary>
    public static readonly IReadOnlyList<int> SelectableSlopes = CrossoverFilter
        .SupportedSlopes(CrossoverFilterFamily.Butterworth)
        .Where(slope => slope >= CrossoverJunctionTuner.PracticalSlopeFloorDbPerOctave)
        .ToArray();

    public static readonly NumericFieldRange CornerRange = new(
        (decimal)VirtualCrossoverJunctionTuneSettings.WindowLowestHz,
        (decimal)VirtualCrossoverJunctionTuneSettings.WindowHighestHz,
        0);

    public static readonly NumericFieldRange BudgetRange = new(0m, 3m, 1);

    private readonly Dictionary<string, (decimal Min, decimal Max)> windows = new(StringComparer.Ordinal);
    private Func<int, JunctionTuneDefaults> defaultsFor = _ => new JunctionTuneDefaults(20, 20_000, [], null);
    private string? shownJunction;

    // Only the first junction shown, with nothing remembered, opens on its own families and goal.
    private bool useJunctionDefaults = true;

    public IReadOnlyList<string> Junctions { get; private set; } = [];

    public int JunctionIndex { get; private set; } = -1;

    public decimal MinHz { get; private set; } = 500;

    public decimal MaxHz { get; private set; } = 2_000;

    public bool Butterworth { get; private set; }

    public bool LinkwitzRiley { get; private set; }

    public bool Bessel { get; private set; }

    public bool IndependentSlopes { get; private set; } = true;

    public bool SplitCorners { get; private set; } = true;

    public bool Acoustic { get; private set; }

    public int MinSlope { get; private set; } = SelectableSlopes[0];

    public int MaxSlope { get; private set; } = SelectableSlopes[^1];

    public decimal SumBudget { get; private set; } = (decimal)DefaultSumBudgetDb;

    public CrossoverFamilyChoice? GoalFamily { get; private set; }

    /// <summary>What the goal's slope box offers: the goal family's slopes, none without a family.</summary>
    public IReadOnlyList<int> GoalSlopes { get; private set; } = [];

    public int? GoalSlope { get; private set; }

    /// <summary>Bumped by every change: a search that returns to another question lands nothing.</summary>
    public int Question { get; private set; }

    /// <summary>The request the standing answer was searched for; null until a search that may be applied lands.</summary>
    public JunctionTuneRequest? Result { get; private set; }

    public IReadOnlyList<JunctionTuneLine> Report { get; private set; } = [];

    public string Status { get; private set; } = NothingYet;

    public JunctionTuneStatusTone StatusTone { get; private set; } = JunctionTuneStatusTone.Muted;

    public bool Searching { get; private set; }

    public IReadOnlyList<CrossoverFilterFamily> Families =>
        new[]
            {
                (Butterworth, CrossoverFilterFamily.Butterworth),
                (LinkwitzRiley, CrossoverFilterFamily.LinkwitzRiley),
                (Bessel, CrossoverFilterFamily.Bessel)
            }
            .Where(family => family.Item1)
            .Select(family => family.Item2)
            .ToList();

    public void Open(
        IReadOnlyList<string> junctions,
        Func<int, JunctionTuneDefaults> defaults,
        VirtualCrossoverJunctionTuneSettings? remembered)
    {
        Junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        defaultsFor = defaults ?? throw new ArgumentNullException(nameof(defaults));
        if (remembered != null)
        {
            Restore(remembered);
        }

        if (junctions.Count > 0)
        {
            int last = remembered?.Junction is { } label ? IndexOf(junctions, label) : -1;
            ShowJunction(Math.Max(0, last));
        }
        else
        {
            Status = NoJunction;
            StatusTone = JunctionTuneStatusTone.Warning;
        }
    }

    /// <summary>Switching junction changes only its corner window: a remembered one while it still holds the junction's
    /// corner, else the junction's default. The rest of the question stays as set.</summary>
    public void ShowJunction(int index)
    {
        if (index < 0 || index >= Junctions.Count)
        {
            return;
        }

        KeepShownWindow();
        JunctionIndex = index;
        string label = Junctions[index];
        JunctionTuneDefaults opening = defaultsFor(index);
        (decimal min, decimal max) = windows.TryGetValue(label, out (decimal Min, decimal Max) kept) &&
            (opening.CornerHz is not { } corner || ((double)kept.Min <= corner && corner <= (double)kept.Max))
                ? kept
                : (CornerRange.Clamp(opening.MinHz), CornerRange.Clamp(opening.MaxHz));
        SetWindow(CornerRange.Clamp((double)min), CornerRange.Clamp((double)max));
        shownJunction = label;
        if (useJunctionDefaults)
        {
            useJunctionDefaults = false;
            SetFamily(CrossoverFilterFamily.Butterworth, opening.Families.Contains(CrossoverFilterFamily.Butterworth));
            SetFamily(CrossoverFilterFamily.LinkwitzRiley, opening.Families.Contains(CrossoverFilterFamily.LinkwitzRiley));
            SetFamily(CrossoverFilterFamily.Bessel, opening.Families.Contains(CrossoverFilterFamily.Bessel));
            if (opening.Goal is { } asked)
            {
                ShowGoal(asked);
                SetMode(acoustic: true);
            }
        }

        Report = [];
        Retire(NothingYet);
    }

    public void SetWindow(decimal minHz, decimal maxHz)
    {
        if (MinHz != minHz)
        {
            MinHz = minHz;
            Retire(Again);
        }

        if (MaxHz != maxHz)
        {
            MaxHz = maxHz;
            Retire(Again);
        }
    }

    public void SetSlopeWindow(int minSlope, int maxSlope)
    {
        if (MinSlope != minSlope)
        {
            MinSlope = minSlope;
            Retire(Again);
        }

        if (MaxSlope != maxSlope)
        {
            MaxSlope = maxSlope;
            Retire(Again);
        }
    }

    public void SetFamily(CrossoverFilterFamily family, bool allowed)
    {
        if (Families.Contains(family) == allowed)
        {
            return;
        }

        switch (family)
        {
            case CrossoverFilterFamily.Butterworth:
                Butterworth = allowed;
                break;
            case CrossoverFilterFamily.LinkwitzRiley:
                LinkwitzRiley = allowed;
                break;
            case CrossoverFilterFamily.Bessel:
                Bessel = allowed;
                break;
            default:
                return;
        }

        Retire(Again);
    }

    public void SetIndependentSlopes(bool free)
    {
        if (IndependentSlopes != free)
        {
            IndependentSlopes = free;
            Retire(Again);
        }
    }

    public void SetSplitCorners(bool free)
    {
        if (SplitCorners != free)
        {
            SplitCorners = free;
            Retire(Again);
        }
    }

    public void SetSumBudget(decimal budgetDb)
    {
        if (SumBudget != budgetDb)
        {
            SumBudget = budgetDb;
            Retire(Again);
        }
    }

    /// <summary>The acoustic mode needs a goal family, so it opens on Linkwitz-Riley when none is set.</summary>
    public void SetMode(bool acoustic)
    {
        Acoustic = acoustic;
        if (acoustic && GoalFamily == null)
        {
            SetGoalFamily(CrossoverFamilyChoice.Offered
                .First(choice => choice.Value == CrossoverFilterFamily.LinkwitzRiley));
        }

        Retire(Again);
    }

    /// <summary>A new family offers its own slopes and keeps the slope it held where it offers it; null states no goal.</summary>
    public void SetGoalFamily(CrossoverFamilyChoice? family)
    {
        if (Equals(GoalFamily, family))
        {
            return;
        }

        GoalFamily = family;
        if (family == null)
        {
            GoalSlopes = [];
            GoalSlope = null;
            Retire();
            return;
        }

        GoalSlopes = family.GoalSlopes;
        GoalSlope = family.GoalSlope(GoalSlope);
        Retire(Again);
    }

    public void SetGoalSlope(int slope)
    {
        if (GoalSlope != slope && GoalSlopes.Contains(slope))
        {
            GoalSlope = slope;
            Retire(Again);
        }
    }

    /// <summary>The request a search is run for, or null when no family is ticked (the status then says so).</summary>
    public JunctionTuneRequest? Ask()
    {
        if (Searching || JunctionIndex < 0)
        {
            return null;
        }

        List<CrossoverFilterFamily> families = Families.ToList();
        if (families.Count == 0)
        {
            Status = NoFamily;
            StatusTone = JunctionTuneStatusTone.Warning;
            return null;
        }

        // The slope window as the two boxes state it, either way round; empty means every slope the families have.
        List<int> slopes = Acoustic
            ? []
            : SelectableSlopes
                .Where(slope => slope >= Math.Min(MinSlope, MaxSlope) && slope <= Math.Max(MinSlope, MaxSlope))
                .ToList();
        return new JunctionTuneRequest(
            JunctionIndex,
            (double)MinHz,
            (double)MaxHz,
            families,
            slopes,
            IndependentSlopes,
            Acoustic && GoalFamily is { } goalFamily && GoalSlope is { } goalSlope
                ? new JunctionAcousticTarget(goalFamily.Value, goalSlope)
                : null,
            SplitCorners,
            (double)SumBudget);
    }

    /// <summary>A search starts: returns the question it answers, for <see cref="Land"/>.</summary>
    public int BeginSearch()
    {
        Searching = true;
        Result = null;
        Status = "Searching…";
        StatusTone = JunctionTuneStatusTone.Muted;
        return Question;
    }

    public void EndSearch() => Searching = false;

    /// <summary>The answer lands only while its question stands: a question changed during the search has said so already.</summary>
    public bool Land(int asked, JunctionTuneRequest request, JunctionTuneOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (asked != Question)
        {
            return false;
        }

        Report = outcome.Report;
        Status = outcome.Status;
        StatusTone = outcome.Refused
            ? JunctionTuneStatusTone.Error
            : outcome.Recommended ? JunctionTuneStatusTone.Success : JunctionTuneStatusTone.Warning;
        Result = outcome.CanApply ? request : null;
        return true;
    }

    /// <summary>What the next dialog restores: the question as it stands and every junction's window.</summary>
    public VirtualCrossoverJunctionTuneSettings Remembered()
    {
        KeepShownWindow();
        return new VirtualCrossoverJunctionTuneSettings
        {
            Junction = shownJunction,
            Families = Families.ToList(),
            IndependentSlopes = IndependentSlopes,
            SplitCorners = SplitCorners,
            Acoustic = Acoustic,
            MinSlopeDbPerOctave = MinSlope,
            MaxSlopeDbPerOctave = MaxSlope,
            SumBudgetDb = (double)SumBudget,
            Goal = GoalFamily is { } family && GoalSlope is { } slope
                ? new JunctionAcousticTarget(family.Value, slope)
                : null,
            Windows = windows.ToDictionary(
                pair => pair.Key,
                pair => new[] { (double)pair.Value.Min, (double)pair.Value.Max },
                StringComparer.Ordinal)
        };
    }

    private void Retire(string? status = null)
    {
        Question++;
        Result = null;
        if (status != null)
        {
            Status = status;
            StatusTone = JunctionTuneStatusTone.Muted;
        }
    }

    private void KeepShownWindow()
    {
        if (shownJunction != null)
        {
            windows[shownJunction] = (MinHz, MaxHz);
        }
    }

    // A family the menus no longer offer shows no goal; a slope its family no longer offers keeps the family's default.
    private void ShowGoal(JunctionAcousticTarget goal)
    {
        SetGoalFamily(CrossoverFamilyChoice.Offered.FirstOrDefault(choice => choice.Value == goal.Family));
        SetGoalSlope(goal.SlopeDbPerOctave);
    }

    /// <summary>Anything the menus no longer offer keeps its default.</summary>
    private void Restore(VirtualCrossoverJunctionTuneSettings remembered)
    {
        useJunctionDefaults = false;
        SetFamily(CrossoverFilterFamily.Butterworth, remembered.Families.Contains(CrossoverFilterFamily.Butterworth));
        SetFamily(CrossoverFilterFamily.LinkwitzRiley, remembered.Families.Contains(CrossoverFilterFamily.LinkwitzRiley));
        SetFamily(CrossoverFilterFamily.Bessel, remembered.Families.Contains(CrossoverFilterFamily.Bessel));
        SetIndependentSlopes(remembered.IndependentSlopes);
        SetSplitCorners(remembered.SplitCorners);
        SetSlopeWindow(
            remembered.MinSlopeDbPerOctave is { } low && SelectableSlopes.Contains(low) ? low : MinSlope,
            remembered.MaxSlopeDbPerOctave is { } high && SelectableSlopes.Contains(high) ? high : MaxSlope);
        if (remembered.Goal is { } goal)
        {
            ShowGoal(goal);
        }

        SetSumBudget(BudgetRange.Clamp(remembered.SumBudgetDb ?? DefaultSumBudgetDb));
        if (remembered.Acoustic != Acoustic)
        {
            SetMode(remembered.Acoustic);
        }

        foreach ((string label, double[] window) in remembered.Windows)
        {
            // Clamped as a double: a hand-edited file may hold a figure no decimal can.
            if (window is [var min, var max])
            {
                windows[label] = (CornerRange.Clamp(min), CornerRange.Clamp(max));
            }
        }
    }

    private static int IndexOf(IReadOnlyList<string> junctions, string label)
    {
        for (int i = 0; i < junctions.Count; i++)
        {
            if (string.Equals(junctions[i], label, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
