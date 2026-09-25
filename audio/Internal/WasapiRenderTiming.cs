namespace Resonalyze.Audio;

/// <summary>What an exclusive-mode buffer event asks the render loop for.</summary>
internal enum WasapiExclusiveRenderStep
{
    /// <summary>The next buffer of the source.</summary>
    Fill,

    /// <summary>A silent period behind the final buffer, which the device has only just begun to play.</summary>
    Silence,

    /// <summary>The final buffer has played out; the client may stop.</summary>
    Finish
}

internal static class WasapiRenderTiming
{
    /// <summary>An exclusive event-driven stream double-buffers: the event that follows the final buffer means the
    /// device has taken it and started playing, not finished it. Stopping there cut up to one buffer (100 ms) off the
    /// sweep's end, its top octave on a fast sweep; one silent period lets it play out first.</summary>
    public static WasapiExclusiveRenderStep NextExclusiveStep(bool finalBufferQueued, bool silenceQueued) =>
        !finalBufferQueued
            ? WasapiExclusiveRenderStep.Fill
            : !silenceQueued
                ? WasapiExclusiveRenderStep.Silence
                : WasapiExclusiveRenderStep.Finish;

    private const double MissedDeadlineFactor = 1.5;

    public static bool IsUnderrun(
        int currentPaddingFrames,
        bool sourceEnded,
        TimeSpan elapsedSinceFill,
        TimeSpan bufferDuration)
    {
        return !sourceEnded &&
            currentPaddingFrames == 0 &&
            bufferDuration > TimeSpan.Zero &&
            elapsedSinceFill > bufferDuration * MissedDeadlineFactor;
    }
}
