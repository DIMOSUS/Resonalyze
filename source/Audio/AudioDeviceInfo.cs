using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Resonalyze;

public sealed record AudioDeviceInfo(int DeviceNumber, string Name, int Channels = 0)
{
    public override string ToString() => Name;
}

public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices()
    {
        List<AudioDeviceInfo> devices = [new AudioDeviceInfo(-1, "Default playback device")];
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            WaveOutCapabilities capabilities = WaveOut.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo(
                i,
                capabilities.ProductName,
                capabilities.Channels));
        }

        return devices;
    }

    public static IReadOnlyList<AudioDeviceInfo> GetRecordingDevices()
    {
        List<AudioDeviceInfo> devices = [new AudioDeviceInfo(-1, "Default recording device")];
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            WaveInCapabilities capabilities = WaveIn.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo(
                i,
                capabilities.ProductName,
                capabilities.Channels));
        }

        return devices;
    }

    public static int FindDeviceIndex(
        IReadOnlyList<AudioDeviceInfo> devices,
        int deviceNumber)
    {
        int index = devices
            .Select((device, i) => new { device, i })
            .FirstOrDefault(candidate =>
                candidate.device.DeviceNumber == deviceNumber)
            ?.i ?? 0;
        return index;
    }

    public static IReadOnlyList<int> GetSupportedWaveSampleRates(
        int playbackDeviceNumber,
        int recordingDeviceNumber,
        int playbackChannelCount,
        int recordingChannelCount,
        int bitsPerSample,
        int minimumSampleRate = 44_100)
    {
        return SampleRateCatalog.GetCandidateRates(minimumSampleRate)
            .Where(sampleRate =>
                SupportsWaveOutFormat(playbackDeviceNumber, sampleRate, bitsPerSample, playbackChannelCount) &&
                SupportsWaveInFormat(recordingDeviceNumber, sampleRate, bitsPerSample, recordingChannelCount))
            .ToArray();
    }

    private static bool SupportsWaveOutFormat(
        int deviceNumber,
        int sampleRate,
        int bitsPerSample,
        int channelCount)
    {
        var format = CreateWaveFormat(sampleRate, bitsPerSample, channelCount);
        int result = waveOutOpen(
            out _,
            unchecked((uint)deviceNumber),
            ref format,
            IntPtr.Zero,
            IntPtr.Zero,
            WaveFormatQueryFlag);
        return result == NoError;
    }

    private static bool SupportsWaveInFormat(
        int deviceNumber,
        int sampleRate,
        int bitsPerSample,
        int channelCount)
    {
        var format = CreateWaveFormat(sampleRate, bitsPerSample, channelCount);
        int result = waveInOpen(
            out _,
            unchecked((uint)deviceNumber),
            ref format,
            IntPtr.Zero,
            IntPtr.Zero,
            WaveFormatQueryFlag);
        return result == NoError;
    }

    private static WaveFormatEx CreateWaveFormat(
        int sampleRate,
        int bitsPerSample,
        int channelCount)
    {
        ushort blockAlign = checked((ushort)(channelCount * (bitsPerSample / 8)));
        return new WaveFormatEx
        {
            FormatTag = 1,
            Channels = checked((ushort)channelCount),
            SamplesPerSecond = checked((uint)sampleRate),
            AverageBytesPerSecond = checked((uint)(sampleRate * blockAlign)),
            BlockAlign = blockAlign,
            BitsPerSample = checked((ushort)bitsPerSample),
            ExtraSize = 0
        };
    }

    private const int NoError = 0;
    private const uint WaveFormatQueryFlag = 0x0001;

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static extern int waveOutOpen(
        out IntPtr waveOutHandle,
        uint deviceId,
        ref WaveFormatEx waveFormat,
        IntPtr callback,
        IntPtr callbackInstance,
        uint flags);

    [DllImport("winmm.dll", EntryPoint = "waveInOpen")]
    private static extern int waveInOpen(
        out IntPtr waveInHandle,
        uint deviceId,
        ref WaveFormatEx waveFormat,
        IntPtr callback,
        IntPtr callbackInstance,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }
}
