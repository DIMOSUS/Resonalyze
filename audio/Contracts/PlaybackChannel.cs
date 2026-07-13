namespace Resonalyze.Audio;

/// <summary>
/// The routing of a mono excitation signal onto the (up to two) output
/// channels. A neutral audio-layer concept: the same value drives PCM and
/// float playback-stream construction inside the backends.
/// </summary>
public enum PlaybackChannel : byte
{
    Mono = 0,
    Left = 1,
    Right = 2,
    Stereo = 3
}
