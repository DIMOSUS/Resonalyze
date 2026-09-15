using Resonalyze.Audio;

namespace Resonalyze.Options;

/// <summary>Status wording shared with the screenshot tool, which builds the dialog without an interface.</summary>
internal static class ArrayInputSources
{
    /// <param name="channelCount">WASAPI only: two means the interface exposes stereo endpoints (use ASIO for more).</param>
    public static string Describe(AudioBackend backend, int channelCount)
    {
        if (backend == AudioBackend.Asio)
        {
            return "ASIO driver inputs";
        }

        if (backend.IsWasapi())
        {
            return channelCount > 2
                ? "WASAPI endpoint channels"
                : "WASAPI endpoint channels; use ASIO to reach an interface's further inputs";
        }

        return "MME is limited to two channels";
    }
}
