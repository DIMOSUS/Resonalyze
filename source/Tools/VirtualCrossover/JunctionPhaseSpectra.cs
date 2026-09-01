using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The spectra the junction phase read-out is measured from: every channel
/// through the panel's OWN phase gate, at a frequency-dependent window of
/// <see cref="FdwCycles"/> cycles, re-referenced to one absolute time origin.
/// </summary>
/// <remarks>
/// <para>
/// The same window the phase CURVES are drawn through, deliberately: the φ
/// column and the plot beside it then describe one measurement, and a handover
/// the numbers call inverted is one the eye can find on the curves. The
/// read-out used a 0.68 s steady-state window until 2026-09-01 — see the
/// remarks on <see cref="JunctionPhaseAlignment"/> for what the battery
/// measured when the two were compared.
/// </para>
/// <para>
/// Placement is <see cref="PhaseGatePlacement"/>'s, resolved over the channels
/// handed in and nothing else, so a set the read-out does not cover cannot move
/// its windows. Each spectrum is then rotated to the record's own origin: a
/// per-curve placement is a different time reference per channel, and a
/// junction is read from the CROSS-phase of two of them, which would otherwise
/// carry the placement difference as a delay.
/// </para>
/// </remarks>
internal static class JunctionPhaseSpectra
{
    /// <summary>
    /// The window length, in periods, at every frequency the read-out is taken
    /// at — fixed here rather than following the gate dialog's 4/6/8 selector.
    /// </summary>
    /// <remarks>
    /// The dialog's setting shapes a curve for the EYE, and 4 cycles is a
    /// legitimate choice there. It is not a legitimate choice for a number: on
    /// the archived cabins, 4 cycles moved φ by a median 36° against the
    /// steady-state reference and flipped the recommended polarity on 5
    /// junctions of 20, where 8 cycles moved it by 5° and flipped 4 (all of them
    /// the delay-versus-flip tie the block already marks). 8 is also the longest
    /// window the selector offers, so it is the one that keeps the read closest
    /// to the sustained sum the loss column beside it measures.
    /// </remarks>
    public const int FdwCycles = 8;

    /// <summary>
    /// One spectrum per channel, aligned with <paramref name="channels"/>.
    /// </summary>
    /// <param name="sampleRate">
    /// The rate the PLACEMENT arithmetic works in (offsets are milliseconds, so
    /// it only decides sample rounding). Each spectrum is transformed at its own
    /// channel's rate.
    /// </param>
    /// <param name="pinnedOffsetMs">
    /// The gate's absolute offset when the user pinned one — then it is one
    /// shared window for every channel. Null (Auto) gives each channel its own
    /// estimated arrival, which is what lets the short high-frequency windows
    /// land on the right channel's first cycles.
    /// </param>
    public static List<Complex[]> Build(
        IReadOnlyList<ProcessedChannel> channels,
        int sampleRate,
        double? pinnedOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs)
    {
        ArgumentNullException.ThrowIfNull(channels);
        IReadOnlyList<PlacementChannel> placement = PlacementChannel.From(channels);
        double sharedOffsetMs = PhaseGatePlacement.ResolveSharedOffsetMs(
            placement, sampleRate, pinnedOffsetMs);
        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            placement, sharedOffsetMs, sampleRate, pinnedOffsetMs,
            leftMs, plateauMs, rightMs);
        var template = new PhaseAnalysisSettings(
            PhaseWindowMode.FrequencyDependent,
            FdwCycles,
            // A detrend is a display convenience — it flattens a curve for the
            // eye by rotating it against one τ. The cross-phase of two channels
            // must not be detrended: a shared τ cancels out of it anyway, and
            // anything per-channel would BE the answer the junction is read for.
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            leftMs,
            plateauMs,
            rightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        var spectra = new List<Complex[]>(channels.Count);
        for (int i = 0; i < channels.Count; i++)
        {
            ProcessedChannel channel = channels[i];
            // Peak index 0: gate offsets are absolute times from the start of
            // the record, the same origin GatedPhaseCurves reads the drawn
            // curves on.
            Complex[] gated = DataHelper.GetPhaseAnalysisSpectrum(
                new ImpulseMeasurementView(
                    channel.ImpulseResponse, 0, channel.SampleRate),
                template with { GateOffsetMs = offsets[i] },
                out int extractionStart);
            // A one-spectrum sum IS the re-reference: the rotation to the
            // record's origin, without copying the caller's array.
            spectra.Add(DataHelper.SumGatedSpectra(
                [(gated, extractionStart)], targetExtractionStart: 0));
        }

        return spectra;
    }
}
