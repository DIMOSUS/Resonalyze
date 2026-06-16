using NAudio.Wave;

namespace Resonalyze;

public sealed record AudioDeviceInfo(int DeviceNumber, string Name)
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
            devices.Add(new AudioDeviceInfo(i, capabilities.ProductName));
        }

        return devices;
    }

    public static IReadOnlyList<AudioDeviceInfo> GetRecordingDevices()
    {
        List<AudioDeviceInfo> devices = [new AudioDeviceInfo(-1, "Default recording device")];
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            WaveInCapabilities capabilities = WaveIn.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo(i, capabilities.ProductName));
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
}
