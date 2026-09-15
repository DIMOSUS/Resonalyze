using System.Numerics;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>Written on the UI thread, read by plot-build workers: volatile reference to an immutable selection; snapshot once.</summary>
internal sealed class CompareSelection
{
    private volatile CompareMeasurementSelection? current;

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
            // Its own K: loopback levels differ unless both share a session.
            selection.Snapshot.SplOffsetDb,
            selection.Snapshot.TimingReference,
            MeasuredBand.Resolve(
                selection.Snapshot.ProtectiveHighPass,
                selection.Snapshot.MeasuredLowFrequencyHz,
                selection.Snapshot.MeasuredHighFrequencyHz,
                selection.Snapshot.SampleRate));
    }

    // Recorded-sweep imports cannot join Time Alignment: their arrival depends on when the recorder started.
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

// Sample rate match is validated by consumers. SplOffsetDb null means no anchor: FR omits it in dB SPL.
public readonly record struct CompareAnalysisSource(
    string DisplayName,
    int SampleRate,
    Complex[] TransferImpulseResponse,
    int TransferPeakIndex,
    double[]? TransferCoherence = null,
    double? SplOffsetDb = null,
    TimingReference TimingReference = TimingReference.SynchronizedLoopback,
    MeasuredBand Band = default);
