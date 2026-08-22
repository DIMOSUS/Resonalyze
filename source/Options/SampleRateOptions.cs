namespace Resonalyze.Options;

/// <summary>
/// What a sample-rate probe amounted to, and what the rate list should therefore be.
/// </summary>
/// <param name="Rates">
/// The rates to offer, or empty when the existing list should be left alone.
/// </param>
/// <param name="Selected">The rate to select; the preferred one unless it fell back.</param>
/// <param name="ProbeFailed">
/// The driver returned nothing usable. Not the same as "the configured rate is
/// unsupported" — it is the absence of an answer, and nothing should be changed on it.
/// Reported by the caller, which is the only place that can tell the two apart.
/// </param>
/// <param name="FellBackFrom">
/// The rate the user had, when the driver answered and did not offer it. Null when
/// nothing was taken away from them.
/// </param>
internal readonly record struct SampleRateResolution(
    int[] Rates,
    int Selected,
    bool ProbeFailed,
    int? FellBackFrom);

/// <summary>
/// Decides the rate list from a probe, separately from the panel that displays it.
/// </summary>
/// <remarks>
/// The distinction this exists to keep is between a driver that says "not that rate"
/// and a driver that says nothing at all. Some ASIO drivers refuse to be opened a
/// second time moments after the first, which is what closing and reopening the
/// settings window does, and their answer to "which rates do you support" is then an
/// empty list. Treating that as an answer replaces the user's configured rate with a
/// fallback constant, and the next Apply persists it.
/// </remarks>
internal static class SampleRateOptions
{
    /// <summary>The rate used when there is nothing else to go on at all.</summary>
    public const int FallbackSampleRate = 44_100;

    /// <summary>
    /// Whether a probe answered with SILENCE rather than with an empty answer — the
    /// distinction <see cref="Resolve"/> cannot make for itself.
    /// </summary>
    /// <remarks>
    /// Only ASIO can fall silent. Every other backend derives its list from device
    /// descriptors, where empty is a real answer — WASAPI Shared endpoints whose mix
    /// rates differ, an Exclusive or Wave pair with no rate in common — and the list
    /// must be rebuilt from it rather than left standing. A named ASIO driver is the
    /// one case where empty cannot be an answer: a driver that opens accepts at least
    /// one standard rate, so naming none is silence, which is what a driver refusing a
    /// second open produces. With no driver selected there is nothing to preserve.
    /// </remarks>
    public static bool IsProbeFailure(
        bool isAsioBackend,
        string? asioDriverName,
        int reportedRateCount) =>
        isAsioBackend &&
        !string.IsNullOrWhiteSpace(asioDriverName) &&
        reportedRateCount == 0;

    /// <param name="probeFailed">
    /// Whether the probe produced no answer at all. An empty <paramref name="supportedRates"/>
    /// cannot stand in for this: it is a real answer everywhere except ASIO — WASAPI Shared
    /// endpoints whose mix rates differ, an Exclusive or Wave pair with no rate in common —
    /// and rebuilding the list from it is then correct. Only the caller knows which it has.
    /// </param>
    public static SampleRateResolution Resolve(
        IReadOnlyList<int> supportedRates,
        int preferredSampleRate,
        bool hasExistingList,
        bool probeFailed)
    {
        ArgumentNullException.ThrowIfNull(supportedRates);

        if (probeFailed && hasExistingList)
        {
            return new SampleRateResolution([], preferredSampleRate, true, null);
        }

        int[] rates = supportedRates.Count > 0
            ? [.. supportedRates]
            : [preferredSampleRate > 0 ? preferredSampleRate : FallbackSampleRate];

        // A probe that failed with nothing on screen to keep still failed: the list
        // below is the configured rate standing alone, not something a driver offered,
        // and the status line has to say so rather than pronounce the rate supported.
        bool fellBack = !rates.Contains(preferredSampleRate);
        return new SampleRateResolution(
            rates,
            fellBack ? rates[0] : preferredSampleRate,
            probeFailed,
            fellBack ? preferredSampleRate : null);
    }
}
