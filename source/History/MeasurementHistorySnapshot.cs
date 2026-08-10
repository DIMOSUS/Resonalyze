using System.Numerics;

namespace Resonalyze.History;

internal sealed class MeasurementHistorySnapshot
{
    public int SampleRate { get; init; }
    public int Bits { get; init; }
    // Legacy: only set when restoring a pre-band file; the band is stored
    // explicitly below. Use ResolveSweepBand() rather than reading these.
    public int Octaves { get; init; }
    public double LowFrequencyHz { get; init; }
    public double HighFrequencyHz { get; init; }
    // The band actually swept, as opposed to the requested one above. Harmonic
    // geometry reads this; see ImpulseResponseFile.ResolveAchievedSweepBand.
    public double AchievedLowFrequencyHz { get; init; }
    public double AchievedHighFrequencyHz { get; init; }
    public double SweepDurationSeconds { get; init; }

    /// <summary>The band that was requested.</summary>
    public (double LowHz, double HighHz) ResolveSweepBand() =>
        ImpulseResponseFile.ResolveSweepBand(
            LowFrequencyHz, HighFrequencyHz, Octaves, SampleRate);

    /// <summary>The band the sweep actually swept.</summary>
    public (double LowHz, double HighHz) ResolveAchievedSweepBand() =>
        ImpulseResponseFile.ResolveAchievedSweepBand(
            AchievedLowFrequencyHz,
            AchievedHighFrequencyHz,
            LowFrequencyHz,
            HighFrequencyHz,
            Octaves,
            SampleRate,
            SweepDurationSeconds);
    public PlaybackChannel PlayChannel { get; init; }
    public SweepMeasurementMode MeasurementMode { get; init; }

    public TimingReference TimingReference { get; init; }
    public int SweepDeconvolutionPeakIndex { get; init; }
    public int? TransferPeakIndex { get; init; }
    public int AverageRunCount { get; init; } = 1;
    public int AcceptedAverageRunCount { get; init; } = 1;
    public ImpulseResponseFile.AudioSessionFileEntry? AudioSession { get; init; }
    public required Complex[] SweepDeconvolutionImpulseResponse { get; init; }
    public Complex[]? TransferImpulseResponse { get; init; }
    public double[]? TransferCoherence { get; init; }
    public required InputLevelMeterSnapshot MeterSnapshot { get; init; }
    /// <summary>
    /// The SPL anchor frozen onto the result this snapshot holds, validated against
    /// its own input when it was captured (see <c>ImpulseResponseFile.Capture</c>).
    /// Together with <see cref="MeterSnapshot"/>'s loopback level it is the whole
    /// recipe for dB SPL, so restoring the entry — or comparing against it — keeps
    /// the absolute axis the original measurement had. Null when there was none.
    /// </summary>
    public SplCalibration? SplCalibration { get; init; }
    public required MeasurementHistoryPreview Preview { get; init; }

    /// <summary>
    /// The offset K that turns this snapshot's loopback-referenced magnitude (dBr)
    /// into dB SPL: <c>K = loopbackPeakDbFs + calibrationOffsetDb</c>. Null without
    /// an anchor or a captured loopback level. The anchor was matched against its
    /// own input when stored, so it is trusted here as a loaded file's is.
    /// </summary>
    public double? SplOffsetDb =>
        SplCalibration is { } calibration && MeterSnapshot.Loopback is { Available: true } loopback
            ? loopback.PeakDbFs + calibration.OffsetDb
            : null;
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
            LowFrequencyHz = LowFrequencyHz,
            HighFrequencyHz = HighFrequencyHz,
            AchievedLowFrequencyHz = AchievedLowFrequencyHz,
            AchievedHighFrequencyHz = AchievedHighFrequencyHz,
            Octaves = Octaves,
            SweepDurationSeconds = SweepDurationSeconds,
            PlayChannel = PlayChannel,
            MeasurementMode = MeasurementMode,
            TimingReference = TimingReference,
            SweepDeconvolutionPeakIndex = SweepDeconvolutionPeakIndex,
            TransferPeakIndex = TransferPeakIndex,
            AverageRunCount = AverageRunCount,
            AcceptedAverageRunCount = AcceptedAverageRunCount,
            AudioSession = AudioSession,
            SplCalibration = SplCalibration,
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
            TransferCoherence = TransferCoherence?.ToArray(),
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
