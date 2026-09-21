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

    public override string ToString() => FirCrossoverDescription.FamilyName(Value);
}
