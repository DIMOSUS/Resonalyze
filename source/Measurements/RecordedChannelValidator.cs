namespace Resonalyze;

internal static class RecordedChannelValidator
{
    public static void EnsureDifferentSignals(
        IReadOnlyList<float[]> channels,
        int firstChannel,
        int secondChannel,
        string context)
    {
        if ((uint)firstChannel >= (uint)channels.Count ||
            (uint)secondChannel >= (uint)channels.Count)
        {
            return;
        }

        float[] first = channels[firstChannel];
        float[] second = channels[secondChannel];
        int count = Math.Min(first.Length, second.Length);
        if (count == 0)
        {
            return;
        }

        double firstPower = 0;
        double secondPower = 0;
        double differencePower = 0;
        for (int i = 0; i < count; i++)
        {
            double a = first[i];
            double b = second[i];
            firstPower += a * a;
            secondPower += b * b;
            double difference = a - b;
            differencePower += difference * difference;
        }

        double signalPower = Math.Max(firstPower, secondPower);
        if (signalPower <= 1e-12)
        {
            return;
        }

        double relativeDifference = Math.Sqrt(differencePower / signalPower);
        if (relativeDifference < 1e-5)
        {
            throw new InvalidOperationException(
                $"{context}: selected input channels contain the same duplicated mono signal. " +
                "Select a true stereo input pair or use ASIO channels that expose microphone and loopback separately.");
        }
    }
}
