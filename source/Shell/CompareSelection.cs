using System.Numerics;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// Owns the Compare measurement selection that used to live as a raw field on
/// <c>Form1</c>. Written on the UI thread (choose file / history entry /
/// clear); read by <c>Task.Run</c> plot builds through
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

    // Exposes the Compare measurement's impulse responses for the mode plots: the
    // transfer IR (Phase / Group Delay / Impulse) and the sweep-deconvolution IR
    // (Frequency Response). Each consumer checks the response it needs, so a Compare
    // without a transfer IR still contributes a Frequency Response curve.
    public CompareAnalysisSource? GetAnalysisSource()
    {
        if (current is not { } selection ||
            selection.Snapshot.SweepDeconvolutionImpulseResponse is not { Length: > 0 } sweepIr)
        {
            return null;
        }

        return new CompareAnalysisSource(
            selection.DisplayName,
            selection.Snapshot.SampleRate,
            selection.Snapshot.TransferImpulseResponse ?? Array.Empty<Complex>(),
            selection.Snapshot.TransferPeakIndex ?? 0,
            sweepIr,
            selection.Snapshot.SweepDeconvolutionPeakIndex,
            selection.Snapshot.TransferCoherence);
    }

    public TimeAlignmentCompareMeasurement? GetTimeAlignmentMeasurement() =>
        current is not { } selection
            ? null
            : new TimeAlignmentCompareMeasurement(
                selection.DisplayName,
                selection.Snapshot);
}

internal sealed record CompareMeasurementSelection(
    string DisplayName,
    string? SourceFilePath,
    MeasurementHistorySnapshot Snapshot);

// The Compare measurement's impulse responses used by the mode plots: the transfer IR
// (Phase / Group Delay / Impulse and the gated IR preview) and the sweep-deconvolution
// IR (Frequency Response magnitude). Matching sample rate is validated by the consumers.
public readonly record struct CompareAnalysisSource(
    string DisplayName,
    int SampleRate,
    Complex[] TransferImpulseResponse,
    int TransferPeakIndex,
    Complex[] SweepDeconvolutionImpulseResponse,
    int SweepDeconvolutionPeakIndex,
    double[]? TransferCoherence = null);
