using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Unambiguous per-run failures only (clipping, dead signal, short capture). See docs/tech/sweep-measurement.md#run-acceptance.</summary>
internal static class SweepRunQualityCheck
{
    /// <summary>~-80 dBFS: unplugged, wrong channel or dead device; never a quiet working signal.</summary>
    public const double SilentPeakThreshold = 1e-4;

    /// <summary>Judges the whole capture (the analyzed range includes pre-roll). Full-scale loopback is normal, not flagged.</summary>
    public static IReadOnlyList<string> Assess(
        float[] microphone,
        float[]? loopback,
        int expectedSweepSamples)
    {
        ArgumentNullException.ThrowIfNull(microphone);

        var issues = new List<string>();
        if (microphone.Length < expectedSweepSamples)
        {
            issues.Add(
                $"the capture is shorter than the sweep " +
                $"({microphone.Length} of {expectedSweepSamples} samples)");
        }

        double microphonePeak = Peak(microphone);
        if (microphonePeak >= RecordedLevelMetering.FullScaleThreshold)
        {
            issues.Add("the microphone signal clipped");
        }
        else if (microphonePeak < SilentPeakThreshold)
        {
            issues.Add("the microphone signal is silent");
        }

        if (loopback != null && Peak(loopback) < SilentPeakThreshold)
        {
            issues.Add("the loopback reference signal is silent");
        }

        return issues;
    }

    /// <summary>Clipping, silence and length for one array mic; a failure rejects the whole run.</summary>
    public static IReadOnlyList<string> AssessArrayMicrophone(
        float[] samples,
        int expectedSweepSamples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var issues = new List<string>();
        if (samples.Length < expectedSweepSamples)
        {
            issues.Add(
                $"the capture is shorter than the sweep " +
                $"({samples.Length} of {expectedSweepSamples} samples)");
        }

        double peak = Peak(samples);
        if (peak >= RecordedLevelMetering.FullScaleThreshold)
        {
            issues.Add("the signal clipped");
        }
        else if (peak < SilentPeakThreshold)
        {
            issues.Add("the signal is silent");
        }

        return issues;
    }

    private static double Peak(float[] samples)
    {
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }
}

internal sealed record SweepRunRejection(
    int Run,
    IReadOnlyList<string> Issues);

/// <summary>Notices published with the result; never a refusal. See docs/tech/sweep-measurement.md#pre-arrival and #arrival-ahead-of-the-loopback.</summary>
internal sealed record SweepResultCaution(double? PreArrivalDb, double? AheadOfLoopbackMs = null)
{
    public string Describe() =>
        string.Join(
            "\r\n\r\n",
            new[]
            {
                AheadOfLoopbackMs is { } aheadMs ? DescribeAheadOfLoopback(aheadMs) : null,
                PreArrivalDb is { } preArrivalDb ? DescribePreArrival(preArrivalDb) : null
            }.OfType<string>());

    private static string DescribeAheadOfLoopback(double aheadMs) =>
        FormattableString.Invariant(
            $"The measurement was saved, but its microphone heard the sweep {aheadMs:0.0} ms BEFORE the loopback did.\r\n\r\n") +
        "Nothing reaches a microphone before the signal that drives it, so the " +
        "loopback ran on another clock or stream: a driver that joins two audio " +
        "devices (ASIO4ALL, FlexASIO, an aggregate device) with the microphone on " +
        "one and the loopback on the other. The offset between them is set anew " +
        "each time the stream starts.\r\n\r\n" +
        "The response's shape is real, so it is filed without absolute time, like " +
        "an imported recording: its arrival is placed at " +
        FormattableString.Invariant($"{ArrivalPlacement.PlacedArrivalSeconds * 1000:0} ms, ") +
        "Virtual DSP and Time Alignment will not take it, and its delay cannot be " +
        "compared with another measurement's. Put the microphone that times the " +
        "measurement and the loopback on one audio device.";

    private static string DescribePreArrival(double preArrivalDb) =>
        FormattableString.Invariant(
            $"The measurement was saved, but it carries unusual energy well before its arrival: the stretch from {TransferIrDiagnostics.PreArrivalStartSeconds * 1000:0} to {TransferIrDiagnostics.PreArrivalEndSeconds * 1000:0} ms AHEAD of the peak reads {preArrivalDb:0.0} dB against the arrival itself, where a clean field record reads -39 dB or less.\r\n\r\n") +
        "Nothing physical arrives before the direct sound, so this is either the " +
        "reference — a loopback that is not a clean copy of the excitation cancels " +
        "itself and divides into a resonance the microphone never heard — or a " +
        "record whose strongest sample is not its direct sound at all, where a " +
        "later reflection outweighs an obstructed arrival. This measurement cannot " +
        "tell which.\r\n\r\n" +
        "Worth checking that the loopback carries the excitation itself: a wire " +
        "from the output, not an interface direct-mixer or monitor path with " +
        "effects, sends or faders in it. The result is still usable away from the " +
        "affected frequencies, but compare it against a channel that measures " +
        "cleanly before you tune on this one.";
}

internal sealed record SweepRunQualityReport(
    int RequestedRuns,
    int AcceptedRuns,
    IReadOnlyList<SweepRunRejection> Rejections)
{
    public bool IsDegraded => AcceptedRuns < RequestedRuns || Rejections.Count > 0;

    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(
            $"The averaged measurement used {AcceptedRuns} of the " +
            $"{RequestedRuns} requested sweep runs:");
        foreach (SweepRunRejection rejection in Rejections)
        {
            text.Append("\r\n");
            text.Append(
                $"Run {rejection.Run}: stopped the measurement " +
                $"({string.Join(", ", rejection.Issues)})");
        }

        return text.ToString();
    }
}
