namespace Resonalyze.Options;

/// <param name="Rates">null = keep the list on screen; empty = no rate works (Apply refuses); otherwise offer these.</param>
/// <param name="ProbeFailed">The driver gave no answer at all, which is not "unsupported"; change nothing on it.</param>
internal readonly record struct SampleRateResolution(
    int[]? Rates,
    int Selected,
    bool ProbeFailed,
    int? FellBackFrom);

/// <remarks>Some ASIO drivers refuse a second open moments later and report an empty rate list; treating that
/// as an answer replaced the user's rate with a fallback that the next Apply persisted.</remarks>
internal static class SampleRateOptions
{
    public const int FallbackSampleRate = 44_100;

    /// <remarks>Only a named ASIO driver can fall silent; other backends' empty list is a real answer.</remarks>
    public static bool IsProbeFailure(
        bool isAsioBackend,
        string? asioDriverName,
        int reportedRateCount) =>
        isAsioBackend &&
        !string.IsNullOrWhiteSpace(asioDriverName) &&
        reportedRateCount == 0;

    /// <remarks>Wave/WASAPI Exclusive only: an empty combo makes GetSelectedSampleRate fall back to 44.1 kHz, which
    /// Apply would persist. Not for ASIO, where empty can be silence (see <see cref="IsProbeFailure"/>).</remarks>
    public static void ValidateSelectedRate(
        IReadOnlyList<int> supportedRates,
        int sampleRate,
        string deviceDescription)
    {
        ArgumentNullException.ThrowIfNull(supportedRates);

        if (supportedRates.Count == 0)
        {
            // Only levers the panel offers; the bit depth control is disabled.
            throw new InvalidOperationException(
                $"{deviceDescription} report no sample rate in common for the current " +
                "configuration. Change the devices, the playback channel, or the " +
                "microphone and loopback channels.");
        }

        if (!supportedRates.Contains(sampleRate))
        {
            throw new InvalidOperationException(
                $"{deviceDescription} do not support {sampleRate} Hz for the current configuration.");
        }
    }

    /// <summary>-1 on an empty list: selecting entry 0 there throws mid-rebuild and leaves the window unresponsive.</summary>
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

    public static SampleRateResolution Resolve(
        IReadOnlyList<int> supportedRates,
        int preferredSampleRate,
        bool hasExistingList,
        bool probeFailed)
    {
        ArgumentNullException.ThrowIfNull(supportedRates);

        if (probeFailed)
        {
            // Silence: the list on screen stands; with none, offer the configured rate alone, still flagged failed.
            if (hasExistingList)
            {
                return new SampleRateResolution(null, preferredSampleRate, true, null);
            }

            int synthesized = preferredSampleRate > 0 ? preferredSampleRate : FallbackSampleRate;
            return new SampleRateResolution([synthesized], synthesized, true, null);
        }

        if (supportedRates.Count == 0)
        {
            // Answer is none: offering the configured rate would manufacture support. Apply refuses empty.
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
