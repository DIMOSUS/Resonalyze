using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// Renders a history entry's metadata as tooltip text. This is presentation, so
/// it lives on the UI side rather than on
/// <see cref="MeasurementHistorySnapshotMetadata"/> — the metadata record is a
/// persisted schema and has no business formatting strings or reaching for
/// <see cref="ToolTipTextWrapper"/>.
/// </summary>
internal static class MeasurementHistoryToolTip
{
    public static string Build(MeasurementHistorySnapshotMetadata metadata, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string sweepBand =
            metadata.LowFrequencyHz > 0 && metadata.HighFrequencyHz > metadata.LowFrequencyHz
                ? $"Sweep: {metadata.LowFrequencyHz:0.#}–{metadata.HighFrequencyHz:0} Hz " +
                    $"({Math.Log2(metadata.HighFrequencyHz / metadata.LowFrequencyHz):0.0} oct)"
                : "Sweep: —";
        var lines = new List<string>
        {
            $"Time: {TimestampDisplayHelper.Format(timestamp)}",
            $"Mode: {metadata.MeasurementMode}",
            $"Sample rate: {metadata.SampleRate} Hz",
            $"Bits: {metadata.Bits}",
            sweepBand,
            $"Duration: {metadata.SweepDurationSeconds:0.###} s",
            $"Channel: {metadata.PlayChannel}"
        };
        if (metadata.AverageRunCount > 1 || metadata.AcceptedAverageRunCount > 1)
        {
            lines.Add(
                $"Averaging: {metadata.AcceptedAverageRunCount}/{metadata.AverageRunCount} runs");
        }

        if (metadata.MeasurementMode == SweepMeasurementMode.LoopbackTransfer &&
            metadata.TransferPeakIndex.HasValue)
        {
            lines.Add($"Transfer peak index: {metadata.TransferPeakIndex.Value}");
        }
        else
        {
            lines.Add($"Sweep peak index: {metadata.SweepDeconvolutionPeakIndex}");
        }

        if (metadata.MeterSnapshot.Microphone.Available)
        {
            lines.Add(
                $"Mic: peak {metadata.MeterSnapshot.Microphone.PeakDbFs:0.0} dBFS, " +
                $"RMS {metadata.MeterSnapshot.Microphone.RmsDbFs:0.0} dBFS");
        }
        if (metadata.MeterSnapshot.Loopback.Available)
        {
            lines.Add(
                $"Loopback: peak {metadata.MeterSnapshot.Loopback.PeakDbFs:0.0} dBFS, " +
                $"RMS {metadata.MeterSnapshot.Loopback.RmsDbFs:0.0} dBFS");
        }

        // These land on ToolStrip items and grid cells, which do not go through the
        // app's wrapping tooltip, so the wrap happens here instead.
        return ToolTipTextWrapper.Wrap(string.Join(Environment.NewLine, lines));
    }
}
