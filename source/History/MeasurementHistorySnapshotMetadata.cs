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

    public static MeasurementHistorySnapshotMetadata FromResult(MeasurementResult result) =>
        new()
        {
            SampleRate = result.SampleRate,
            Bits = result.Bits,
            LowFrequencyHz = result.LowFrequencyHz,
            HighFrequencyHz = result.HighFrequencyHz,
            SweepDurationSeconds = result.SweepDurationSeconds,
            PlayChannel = result.PlaybackChannel,
            MeasurementMode = result.MeasurementMode,
            TimingReference = result.TimingReference,
            SweepDeconvolutionPeakIndex = result.SweepDeconvolution.PeakIndex,
            TransferPeakIndex = result.Transfer?.PeakIndex,
            AverageRunCount = result.AverageRunCount,
            AcceptedAverageRunCount = result.AcceptedAverageRunCount,
            MeterSnapshot = result.Levels
        };
}
