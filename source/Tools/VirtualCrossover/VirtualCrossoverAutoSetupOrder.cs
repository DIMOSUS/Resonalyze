using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Which of a group's drivers the crossover wizard walks first, and when it has
/// to stop and ask instead of deciding.
/// </summary>
/// <remarks>
/// The optimizer walks its channels in the order it is handed them, so somebody
/// has to say which plays lower. The driver type used to: sorting by it put a
/// subwoofer under a midbass under a tweeter, which is right until two channels
/// share a type — a pair of subs dividing the bottom — and then it says nothing
/// at all. What does say something is the band each one measured, narrowed by
/// whatever corners the channel already carries. Setting those corners is how
/// the user answers the question when the raw measurement cannot: two subs
/// measured full-range look alike, and one crossed at 50 Hz and one under it do
/// not.
/// </remarks>
public static class VirtualCrossoverAutoSetupOrder
{
    /// <summary>
    /// Two drivers whose effective band centres sit closer than this are not
    /// ordered by anything the measurement can see, so the wizard asks before it
    /// commits to a chain. Half an octave is the same separation the optimizer
    /// demands between two adjacent junctions — closer than that and there is no
    /// room for a handover between them anyway.
    /// </summary>
    public const double AmbiguousSeparationOctaves = 0.5;

    /// <summary>
    /// The band a channel actually contributes: what it measured, narrowed by any
    /// crossover corner already set on it. A corner pair that leaves nothing at
    /// all says less than the measurement does, so the measured band stands.
    /// </summary>
    public static (double LowHz, double HighHz) EffectiveBand(
        DriverBandEstimate band,
        double? highPassHz,
        double? lowPassHz)
    {
        ArgumentNullException.ThrowIfNull(band);

        double low = Math.Max(band.LowHz, highPassHz ?? 0);
        double high = Math.Min(band.HighHz, lowPassHz ?? double.PositiveInfinity);
        return high > low ? (low, high) : (band.LowHz, band.HighHz);
    }

    /// <summary>
    /// The log-centre of <see cref="EffectiveBand"/> — the single number the
    /// wizard orders a group by.
    /// </summary>
    public static double CenterHz(
        DriverBandEstimate band,
        double? highPassHz,
        double? lowPassHz)
    {
        (double low, double high) = EffectiveBand(band, highPassHz, lowPassHz);
        return Math.Sqrt(low * high);
    }

    /// <summary>
    /// What the measurement makes of one adjacent pair of the chain, given the
    /// centre of the channel placed FIRST and of the one placed after it.
    /// </summary>
    public static VirtualCrossoverChainOrder Judge(
        double earlierCenterHz,
        double laterCenterHz)
    {
        if (!(earlierCenterHz > 0) || !(laterCenterHz > 0))
        {
            // A band estimate that collapsed says nothing about the order; a
            // channel with no usable band is reported on its own terms elsewhere.
            return VirtualCrossoverChainOrder.AsMeasured;
        }

        double octaves = Math.Log2(laterCenterHz / earlierCenterHz);
        if (Math.Abs(octaves) < AmbiguousSeparationOctaves)
        {
            return VirtualCrossoverChainOrder.Unclear;
        }

        return octaves > 0
            ? VirtualCrossoverChainOrder.AsMeasured
            : VirtualCrossoverChainOrder.Reversed;
    }
}

/// <summary>
/// What the measurement says about where two neighbours of a chain sit relative
/// to each other — the chain runs low to high, so the second of a pair should
/// measure the higher.
/// </summary>
public enum VirtualCrossoverChainOrder
{
    /// <summary>The later channel measures clearly higher: the order is the measured one.</summary>
    AsMeasured,

    /// <summary>
    /// The two measure within half an octave of each other, so nothing read off
    /// them put one above the other — a pair of subwoofers measured full range.
    /// The order shown is a guess, and the wizard says so.
    /// </summary>
    Unclear,

    /// <summary>
    /// The later channel measures clearly LOWER — the chain runs backwards here.
    /// Almost always somebody moved a row the wrong way, or confirmed a driver
    /// type that does not match what the channel plays.
    /// </summary>
    Reversed
}
