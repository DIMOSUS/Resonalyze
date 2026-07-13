using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze.Audio;

public sealed record DuplexFormatSupport(
    int SampleRate,
    int BitsPerSample,
    int CaptureChannels,
    int RenderChannels,
    bool CaptureSupported,
    bool RenderSupported)
{
    public bool Supported => CaptureSupported && RenderSupported;
}

/// <summary>
/// WASAPI Exclusive format probing. <see cref="CheckExclusive"/> is the only
/// NAudio-free (bool-returning) surface exposed to the settings UI;
/// <see cref="CreateDeviceFormat"/> stays internal because it returns a NAudio
/// <see cref="WaveFormat"/> that must not cross the audio boundary.
/// </summary>
public static class WasapiFormatSupport
{
    public static DuplexFormatSupport CheckExclusive(
        string captureEndpointId,
        string renderEndpointId,
        int sampleRate,
        int bitsPerSample,
        int captureChannels,
        int renderChannels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureEndpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(renderEndpointId);
        WaveFormat captureFormat = CreateDeviceFormat(sampleRate, bitsPerSample, captureChannels);
        WaveFormat renderFormat = CreateDeviceFormat(sampleRate, bitsPerSample, renderChannels);
        using var enumerator = new MMDeviceEnumerator();
        MMDevice capture = enumerator.GetDevice(captureEndpointId);
        MMDevice render = enumerator.GetDevice(renderEndpointId);
        bool captureSupported = capture.AudioClient.IsFormatSupported(
            AudioClientShareMode.Exclusive,
            captureFormat);
        bool renderSupported = render.AudioClient.IsFormatSupported(
            AudioClientShareMode.Exclusive,
            renderFormat);
        return new DuplexFormatSupport(
            sampleRate,
            bitsPerSample,
            captureChannels,
            renderChannels,
            captureSupported,
            renderSupported);
    }

    internal static WaveFormat CreateDeviceFormat(int sampleRate, int bitsPerSample, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (bitsPerSample is not (16 or 24 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        int channelMask = channels >= 31 ? 0 : (1 << channels) - 1;
        return new WaveFormatExtensible(
            sampleRate,
            bitsPerSample,
            channels,
            channelMask);
    }
}
