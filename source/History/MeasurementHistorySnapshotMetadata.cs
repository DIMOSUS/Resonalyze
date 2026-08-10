namespace Resonalyze.History;

internal sealed class MeasurementHistorySnapshotMetadata
{
    public int SampleRate { get; init; }
    public int Bits { get; init; }
    public double LowFrequencyHz { get; init; }
    public double HighFrequencyHz { get; init; }
    public double SweepDurationSeconds { get; init; }
    public PlaybackChannel PlayChannel { get; init; }
    public SweepMeasurementMode MeasurementMode { get; init; }

    public TimingReference TimingReference { get; init; }
    public int SweepDeconvolutionPeakIndex { get; init; }
    public int? TransferPeakIndex { get; init; }
    public int AverageRunCount { get; init; } = 1;
    public int AcceptedAverageRunCount { get; init; } = 1;
    public required InputLevelMeterSnapshot MeterSnapshot { get; init; }

    public static MeasurementHistorySnapshotMetadata FromSnapshot(
        MeasurementHistorySnapshot snapshot)
    {
        (double lowHz, double highHz) = snapshot.ResolveSweepBand();
        return new MeasurementHistorySnapshotMetadata
        {
            SampleRate = snapshot.SampleRate,
            Bits = snapshot.Bits,
            LowFrequencyHz = lowHz,
            HighFrequencyHz = highHz,
            SweepDurationSeconds = snapshot.SweepDurationSeconds,
            PlayChannel = snapshot.PlayChannel,
            MeasurementMode = snapshot.MeasurementMode,
            TimingReference = snapshot.TimingReference,
            SweepDeconvolutionPeakIndex = snapshot.SweepDeconvolutionPeakIndex,
            TransferPeakIndex = snapshot.TransferPeakIndex,
            AverageRunCount = snapshot.AverageRunCount,
            AcceptedAverageRunCount = snapshot.AcceptedAverageRunCount,
            MeterSnapshot = snapshot.MeterSnapshot
        };
    }
}
