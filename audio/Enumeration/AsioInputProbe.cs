namespace Resonalyze.Audio;

public sealed record AsioInputProbeChannelResult(
    int Offset,
    string Name,
    double PeakDbFs,
    double RmsDbFs,
    double CorrelationToFirst);

public static class AsioInputProbe
{
    public static async Task<IReadOnlyList<AsioInputProbeChannelResult>> CaptureAsync(
        string driverName,
        int sampleRate,
        int outputChannelOffset,
        int milliseconds,
        CancellationToken cancellationToken)
    {
        AsioDriverInfo driverInfo = AsioDeviceCatalog.GetDriverInfo(
            driverName,
            sampleRate);
        if (!string.IsNullOrWhiteSpace(driverInfo.ErrorMessage))
        {
            throw new InvalidOperationException(driverInfo.ErrorMessage);
        }
        if (!driverInfo.SupportsSampleRate)
        {
            throw new InvalidOperationException(
                $"ASIO driver '{driverName}' does not support {sampleRate} Hz.");
        }
        if (driverInfo.InputChannels.Count == 0)
        {
            throw new InvalidOperationException(
                $"ASIO driver '{driverName}' has no input channels.");
        }

        int silenceSamples = Math.Max(64, sampleRate / 10);
        using FloatArrayWaveStream silence = FloatArrayWaveStream.FromMonoSamples(
            new float[silenceSamples],
            sampleRate,
            PlaybackChannel.Stereo);
        using var session = new AsioFullDuplexSession(
            driverName,
            inputChannelOffset: 0,
            outputChannelOffset,
            inputChannelCount: driverInfo.InputChannels.Count);
        await session.StartAsync(
            new LoopingWaveProvider(silence),
            sampleRate,
            autoStop: false,
            cancellationToken,
            expectedTotalSamples: (int)((long)sampleRate * milliseconds / 1000) + sampleRate)
            .ConfigureAwait(false);
        try
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.StopAsync().ConfigureAwait(false);
        }

        float[][] samples = session.CompleteCaptureSnapshot();
        return driverInfo.InputChannels
            .Select((channel, index) =>
            {
                float[] data = (uint)index < (uint)samples.Length
                    ? samples[index]
                    : Array.Empty<float>();
                return new AsioInputProbeChannelResult(
                    channel.Offset,
                    channel.Name,
                    ToDecibels(GetPeak(data)),
                    ToDecibels(GetRms(data)),
                    index == 0
                        ? 1.0
                        : samples.Length > 0
                            ? GetCorrelation(samples[0], data)
                            : 0.0);
            })
            .ToArray();
    }

    private static double GetPeak(IReadOnlyList<float> samples)
    {
        double peak = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }

    private static double GetRms(IReadOnlyList<float> samples)
    {
        if (samples.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double sample = samples[i];
            sum += sample * sample;
        }

        return Math.Sqrt(sum / samples.Count);
    }

    private static double GetCorrelation(
        IReadOnlyList<float> first,
        IReadOnlyList<float> second)
    {
        int count = Math.Min(first.Count, second.Count);
        if (count == 0)
        {
            return 0;
        }

        double dot = 0;
        double firstPower = 0;
        double secondPower = 0;
        for (int i = 0; i < count; i++)
        {
            double a = first[i];
            double b = second[i];
            dot += a * b;
            firstPower += a * a;
            secondPower += b * b;
        }

        double denominator = Math.Sqrt(firstPower * secondPower);
        return denominator <= 1e-12 ? 0 : dot / denominator;
    }

    private static double ToDecibels(double amplitude)
    {
        return amplitude <= 1e-12
            ? -240
            : 20.0 * Math.Log10(amplitude);
    }
}
