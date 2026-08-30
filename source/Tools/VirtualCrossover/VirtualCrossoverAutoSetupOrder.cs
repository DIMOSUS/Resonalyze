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
    /// Whether the two centres are too close together for their order to have
    /// been read off the measurement rather than guessed.
    /// </summary>
    public static bool IsAmbiguous(double oneCenterHz, double otherCenterHz) =>
        oneCenterHz > 0 &&
        otherCenterHz > 0 &&
        Math.Abs(Math.Log2(otherCenterHz / oneCenterHz)) < AmbiguousSeparationOctaves;
}
