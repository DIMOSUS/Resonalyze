namespace Resonalyze.Dsp;

/// <summary>
/// The shape of a filter slot: a bell, or one of the two shelves. All three take
/// the same three numbers — centre frequency, Q and gain — but read two of them
/// differently, which is the whole reason the type has to travel with the band.
/// </summary>
/// <remarks>
/// For both shelves the RBJ cookbook puts <see cref="PeqBand.FrequencyHz"/> at the
/// MIDDLE of the transition, where the response has reached half the shelf gain in
/// dB, and <see cref="PeqBand.Q"/> controls the knee rather than a bandwidth:
/// 1/sqrt(2) ~ 0.707 is the steepest shelf that stays monotonic, and anything
/// above it overshoots before settling. A shelf therefore has no "bandwidth", and
/// the Q conventions of <see cref="PeqQConvention"/> — which restate a bell's
/// bandwidth — do not apply to one.
/// </remarks>
public enum PeqBandType
{
    /// <summary>Peaking / bell, the default: a boost or cut centred on the band.</summary>
    Peaking,

    /// <summary>Low shelf: everything below the band is lifted or lowered.</summary>
    LowShelf,

    /// <summary>High shelf: everything above the band is lifted or lowered.</summary>
    HighShelf
}

/// <summary>Shape questions asked of a <see cref="PeqBandType"/>.</summary>
public static class PeqBandTypes
{
    /// <summary>
    /// True only for the two shelves. Everything else — the bell, and a value no
    /// member matches — is a bell, which is what the realization, the header and
    /// every profile writer already fall back to.
    /// </summary>
    /// <remarks>
    /// Asked through this rather than by testing <c>!= Peaking</c>, so a settings
    /// file carrying an out-of-range number cannot be a bell to the filter and a
    /// shelf to the tuning sheet at the same time.
    /// </remarks>
    public static bool IsShelving(this PeqBandType type) =>
        type is PeqBandType.LowShelf or PeqBandType.HighShelf;
}
