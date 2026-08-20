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

    public static SampleRateResolution Resolve(
        IReadOnlyList<int> supportedRates,
        int preferredSampleRate,
        bool hasExistingList)
    {
        ArgumentNullException.ThrowIfNull(supportedRates);

        if (supportedRates.Count == 0 && hasExistingList)
        {
            return new SampleRateResolution([], preferredSampleRate, true, null);
        }

        int[] rates = supportedRates.Count > 0
            ? [.. supportedRates]
            : [preferredSampleRate > 0 ? preferredSampleRate : FallbackSampleRate];

        bool fellBack = !rates.Contains(preferredSampleRate);
        return new SampleRateResolution(
            rates,
            fellBack ? rates[0] : preferredSampleRate,
            false,
            fellBack ? preferredSampleRate : null);
    }
}
