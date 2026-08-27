using System.Numerics;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// Owns the Compare measurement selection. Written on the UI thread (choose
/// file / history entry / clear); read by <c>Task.Run</c> plot builds through
/// <see cref="GetAnalysisSource"/>, so the reference is volatile and every
/// reader snapshots it once — the selection itself is immutable.
/// </summary>
internal sealed class CompareSelection
{
    private volatile CompareMeasurementSelection? current;

    /// <summary>Raised after Set/Clear, on the caller's (UI) thread.</summary>
    public event Action? Changed;

    public CompareMeasurementSelection? Current => current;

    public void Set(
        string displayName,
        string? sourceFilePath,
        MeasurementHistorySnapshot snapshot)
    {
        current = new CompareMeasurementSelection(displayName, sourceFilePath, snapshot);
        Changed?.Invoke();
    }

    public void Clear()
    {
        current = null;
        Changed?.Invoke();
    }

    // Exposes the Compare measurement's transfer IR for the mode plots (Frequency
    // Response / Phase / Group Delay and the gated IR preview). Loopback is mandatory
    // for every measurement, so all Compare analysis runs on the transfer IR — the
    // same source the main curves are built from.
    public CompareAnalysisSource? GetAnalysisSource()
    {
        if (current is not { } selection ||
            selection.Snapshot.TransferImpulseResponse is not { Length: > 0 } transferIr)
        {
            return null;
        }

        return new CompareAnalysisSource(
            selection.DisplayName,
            selection.Snapshot.SampleRate,
            transferIr,
            selection.Snapshot.TransferPeakIndex ?? 0,
            selection.Snapshot.TransferCoherence,
            // Its OWN K, not the main measurement's: the two were captured at
            // different loopback levels unless they share a session.
            selection.Snapshot.SplOffsetDb,
            selection.Snapshot.TimingReference,
            MeasuredBand.Resolve(
                selection.Snapshot.ProtectiveHighPass,
                selection.Snapshot.AchievedLowFrequencyHz,
                selection.Snapshot.AchievedHighFrequencyHz,
                selection.Snapshot.SampleRate));
    }

    // Time Alignment compares one arrival against another, so a measurement
    // imported from a recorded sweep cannot take part: its arrival is set by when
    // the recorder was started. The curve comparison above is unaffected — a
    // magnitude response does not care what time it arrived at.
    public TimeAlignmentCompareMeasurement? GetTimeAlignmentMeasurement() =>
        current is not { } selection ||
            selection.Snapshot.TimingReference == TimingReference.RecordedSweep
            ? null
            : new TimeAlignmentCompareMeasurement(
                selection.DisplayName,
                selection.Snapshot);
}

internal sealed record CompareMeasurementSelection(
    string DisplayName,
    string? SourceFilePath,
    MeasurementHistorySnapshot Snapshot);

// The Compare measurement's transfer IR used by the mode plots (Frequency Response
// magnitude, Phase / Group Delay and the gated IR preview). Matching sample rate is
// validated by the consumers.
//
// SplOffsetDb is this measurement's own K (loopback level + calibration offset), so
// the Compare magnitude can be drawn on the absolute axis beside the main curve;
// null when the compared measurement carries no SPL anchor, and then Frequency
// Response omits it in dB SPL rather than drawing dBr on an absolute scale.
public readonly record struct CompareAnalysisSource(
    string DisplayName,
    int SampleRate,
    Complex[] TransferImpulseResponse,
    int TransferPeakIndex,
    double[]? TransferCoherence = null,
    double? SplOffsetDb = null,
    TimingReference TimingReference = TimingReference.SynchronizedLoopback,
    // What IT measured — its own protective high-pass and its own sweep band,
    // neither of which need be the main measurement's.
    MeasuredBand Band = default);
