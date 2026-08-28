using Resonalyze.Audio;

namespace Resonalyze.Options;

/// <summary>
/// Where the inputs an array may be spread over come from, in the words the
/// <see cref="ArrayMicrophonesDialog"/> status line states them in.
/// </summary>
/// <remarks>
/// Apart from the panel that reads the device because it is the one part of that
/// dialog a FIGURE of it has to reproduce exactly: the screenshot tool builds the
/// dialog with no interface attached, and a hint it worded itself would publish a
/// line the application never shows.
/// </remarks>
internal static class ArrayInputSources
{
    /// <param name="channelCount">
    /// How many inputs the backend offers. Read only by WASAPI, where the count is
    /// itself the diagnosis: an interface presenting its inputs as separate stereo
    /// endpoints reports two, and its further inputs are genuinely unreachable in one
    /// session — through ASIO they are not.
    /// </param>
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
