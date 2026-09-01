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
/// The gate the user placed, at the window the MEASUREMENT needs: its offset
/// (or the arrival it follows unpinned) and its Tukey durations are the
/// dialog's, its window mode and cycle count are not. The read-out used a
/// 0.68 s steady-state window until 2026-09-01 — see the remarks on
/// <see cref="JunctionPhaseAlignment"/> for what the battery measured when the
/// two were compared.
/// </para>
/// <para>
/// Where that leaves the drawn curves. With the phase view in its default
/// frequency-dependent mode the two are one window and the φ column can be read
/// off the plot beside it. In FIXED mode they are not: the curves keep the
/// dialog's fixed duration while the numbers stay on the 8-cycle window, so at
/// a high junction the plot shows a far longer window than the figures do. That
/// is deliberate and it is the same judgement as the cycle count below — the
/// selector answers "what should this curve look like", and the answer that
/// suits an eye is measurably the wrong instrument for a number: on the
/// archived cabins a fixed window's CEILING above 1 kHz (the best score any
/// delay reaches over the band) is 0.693 against the gated window's 0.928, so
/// honouring the selector would hand a fixed-mode project a read-out that
/// cannot score a correctly tuned tweeter junction above about 0.7.
/// </para>
/// <para>
/// Placement is <see cref="PhaseGatePlacement"/>'s, resolved over the channels
/// handed in and nothing else, so a set the read-out does not cover cannot move
/// its windows. That set is the SUMMING channels, which in a grouped view is
/// not quite the drawn set — a centre is drawn beside the front stage and sums
/// with neither — so when such a spectator fails the leading-edge guard the
/// curves fall back to one shared window while these spectra keep their
/// per-curve placements. The read-out's own channels all passed that guard; a
/// channel that takes part in none of the junctions being reported has no claim
/// on the windows they are read through.
/// </para>
/// <para>
/// Each spectrum is then rotated to the record's own origin: a per-curve
/// placement is a different time reference per channel, and a junction is read
/// from the CROSS-phase of two of them, which would otherwise carry the
/// placement difference as a delay.
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
