using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A family as a combo item, spelled as the UI spells it. Chebyshev is not offered: it needs a ripple.</summary>
internal sealed record CrossoverFamilyChoice(CrossoverFilterFamily Value)
{
    public static IReadOnlyList<CrossoverFamilyChoice> Offered { get; } =
    [
        new(CrossoverFilterFamily.Butterworth),
        new(CrossoverFilterFamily.LinkwitzRiley),
        new(CrossoverFilterFamily.Bessel)
    ];

    /// <summary>The slopes an acoustic goal of this family can state.</summary>
    public IReadOnlyList<int> GoalSlopes => CrossoverFilter.SupportedSlopes(Value);

    /// <summary>The slope a goal box shows once this family is picked: the one it held where the family offers it, else
    /// the family's second slope.</summary>
    public int GoalSlope(int? kept)
    {
        IReadOnlyList<int> slopes = GoalSlopes;
        return kept is { } previous && slopes.Contains(previous) ? previous : slopes[Math.Min(1, slopes.Count - 1)];
    }

    public override string ToString() => FirCrossoverDescription.FamilyName(Value);
}
