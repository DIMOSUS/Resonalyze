using NAudio.Wave;

namespace Resonalyze;

public static class AsioDeviceCatalog
{
    public static readonly AsioDriverInfo EmptyDriverInfo = new(
        string.Empty,
        Array.Empty<AsioChannelInfo>(),
        Array.Empty<AsioChannelInfo>(),
        0,
        0,
        false,
        null);

    public static IReadOnlyList<AsioDeviceInfo> GetDrivers()
    {
        try
        {
            if (!AsioOut.isSupported())
            {
                return Array.Empty<AsioDeviceInfo>();
            }

            return AsioOut.GetDriverNames()
                .Select(name => new AsioDeviceInfo(name))
                .ToArray();
        }
        catch
        {
            return Array.Empty<AsioDeviceInfo>();
        }
    }

    public static int FindDriverIndex(
        IReadOnlyList<AsioDeviceInfo> drivers,
        string? driverName)
    {
        if (drivers.Count == 0)
        {
            return -1;
        }
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return 0;
        }

        int index = drivers
            .Select((driver, position) => new { driver, position })
            .FirstOrDefault(item =>
                string.Equals(
                    item.driver.DriverName,
                    driverName,
                    StringComparison.OrdinalIgnoreCase))
            ?.position ?? -1;
        return index >= 0 ? index : 0;
    }

    public static AsioDriverInfo GetDriverInfo(
        string? driverName,
        int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return EmptyDriverInfo with
            {
                ErrorMessage = "ASIO driver is not selected."
            };
        }

        try
        {
            using var driver = new AsioOut(driverName);
            var inputChannels = Enumerable
                .Range(0, driver.DriverInputChannelCount)
                .Select(index => new AsioChannelInfo(
                    index,
                    SafeChannelName(() => driver.AsioInputChannelName(index))))
                .ToArray();
            var outputChannels = Enumerable
                .Range(0, Math.Max(0, driver.DriverOutputChannelCount - 1))
                .Select(index => new AsioChannelInfo(
                    index,
                    SafeChannelName(() =>
                    {
                        string left = driver.AsioOutputChannelName(index);
                        string right = driver.AsioOutputChannelName(index + 1);
                        return $"{left} / {right}";
                    })))
                .ToArray();

            bool supportsSampleRate = sampleRate > 0 && driver.IsSampleRateSupported(sampleRate);
            return new AsioDriverInfo(
                driverName,
                inputChannels,
                outputChannels,
                SafeInt(() => driver.FramesPerBuffer),
                SafeInt(() => driver.PlaybackLatency),
                supportsSampleRate,
                null);
        }
        catch (Exception exception)
        {
            return EmptyDriverInfo with
            {
                DriverName = driverName,
                ErrorMessage = exception.Message
            };
        }
    }

    public static int FindChannelIndex(
        IReadOnlyList<AsioChannelInfo> channels,
        int offset)
    {
        if (channels.Count == 0)
        {
            return -1;
        }

        int index = channels
            .Select((channel, position) => new { channel, position })
            .FirstOrDefault(item => item.channel.Offset == offset)
            ?.position ?? -1;
        return index >= 0 ? index : 0;
    }

    public static bool IsLoopbackChannel(AsioChannelInfo channel)
    {
        return channel.Name.Contains(
                "loopback",
                StringComparison.OrdinalIgnoreCase) ||
            channel.Name.Contains(
                "loop back",
                StringComparison.OrdinalIgnoreCase);
    }

    public static void ShowControlPanel(string driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new InvalidOperationException("ASIO driver is not selected.");
        }

        using var driver = new AsioOut(driverName);
        driver.ShowControlPanel();
    }

    private static int SafeInt(Func<int> getValue)
    {
        try
        {
            return getValue();
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeChannelName(Func<string> getName)
    {
        try
        {
            string name = getName();
            return string.IsNullOrWhiteSpace(name)
                ? "Unnamed channel"
                : name;
        }
        catch
        {
            return "Unnamed channel";
        }
    }
}
