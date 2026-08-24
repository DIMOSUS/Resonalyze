namespace Resonalyze.Dsp;

/// <summary>
/// The shape of a filter slot: a bell, one of the two shelves, or one of the two
/// all-pass orders. All of them take the same three numbers — centre frequency, Q
/// and gain — but read them differently (an all-pass reads no gain at all), which
/// is the whole reason the type has to travel with the band.
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
    HighShelf,

    /// <summary>
    /// First-order all-pass: unity magnitude at every frequency, 180° of phase
    /// swing, −90° at <see cref="PeqBand.FrequencyHz"/>. Reads neither gain nor Q
    /// (a single real pole has no Q); the slot keeps Q at its usual default so the
    /// band still passes the validators that require a positive Q.
    /// </summary>
    AllPassFirstOrder,

    /// <summary>
    /// Second-order all-pass: unity magnitude at every frequency, 360° of phase
    /// swing, −180° at the corner. Reads no gain; <see cref="PeqBand.Q"/> sets how
    /// abruptly the phase turns, and therefore how much group delay piles up at the
    /// corner — see <see cref="AllPassFilter"/>.
    /// </summary>
    AllPassSecondOrder
}

/// <summary>Shape questions asked of a <see cref="PeqBandType"/>.</summary>
public static class PeqBandTypes
{
    /// <summary>
    /// True only for the two shelves. Everything that is neither a shelf nor an
    /// all-pass — the bell, and a value no member matches — is a bell, which is
    /// what the realization, the header and every profile writer fall back to.
    /// </summary>
    /// <remarks>
    /// Asked through this rather than by testing <c>!= Peaking</c>, so a settings
    /// file carrying an out-of-range number cannot be a bell to the filter and a
    /// shelf to the tuning sheet at the same time.
    /// </remarks>
    public static bool IsShelving(this PeqBandType type) =>
        type is PeqBandType.LowShelf or PeqBandType.HighShelf;

    /// <summary>
    /// True only for the two all-pass orders — the bands that move phase without
    /// touching magnitude. They are the one shape whose zero gain does not mean
    /// "transparent", which is why <see cref="PeqBand.IsTransparent"/> asks this.
    /// </summary>
    public static bool IsAllPass(this PeqBandType type) =>
        type is PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder;
}
