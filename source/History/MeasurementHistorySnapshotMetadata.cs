namespace Resonalyze.History;

internal sealed class MeasurementHistorySnapshotMetadata
{
    public int SampleRate { get; init; }
    public int Bits { get; init; }
    public int Octaves { get; init; }
    public double SweepDurationSeconds { get; init; }
    public PlaybackChannel PlayChannel { get; init; }
    public SweepMeasurementMode MeasurementMode { get; init; }
    public int SweepDeconvolutionPeakIndex { get; init; }
    public int? TransferPeakIndex { get; init; }
    public required InputLevelMeterSnapshot MeterSnapshot { get; init; }

    public static MeasurementHistorySnapshotMetadata FromSnapshot(
        MeasurementHistorySnapshot snapshot)
    {
        return new MeasurementHistorySnapshotMetadata
        {
            SampleRate = snapshot.SampleRate,
            Bits = snapshot.Bits,
            Octaves = snapshot.Octaves,
            SweepDurationSeconds = snapshot.SweepDurationSeconds,
            PlayChannel = snapshot.PlayChannel,
            MeasurementMode = snapshot.MeasurementMode,
            SweepDeconvolutionPeakIndex = snapshot.SweepDeconvolutionPeakIndex,
            TransferPeakIndex = snapshot.TransferPeakIndex,
            MeterSnapshot = snapshot.MeterSnapshot
        };
    }

    public string BuildToolTipText(DateTimeOffset timestamp)
    {
        var lines = new List<string>
        {
            $"Time: {TimestampDisplayHelper.Format(timestamp)}",
            $"Mode: {MeasurementMode}",
            $"Sample rate: {SampleRate} Hz",
            $"Bits: {Bits}",
            $"Octaves: {Octaves}",
            $"Duration: {SweepDurationSeconds:0.###} s",
            $"Channel: {PlayChannel}"
        };

        if (MeasurementMode == SweepMeasurementMode.LoopbackTransfer &&
            TransferPeakIndex.HasValue)
        {
            lines.Add($"Transfer peak index: {TransferPeakIndex.Value}");
        }
        else
        {
            lines.Add($"Sweep peak index: {SweepDeconvolutionPeakIndex}");
        }

        if (MeterSnapshot.Microphone.Available)
        {
            lines.Add(
                $"Mic: peak {MeterSnapshot.Microphone.PeakDbFs:0.0} dBFS, RMS {MeterSnapshot.Microphone.RmsDbFs:0.0} dBFS");
        }
        if (MeterSnapshot.Loopback.Available)
        {
            lines.Add(
                $"Loopback: peak {MeterSnapshot.Loopback.PeakDbFs:0.0} dBFS, RMS {MeterSnapshot.Loopback.RmsDbFs:0.0} dBFS");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
