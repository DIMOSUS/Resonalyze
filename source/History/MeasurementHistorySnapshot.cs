using System.Numerics;

namespace Resonalyze.History;

internal sealed class MeasurementHistorySnapshot
{
    public int SampleRate { get; init; }
    public int Bits { get; init; }
    public int Octaves { get; init; }
    public double SweepDurationSeconds { get; init; }
    public PlaybackChannel PlayChannel { get; init; }
    public SweepMeasurementMode MeasurementMode { get; init; }
    public int SweepDeconvolutionPeakIndex { get; init; }
    public int? TransferPeakIndex { get; init; }
    public required Complex[] SweepDeconvolutionImpulseResponse { get; init; }
    public Complex[]? TransferImpulseResponse { get; init; }
    public required InputLevelMeterSnapshot MeterSnapshot { get; init; }
    public required MeasurementHistoryPreview Preview { get; init; }
    // Settable so the live working state (mode + per-mode settings + active
    // overlays) can be written back into a cached snapshot when navigating away.
    public MeasurementSessionSnapshot? Session { get; set; }

    public ImpulseResponseFile ToImpulseResponseFile()
    {
        return new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            SampleRate = SampleRate,
            Bits = Bits,
            Octaves = Octaves,
            SweepDurationSeconds = SweepDurationSeconds,
            PlayChannel = PlayChannel,
            MeasurementMode = MeasurementMode,
            SweepDeconvolutionPeakIndex = SweepDeconvolutionPeakIndex,
            TransferPeakIndex = TransferPeakIndex,
            SweepDeconvolutionRealSamples = SweepDeconvolutionImpulseResponse.Select(
                sample => sample.Real).ToArray(),
            SweepDeconvolutionImaginarySamples = HasImaginarySamples(SweepDeconvolutionImpulseResponse)
                ? SweepDeconvolutionImpulseResponse.Select(sample => sample.Imaginary).ToArray()
                : null,
            TransferRealSamples = TransferImpulseResponse?.Select(sample => sample.Real).ToArray(),
            TransferImaginarySamples = TransferImpulseResponse is { Length: > 0 } transfer &&
                HasImaginarySamples(transfer)
                ? transfer.Select(sample => sample.Imaginary).ToArray()
                : null,
            MicrophoneLevels = ImpulseResponseFile.CreateLevelSnapshotFileEntry(
                MeterSnapshot.Microphone),
            LoopbackLevels = ImpulseResponseFile.CreateLevelSnapshotFileEntry(
                MeterSnapshot.Loopback),
            PreviewFrequencyResponse = ImpulseResponseFile.CreatePreviewFileEntry(Preview)
        };
    }

    private static bool HasImaginarySamples(IReadOnlyList<Complex> samples)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].Imaginary != 0)
            {
                return true;
            }
        }

        return false;
    }
}
