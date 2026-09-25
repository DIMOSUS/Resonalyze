using NAudio.Wave;

namespace Resonalyze.Audio;

public static class AsioDeviceCatalog
{
    public static readonly AsioDriverInfo EmptyDriverInfo = new(
        string.Empty,
        Array.Empty<AsioChannelInfo>(),
        Array.Empty<AsioChannelInfo>(),
        0,
        0,
        false,
        Array.Empty<int>(),
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

    /// <summary>0 when no name is saved; -1 when the saved driver is missing, which must not remap to another driver on apply.</summary>
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
        int sampleRate,
        int minimumSampleRate = 44_100)
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

            (bool supportsSampleRate, int[] supportedSampleRates) = ProbeSampleRates(
                driver.IsSampleRateSupported,
                sampleRate,
                SampleRateCatalog.GetCandidateRates(minimumSampleRate));
            return new AsioDriverInfo(
                driverName,
                inputChannels,
                outputChannels,
                SafeInt(() => driver.FramesPerBuffer),
                SafeInt(() => driver.PlaybackLatency),
                supportsSampleRate,
                supportedSampleRates,
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

    /// <summary>-1 when the driver does not report the offset; callers keep it as a "(missing)" entry.</summary>
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

    /// <remarks>Reserve API: no caller in the solution today (see AGENTS.md).</remarks>
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

    /// <summary>
    /// ASIO refuses a rate with ASE_NoClock, but some drivers answer another code, which NAudio throws: that rate is
    /// unsupported. A driver that takes no rate and threw is failing, not refusing, so its first error is rethrown.
    /// </summary>
    internal static (bool SupportsSampleRate, int[] SupportedSampleRates) ProbeSampleRates(
        Func<int, bool> isSupported,
        int sampleRate,
        IEnumerable<int> candidateRates)
    {
        Exception? firstRefusal = null;
        bool Probe(int rate)
        {
            try
            {
                return isSupported(rate);
            }
            catch (Exception exception)
            {
                firstRefusal ??= exception;
                return false;
            }
        }

        bool supportsSampleRate = sampleRate > 0 && Probe(sampleRate);
        int[] supportedSampleRates = candidateRates.Where(Probe).ToArray();
        if (!supportsSampleRate && supportedSampleRates.Length == 0 && firstRefusal != null)
        {
            throw firstRefusal;
        }

        return (supportsSampleRate, supportedSampleRates);
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
