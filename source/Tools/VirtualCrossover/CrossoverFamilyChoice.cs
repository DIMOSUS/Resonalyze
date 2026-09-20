using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A crossover family as a combo item, spelled the way the rest of the UI spells it ("Linkwitz-Riley", not the enum
/// name). Chebyshev is not offered: its passband ripple needs a value of its own, and the crossover searches do not
/// offer it either.
/// </summary>
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
