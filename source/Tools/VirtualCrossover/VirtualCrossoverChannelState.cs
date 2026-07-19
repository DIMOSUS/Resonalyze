using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The resolved source measurement of one SIDE of a channel pair (null while
/// unresolved), plus that side's interactive processed-IR cache. UI-free: the
/// runtime model owns this, so the algorithmic paths never reach a WinForms
/// control to read a channel's measurement state.
/// </summary>
internal sealed class VirtualCrossoverChannelState
{
    private Complex[]? transferImpulseResponse;

    public Complex[]? TransferImpulseResponse
    {
        get => transferImpulseResponse;
        set
        {
            transferImpulseResponse = value;
            ProcessingSource = value == null
                ? null
                : new VirtualCrossoverSourceSnapshot(value);
        }
    }
    public VirtualCrossoverSourceSnapshot? ProcessingSource { get; private set; }
    public int TransferPeakIndex { get; set; }
    public int SampleRate { get; set; }

    // The measurement's per-bin coherence (γ²) on the linear FFT grid, when
    // the source carried it. Only the auto-crossover wizard reads it, to
    // discount frequencies the measurement did not trust when reading each
    // driver's usable band; null when the source had none.
    public double[]? TransferCoherence { get; set; }

    // The channel's harmonic distortion (THD, dB vs the fundamental) computed
    // from the source's sweep deconvolution, when it carried one. Only the
    // auto-crossover wizard reads it, to bound each driver by its
    // distortion-clean band (a tweeter's low handover follows its measured
    // distortion knee); null when the source had no sweep deconvolution.
    public IReadOnlyList<SignalPoint>? DistortionCurve { get; set; }

    // The band-limited envelope arrival and gated band level of this
    // side's PROCESSED response, keyed by the processed array's identity
    // and the measured band — the L/R/Δ read-out re-runs on every redraw,
    // and the Hilbert analysis of a full-length IR is far too heavy to
    // repeat when nothing changed. The level rides in the same cache
    // entry: it is measured over the same band from the same response.
    public (Complex[] ProcessedIr, double LowHz, double HighHz,
        TimeAlignmentAnalysisResult Result, double? LevelDb)?
        ArrivalCache
    { get; set; }

    // Invalidation counter for in-flight asynchronous source loads: a
    // load captures the revision when it starts (BeginSourceLoad, which
    // also invalidates any OLDER in-flight load into this slot, so the
    // user's latest pick wins regardless of completion order) and may
    // write back only while the revision still matches. Clear() bumps it
    // too: a project import or mono toggle mid-load kills the landing
    // instead of hiding a stale measurement in a slot that was wiped.
    public int SourceRevision { get; private set; }

    public int BeginSourceLoad() => ++SourceRevision;

    public void Clear()
    {
        TransferImpulseResponse = null;
        TransferPeakIndex = 0;
        SampleRate = 0;
        TransferCoherence = null;
        DistortionCurve = null;
        ArrivalCache = null;
        SourceRevision++;
    }
}
