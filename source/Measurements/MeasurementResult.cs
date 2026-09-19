using System.Numerics;

namespace Resonalyze;

/// <summary>
/// One sweep result, whichever way it arrived: a run, a recorded-sweep import, a file, a history entry or REW. It never
/// changes once built; plot builds read it off the UI thread, so the open result is replaced whole.
/// </summary>
internal sealed record MeasurementResult
{
    public required int SampleRate { get; init; }
    public required int Bits { get; init; }
    public PlaybackChannel PlaybackChannel { get; init; }

    /// <summary>The band the sweep was asked for; kept so a saved result reads back as the same sweep.</summary>
    public double LowFrequencyHz { get; init; }
    public double HighFrequencyHz { get; init; }

    /// <summary>Band actually swept; harmonic geometry reads it.</summary>
    public double AchievedLowFrequencyHz { get; init; }
    public double AchievedHighFrequencyHz { get; init; }

    /// <summary>Band excited at full amplitude. See docs/tech/sweep-measurement.md#measured-band.</summary>
    public double MeasuredLowFrequencyHz { get; init; }
    public double MeasuredHighFrequencyHz { get; init; }

    /// <summary>As recorded, not rebuilt: sweep generation caps its length, and harmonic offsets scale with it.</summary>
    public double SweepDurationSeconds { get; init; }

    /// <summary>When it was measured, not saved: spatial averages show it as the only evidence two channels came from one sitting.</summary>
    public DateTimeOffset MeasuredAtUtc { get; init; }

    public SweepMeasurementMode MeasurementMode { get; init; }

    /// <summary>Imported recordings have no timing reference; cross-measurement delay comparisons must refuse them.</summary>
    public TimingReference TimingReference { get; init; }

    public required MeasurementImpulseResponse SweepDeconvolution { get; init; }
    public MeasurementImpulseResponse? Transfer { get; init; }
    public double[]? TransferCoherence { get; init; }
    public int AverageRunCount { get; init; } = 1;
    public int AcceptedAverageRunCount { get; init; } = 1;
    public InputLevelMeterSnapshot Levels { get; init; } = InputLevelMeterSnapshot.Empty;

    /// <summary>The SPL anchor frozen with this result, and only one captured on the input that produced it.</summary>
    public SplCalibration? SplCalibration { get; init; }

    /// <summary>The IRs are stored raw, so the mic curve they were taken through travels with them.</summary>
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; init; }

    /// <summary>This result's filter; null is "unknown" (an old file), which differs from Off.</summary>
    public ProtectiveHighPassConfiguration? ProtectiveHighPass { get; init; }

    /// <summary>Measurement mic first; empty for a single mic.</summary>
    public IReadOnlyList<ArrayMicrophoneCurve> ArrayMicrophones { get; init; } = [];

    public ImpulseResponseFile.AudioSessionFileEntry? AudioSession { get; init; }

    public int SweepSampleCount => (int)Math.Round(SweepDurationSeconds * SampleRate);

    /// <summary>The sweep's length in whole samples, which the harmonic geometry and a saved file state.</summary>
    public double SweepSampleDurationSeconds =>
        SampleRate > 0 ? SweepSampleCount / (double)SampleRate : 0.0;

    public bool HasTransfer => Transfer is { ImpulseResponse.Length: > 0 };

    /// <summary><c>K = loopbackPeakDbFs + calibrationOffsetDb</c> turns dBr into dB SPL; null without an anchor or a loopback level.</summary>
    public double? SplOffsetDb =>
        SplCalibration is { } calibration && Levels.Loopback is { Available: true } loopback
            ? loopback.PeakDbFs + calibration.OffsetDb
            : null;

    /// <summary>Band every derived curve stops at.</summary>
    public MeasuredBand MeasuredBand => MeasuredBand.Resolve(
        ProtectiveHighPass,
        MeasuredLowFrequencyHz,
        MeasuredHighFrequencyHz,
        SampleRate);

    /// <summary>High/low ratio actually swept; harmonic packets sit at SweepSamples * ln(h) / ln(ratio).</summary>
    public double AchievedFrequencyRatio =>
        AchievedLowFrequencyHz > 0 && AchievedHighFrequencyHz > AchievedLowFrequencyHz
            ? AchievedHighFrequencyHz / AchievedLowFrequencyHz
            : 0.0;

    public double HarmonicIROffset(double harmonic)
    {
        double ratio = AchievedFrequencyRatio;
        return SweepSampleCount <= 0 || ratio <= 1.0
            ? 0
            : SweepSampleCount * Math.Log(harmonic) / Math.Log(ratio);
    }

    /// <summary>A stored result whose band edges were not written falls back to the band it was always read over.</summary>
    public static (double LowHz, double HighHz) ResolveMeasuredBand(
        double measuredLowHz,
        double measuredHighHz,
        double achievedLowHz,
        double achievedHighHz)
    {
        double low = measuredLowHz > 0 ? measuredLowHz : achievedLowHz;
        double high = measuredHighHz > low ? measuredHighHz : achievedHighHz;
        return (low, high);
    }

    /// <summary>What a stored result must hold to be shown; every input that restores one goes through here.</summary>
    public MeasurementResult Validated()
    {
        Complex[] sweep = SweepDeconvolution.ImpulseResponse;
        if (sweep.Length == 0)
        {
            throw new ArgumentException("Sweep deconvolution impulse response cannot be empty.");
        }

        if ((uint)SweepDeconvolution.PeakIndex >= (uint)sweep.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SweepDeconvolution), "The sweep deconvolution peak lies outside its impulse response.");
        }

        if (Transfer == null && MeasurementMode == SweepMeasurementMode.LoopbackTransfer)
        {
            throw new ArgumentException(
                "Transfer impulse response is required for loopback transfer measurements.");
        }

        if (Transfer is { } transfer)
        {
            if (transfer.ImpulseResponse.Length == 0)
            {
                throw new ArgumentException("Transfer impulse response cannot be empty.");
            }

            if ((uint)transfer.PeakIndex >= (uint)transfer.ImpulseResponse.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Transfer), "The transfer peak lies outside its impulse response.");
            }
        }

        return this;
    }
}
