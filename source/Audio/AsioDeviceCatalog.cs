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

    /// <summary>
    /// Index of the named driver; 0 (the first driver) when no name is saved
    /// yet; -1 when the saved driver is not in the list. A missing driver must
    /// stay visible as its own entry instead of remapping to another driver,
    /// or the next apply silently re-targets the persisted configuration.
    /// </summary>
    public static int FindDriverIndex(
        IReadOnlyList<AsioDeviceInfo> drivers,
        string? driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return drivers.Count == 0 ? -1 : 0;
        }

        for (int i = 0; i < drivers.Count; i++)
        {
            if (string.Equals(
                drivers[i].DriverName,
                driverName,
                StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
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

    public static IReadOnlyList<int> GetSupportedSampleRates(
        string? driverName,
        int minimumSampleRate = 44_100)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return Array.Empty<int>();
        }

        try
        {
            using var driver = new AsioOut(driverName);
            return SampleRateCatalog.GetCandidateRates(minimumSampleRate)
                .Where(driver.IsSampleRateSupported)
                .ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// Index of the channel with the given offset, or -1 when the driver does
    /// not report it (fewer channels, or the driver failed to open). Callers
    /// keep the offset visible as a "(missing)" entry so it survives an apply.
    /// </summary>
    public static int FindChannelIndex(
        IReadOnlyList<AsioChannelInfo> channels,
        int offset)
    {
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].Offset == offset)
            {
                return i;
            }
        }

        return -1;
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
