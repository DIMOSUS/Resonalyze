namespace Resonalyze.Options;

/// <summary>
/// What a sample-rate probe amounted to, and what the rate list should therefore be.
/// </summary>
/// <param name="Rates">
/// The rates to offer. THREE outcomes, and they are not the same thing:
/// <c>null</c> — nothing to rebuild, the list already on screen stands;
/// empty — the probe answered and the answer is that NO rate works for this
/// configuration, so nothing may be offered and Apply has to refuse;
/// non-empty — offer exactly these.
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
    int[]? Rates,
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

    /// <summary>
    /// Refuses, on the way to configuration, a rate the devices never reported.
    /// </summary>
    /// <remarks>
    /// For the backends that derive their list by asking the devices — Wave, and WASAPI
    /// Exclusive — an empty list is an ANSWER: no rate works for this configuration. The
    /// combo is then empty, and an empty combo is where the panel's own
    /// <c>GetSelectedSampleRate</c> falls back to <see cref="FallbackSampleRate"/>. Without
    /// this check that fallback becomes configuration: Apply persists 44.1 kHz for an
    /// endpoint pair that has just said it cannot open it. ASIO must not be routed through
    /// here — there an empty list can be a driver that refused to open, which is the
    /// silence <see cref="IsProbeFailure"/> exists to tell apart, and refusing on it would
    /// block a configuration that works.
    /// </remarks>
    /// <param name="deviceDescription">
    /// How to name the devices in the message; the sentence continues from it.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The list is empty, or the rate is not in it.
    /// </exception>
    public static void ValidateSelectedRate(
        IReadOnlyList<int> supportedRates,
        int sampleRate,
        string deviceDescription)
    {
        ArgumentNullException.ThrowIfNull(supportedRates);

        if (supportedRates.Count == 0)
        {
            throw new InvalidOperationException(
                $"{deviceDescription} report no sample rate in common for the current " +
                "configuration. Change the devices, the channel counts or the bit depth.");
        }

        if (!supportedRates.Contains(sampleRate))
        {
            throw new InvalidOperationException(
                $"{deviceDescription} do not support {sampleRate} Hz for the current configuration.");
        }
    }

    /// <summary>
    /// Which entry of a rate list to select, or -1 when there is nothing to select.
    /// </summary>
    /// <remarks>
    /// The empty list is the case this exists for. It is a real outcome — no rate opens
    /// for this configuration — and the combo that shows it has no entry to select, so
    /// asking for entry 0 there throws where the panel cannot recover: the rebuild is
    /// abandoned half-done and the settings window stops responding to selections. The
    /// fallback to 0 on a non-empty list is defensive; <see cref="Resolve"/> only ever
    /// names a rate the list contains.
    /// </remarks>
    public static int FindRateIndex(IReadOnlyList<int> rates, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(rates);

        for (int i = 0; i < rates.Count; i++)
        {
            if (rates[i] == sampleRate)
            {
                return i;
            }
        }

        return rates.Count > 0 ? 0 : -1;
    }

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

        if (probeFailed)
        {
            // Silence. With a list on screen that list is the last real answer anyone
            // got, and it stands. With nothing on screen there is nothing to stand, so
            // the configured rate is offered alone — still reported as a failed probe,
            // because no driver ever said it was supported.
            if (hasExistingList)
            {
                return new SampleRateResolution(null, preferredSampleRate, true, null);
            }

            int synthesized = preferredSampleRate > 0 ? preferredSampleRate : FallbackSampleRate;
            return new SampleRateResolution([synthesized], synthesized, true, null);
        }

        if (supportedRates.Count == 0)
        {
            // An answer, and the answer is none: no rate works for this configuration.
            // Offering the configured rate here would manufacture support that nobody
            // reported, and Apply would go on to accept a rate the devices cannot open.
            // Empty is the honest list, and refusing it belongs to Apply.
            return new SampleRateResolution([], preferredSampleRate, false, null);
        }

        int[] rates = [.. supportedRates];
        bool fellBack = !rates.Contains(preferredSampleRate);
        return new SampleRateResolution(
            rates,
            fellBack ? rates[0] : preferredSampleRate,
            false,
            fellBack ? preferredSampleRate : null);
    }
}
