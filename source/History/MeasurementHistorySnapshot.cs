using System.Numerics;

namespace Resonalyze.History;

internal sealed class MeasurementHistorySnapshot
{
    /// <summary>Not persisted (a thousand levels per mic); a restored history entry has no array and the hybrid says so.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ArrayMicrophoneCurve> ArrayMicrophones { get; init; } = [];

    /// <summary>Accompanies the array: channels compensated for different filters are not one set. Not persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ProtectiveHighPassConfiguration? ProtectiveHighPass { get; init; }

    /// <summary>IRs are stored raw, so the calibration must travel for a recipient to draw the same response.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; init; }

    public int SampleRate { get; init; }
    public int Bits { get; init; }
    // Legacy, pre-band files only; use ResolveSweepBand().
    public int Octaves { get; init; }
    public double LowFrequencyHz { get; init; }
    public double HighFrequencyHz { get; init; }
    public double AchievedLowFrequencyHz { get; init; }
    public double AchievedHighFrequencyHz { get; init; }
    // Full-amplitude band: narrower than achieved by the fade guard bands.
    public double MeasuredLowFrequencyHz { get; init; }
    public double MeasuredHighFrequencyHz { get; init; }

    /// <summary>File stamp when loaded, capture time when run: the only evidence that array channels came from one sitting.</summary>
    public DateTimeOffset MeasuredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public double SweepDurationSeconds { get; init; }

    public (double LowHz, double HighHz) ResolveSweepBand() =>
        ImpulseResponseFile.ResolveSweepBand(
            LowFrequencyHz, HighFrequencyHz, Octaves, SampleRate);

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
    public SplCalibration? SplCalibration { get; init; }
    public required MeasurementHistoryPreview Preview { get; init; }

    /// <summary>K = loopbackPeakDbFs + calibrationOffsetDb, turning dBr into dB SPL; null without anchor or loopback level.</summary>
    public double? SplOffsetDb =>
        SplCalibration is { } calibration && MeterSnapshot.Loopback is { Available: true } loopback
            ? loopback.PeakDbFs + calibration.OffsetDb
            : null;
    // Settable so live working state can be written back when navigating away.
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
            MeasuredLowFrequencyHz = MeasuredLowFrequencyHz,
            MeasuredHighFrequencyHz = MeasuredHighFrequencyHz,
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
            PreviewFrequencyResponse = ImpulseResponseFile.CreatePreviewFileEntry(Preview),
            // Without these, a history-opened measurement would differ from the same file opened off disk.
            ArrayMicrophones = ImpulseResponseFile.ArrayMicrophonesFileEntry.From(
                ArrayMicrophones),
            ProtectiveHighPass = ProtectiveHighPass is { } filter
                ? ImpulseResponseFile.ProtectiveHighPassFileEntry.From(filter)
                : null,
            MicrophoneCalibration = MicrophoneCalibration
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
