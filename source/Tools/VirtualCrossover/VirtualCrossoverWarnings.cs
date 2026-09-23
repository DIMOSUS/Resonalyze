using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

internal enum VirtualCrossoverWarningLevel
{
    /// <summary>The view cannot be read as it stands; not a tuning error.</summary>
    Caution,

    Information,

    /// <summary>The tune itself has a problem.</summary>
    Fault
}

internal sealed record VirtualCrossoverWarning(
    string Text,
    string Detail,
    VirtualCrossoverWarningLevel Level);

/// <summary>The one warning line under the plot. Only one shows, in this order: a misplaced gate first (a late window turns
/// every driver into its tail), then what makes the hybrid or the calibration read differently than it looks, then the
/// tune's own crossover spread. See docs/tech/virtual-dsp-panel.md#warnings.</summary>
internal sealed class VirtualCrossoverWarnings(VirtualCrossoverSession session)
{
    private const double MovingMicSpreadWarningDb = 3.0;

    /// <summary>Arrays share the IRs' loopback, so each should sit on its IR.</summary>
    private const double ArrayDatumWarningDb = 1.5;

    // A steep/narrow LF band-pass arrives so late that Auto delay pushes every driver out by this much.
    private const double CrossoverGroupDelayWarningMs = 15.0;

    public VirtualCrossoverWarning? Judge(
        IReadOnlyList<ProcessedChannel> processed,
        HybridMagnitudes? hybrid,
        GatePlacementVerdict? gatePlacement)
    {
        if (gatePlacement is { CutsChannels: true } verdict)
        {
            return new(
                verdict.FormatWarning(),
                verdict.FormatDetail(),
                VirtualCrossoverWarningLevel.Caution);
        }

        // Only while the hybrid is drawn.
        if (hybrid != null && DescribeHybridDisagreement(hybrid) is { } disagreement)
        {
            return new(
                disagreement,
                FormatHybridSpreadDetail(hybrid, processed),
                VirtualCrossoverWarningLevel.Caution);
        }

        // Warn, not refuse: the loopback holds levels, but "the average" means something different per channel.
        if (hybrid != null && DescribeArrayCompositionMismatch() is { } mismatch)
        {
            return new(
                "⚠ The channels were not averaged over the same array.",
                mismatch,
                VirtualCrossoverWarningLevel.Caution);
        }

        if (DescribeUnappliedCalibration(processed) is { } unapplied)
        {
            return new(
                "⚠ The selected calibration does not reach every curve.",
                unapplied,
                VirtualCrossoverWarningLevel.Caution);
        }

        if (DescribeOwnCalibrationMismatch(processed) is { } corrections)
        {
            return new(
                "⚠ The channels were not measured through one calibration.",
                corrections,
                VirtualCrossoverWarningLevel.Caution);
        }

        // Info: the hybrid exists to keep point-measurement dips away from an EQ.
        if (hybrid is { PointMeasuredCount: > 0 } fallbacks)
        {
            return new(
                fallbacks.PointMeasuredCount == 1
                    ? "1 channel is drawn from its point measurement."
                    : $"{fallbacks.PointMeasuredCount} channels are drawn from their " +
                        "point measurements.",
                FormatPointMeasuredDetail(fallbacks, processed),
                VirtualCrossoverWarningLevel.Information);
        }

        return CrossoverSpread(processed);
    }

    /// <summary>Null while the set holds; an array is judged absolutely. See docs/tech/virtual-dsp-panel.md#hybrid-spread-thresholds.</summary>
    internal string? DescribeHybridDisagreement(HybridMagnitudes hybrid)
    {
        ArgumentNullException.ThrowIfNull(hybrid);
        if (session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray)
        {
            return hybrid.WorstDatumDb is { } worst && Math.Abs(worst) > ArrayDatumWarningDb
                ? $"⚠ A microphone array sits {worst:+0.0;-0.0} dB off its impulse " +
                    "response — check the captures."
                : null;
        }

        return hybrid.SpreadDb > MovingMicSpreadWarningDb
            ? $"⚠ The spatial averages disagree by {hybrid.SpreadDb:0.0} dB — " +
                "check the captures."
            : null;
    }

    private static string FormatPointMeasuredDetail(
        HybridMagnitudes hybrid,
        IReadOnlyList<ProcessedChannel> processed)
    {
        var names = new List<string>();
        for (int i = 0; i < processed.Count && i < hybrid.PointMeasuredChannels.Count; i++)
        {
            if (hybrid.PointMeasuredChannels[i])
            {
                names.Add(processed[i].Channel.Name);
            }
        }

        return $"Drawn from one microphone: {string.Join(", ", names)}." +
            "\r\n\r\nThe rest of the set is drawn from its microphone arrays. A " +
            "channel measured at one point carries dips that belong to that spot " +
            "rather than to the listening volume, and an equalizer fitted to them " +
            "is fitted to a place nobody's head occupies. Below the cabin's first " +
            "mode the two measurements agree, so a subwoofer loses little by it.";
    }

    /// <summary>Null when a named calibration reaches every capture on the plot, or none was named.</summary>
    /// <remarks>A capture with mixed per-position calibrations keeps its own aggregate correction; the user chose a microphone that part of the plot is not reading through.</remarks>
    internal string? DescribeUnappliedCalibration(IReadOnlyList<ProcessedChannel> processed)
    {
        if (session.Calibration.Own || session.Calibration.Selected == null)
        {
            return null;
        }

        var aggregates = new List<string>();
        foreach (ProcessedChannel item in processed)
        {
            LiveCaptureDocument? capture = item.Channel
                .SideState(session.ActiveSideRight)
                .SpatialAverageFor(session.SpatialAverageMode);
            if (capture is { CalibrationIsAggregate: true })
            {
                aggregates.Add($"{item.Channel.Name} {item.Channel.Settings.DisplayName}");
            }
        }

        if (aggregates.Count == 0)
        {
            return null;
        }

        return
            $"{string.Join(", ", aggregates)} " +
            (aggregates.Count == 1 ? "was" : "were") +
            " averaged over positions carrying DIFFERENT calibration files, so the " +
            "correction stored with the average belongs to no single microphone and " +
            "there is nothing " +
            $"\"{session.Calibration.SelectedName ?? "the selected calibration"}\" could be " +
            "swapped for. Those curves keep their own corrections — each position " +
            "through the file it was measured with, which is the closest thing to the " +
            "truth there is. Everything else on the plot is read through your " +
            "selection.\r\n\r\nSelect \"Own (as measured)\" to read the whole plot " +
            "the way each measurement was taken, and the note goes away.";
    }

    /// <summary>Under Own, null when all channels share a microphone. Each channel carries its correction into the sum
    /// (<see cref="MagnitudeGateSnapshot.MeasuredSum"/>), so this is information, not a defect.</summary>
    internal string? DescribeOwnCalibrationMismatch(IReadOnlyList<ProcessedChannel> processed)
    {
        if (!session.Calibration.Own || processed.Count < 2)
        {
            return null;
        }

        CalibrationFile? first = processed[0].MicrophoneCalibration;
        var differing = new List<string>();
        foreach (ProcessedChannel channel in processed)
        {
            if (!CalibrationFile.SameCurve(channel.MicrophoneCalibration, first))
            {
                differing.Add(channel.Channel.Name);
            }
        }

        if (differing.Count == 0)
        {
            return null;
        }

        return
            $"{processed[0].Channel.Name} was measured through " +
            $"{Describe(processed[0].MicrophoneCalibration)}, and " +
            $"{string.Join(", ", differing)} through something else. Each channel is " +
            "drawn through its own correction, which is what \"Own (as measured)\" " +
            "means, and the sum carries each channel's correction with it — so the " +
            "sum and the summation loss are honest. What they are not is one " +
            "instrument: the curves are being compared across microphones, and a " +
            "difference between two channels holds the difference between their " +
            "capsules as well. Pick one calibration above to read the whole plot " +
            "through a single microphone, at the cost of reading every channel " +
            "through one that did not measure it.";

        static string Describe(CalibrationFile? calibration) =>
            calibration is { HasData: true } ? "a calibration" : "no calibration";
    }

    internal string? DescribeArrayCompositionMismatch()
    {
        if (session.SpatialAverageMode != VirtualCrossoverSpatialAverageMode.MicArray)
        {
            return null;
        }

        // Every array in the project, both sides and muted included: composition is a property of the measurements.
        // Cross-side (7 vs 5 positions) is the case nothing else catches. A mono pair would be compared with itself.
        var arrays = new List<(string Name, LiveCaptureDocument Document, bool Drawn)>();
        foreach ((VirtualCrossoverChannel channel, bool rightSide) in session.Sides())
        {
            if (channel.SideState(rightSide).ArrayCapture is { } document)
            {
                arrays.Add((
                    channel.Pair.Mono
                        ? channel.Name
                        : $"{channel.Name} {(rightSide ? "R" : "L")}",
                    document,
                    channel.Pair.Enabled));
            }
        }

        if (arrays.Count < 2)
        {
            return null;
        }

        bool countsDiffer = arrays.Any(entry =>
            entry.Document.Recipe.MicrophoneCount !=
            arrays[0].Document.Recipe.MicrophoneCount);
        bool calibrationsDiffer = arrays.Any(entry =>
            !SameArrayCorrection(entry.Document, arrays[0].Document));
        if (!countsDiffer && !calibrationsDiffer)
        {
            return null;
        }

        var lines = new StringBuilder();
        lines.Append(
            "A spatial average describes the volume its microphones stood in, so " +
            "captures averaged over different arrays are answering slightly " +
            "different questions:\r\n\r\n");
        foreach ((string name, LiveCaptureDocument document, bool drawn) in arrays)
        {
            string calibration = document.Calibration?.Name
                ?? (document.CalibrationIsAggregate
                    ? "several calibrations, one per position"
                    : "no calibration");
            lines.Append(
                $"    {name}    {document.Recipe.MicrophoneCount} microphone(s), " +
                $"{calibration}, measured {document.SavedAtUtc.ToLocalTime():g}" +
                $"{(drawn ? string.Empty : "  (muted)")}\r\n");
        }

        lines.Append(
            "\r\nThe hybrid still draws: each average is honest about its own " +
            "driver, and their levels are held by the loopback rather than by the " +
            "arrays matching. Re-measure only if the odd capture's array sampled a " +
            "different volume from the rest — and read an L/R comparison carefully " +
            "when the two SIDES are what differ, because then the sides are not being " +
            "asked the same question.\r\n\r\nThe dates are there because " +
            "nothing records WHERE the microphones stood, and nothing can derive " +
            "it: a rig lifted and set down somewhere else between two channels " +
            "leaves every stored property identical. Captures from one sitting " +
            "are one volume; captures from different days may not be.");
        return lines.ToString();
    }

    /// <summary>Aggregates (mixed per-position calibrations) name no curve, so they are compared band by band.</summary>
    private static bool SameArrayCorrection(
        LiveCaptureDocument first,
        LiveCaptureDocument second)
    {
        if (first.CalibrationIsAggregate != second.CalibrationIsAggregate)
        {
            return false;
        }
        if (!first.CalibrationIsAggregate)
        {
            return SameCalibration(first.Calibration, second.Calibration);
        }

        double[]? a = first.CalibrationCorrectionDb;
        double[]? b = second.CalibrationCorrectionDb;
        if (a == null || b == null)
        {
            return a == b;
        }
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int band = 0; band < a.Length; band++)
        {
            // 0.01 dB: closer is one correction written twice.
            if (Math.Abs(a[band] - b[band]) > 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameCalibration(
        VirtualCrossoverCalibrationSettings? first,
        VirtualCrossoverCalibrationSettings? second)
    {
        if (first == null || second == null)
        {
            return first == null && second == null;
        }

        return CalibrationFile.SameCurve(
            first.ToCalibrationFile(),
            second.ToCalibrationFile());
    }

    private string FormatHybridSpreadDetail(
        HybridMagnitudes hybrid, IReadOnlyList<ProcessedChannel> processed)
    {
        bool arrays = session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray;
        var lines = new StringBuilder();
        lines.Append(
            arrays
                ? "Every array is referenced to the same loopback its impulse " +
                    "response is, so each should sit on it, within about a dB. " +
                    "These stand off by:\r\n\r\n"
                : "Every capture in one set is taken with one analyzer recipe at one " +
                    "input gain, so each channel should sit the same distance from " +
                    "its impulse response. These do not:\r\n\r\n");
        // The whole set, muted included: the spread was measured over it, and a mute must not hide the outlier.
        if (hybrid.SetDatumsDb.Count > 0)
        {
            var drawn = processed.Select(item => item.Channel).ToHashSet();
            foreach (SetDatum entry in hybrid.SetDatumsDb)
            {
                string figure = entry.DatumDb is { } datum
                    ? $"{datum:+0.0;-0.0} dB"
                    : "no overlap to compare";
                string muted = drawn.Contains(entry.Channel) ? string.Empty : "  (muted)";
                lines.Append(
                    $"    {entry.Channel.Name} {entry.Channel.Settings.DisplayName}" +
                    $"    {figure}{muted}\r\n");
            }
        }
        else
        {
            // Positional, nulls included: packing once shifted figures onto the wrong driver's name.
            for (int i = 0; i < hybrid.ChannelOffsetsDb.Count && i < processed.Count; i++)
            {
                VirtualCrossoverChannel channel = processed[i].Channel;
                string figure = hybrid.ChannelOffsetsDb[i] is { } offset
                    ? $"{offset:+0.0;-0.0} dB"
                    : "no overlap to compare";
                lines.Append(
                    $"    {channel.Name} {channel.Settings.DisplayName}    {figure}\r\n");
            }
        }

        lines.Append(
            arrays
                ? "\r\nAn array standing off its impulse response usually read a " +
                    "different input, a different calibration, or a driver that was " +
                    "not the one being measured. The hybrid still draws each array at " +
                    "the level it measured."
                : "\r\nUsually one capture was taken with a different input gain, a " +
                    "different frame length or window (which moves the noise-slope " +
                    "compensation), or belongs to another session. The hybrid still " +
                    "draws: one offset serves the whole set, so a channel that " +
                    "disagrees is drawn at the level it claims.");
        return lines.ToString();
    }

    // Reads the applied delays, not a GD proxy (a narrow LF band-pass peaks late in its own band). Bypassed excluded.
    private static VirtualCrossoverWarning? CrossoverSpread(IReadOnlyList<ProcessedChannel> processed)
    {
        if (CrossoverSpreadWarning([.. processed.Select(item => item.Channel)])
            is not (string name, double spread, IReadOnlyList<VirtualCrossoverZone> placed))
        {
            return null;
        }

        return new(
            $"⚠ {name} lags the others by ~{spread:0} ms — check its crossover.",
            $"{name} arrives ~{spread:0} ms after the other drivers, so Auto delay pushes " +
            "them out by that much to match it.\r\n\r\n" +
            "This is usually excessive crossover group delay — a narrow or steep low-frequency " +
            "band-pass. Reduce its slope or widen its band to bring the alignment delays down." +
            // Names the groups the spread left out, only those the project has.
            ExcludedGroupsNote(placed),
            VirtualCrossoverWarningLevel.Fault);
    }

    internal static string ExcludedGroupsNote(IReadOnlyList<VirtualCrossoverZone> placed)
    {
        bool rear = placed.Contains(VirtualCrossoverZone.Rear);
        bool centre = placed.Contains(VirtualCrossoverZone.Center);
        return (rear, centre) switch
        {
            (true, true) =>
                "\r\n\r\nThe rear fill and the centre are not counted. They are placed " +
                    "against the front stage rather than tuned with it, and the rear sits " +
                    "its fill offset behind by design.",
            (true, false) =>
                "\r\n\r\nThe rear fill is not counted. It is placed against the front stage " +
                    "rather than tuned with it, and sits its fill offset behind by design.",
            (false, true) =>
                "\r\n\r\nThe centre is not counted. It is placed against the front stage " +
                    "rather than tuned with it.",
            _ => string.Empty
        };
    }

    internal static (string Name, double SpreadMs, IReadOnlyList<VirtualCrossoverZone> Placed)?
        CrossoverSpreadWarning(IReadOnlyList<VirtualCrossoverChannel> channels)
    {
        // Front chain only: later stages are PLACED and drag nothing (a 15 ms rear fill would trip the warning itself).
        // See docs/tech/virtual-dsp-panel.md#staged-auto-delay.
        (List<VirtualCrossoverChannel> active, List<VirtualCrossoverChannel> placed) =
            VirtualCrossoverAlignmentStages.Split([.. channels.Where(channel => !channel.Pair.Bypass)]);
        if (active.Count < 2)
        {
            return null;
        }

        // The latest driver holds the smallest delay.
        VirtualCrossoverChannel latest = active.MinBy(channel => channel.Settings.DelayMs)!;
        double earliestDelay = active.Max(channel => channel.Settings.DelayMs);
        double spread = earliestDelay - latest.Settings.DelayMs;
        return spread > CrossoverGroupDelayWarningMs
            ? (latest.Name, spread, [.. placed.Select(channel => channel.Pair.Zone).Distinct()])
            : null;
    }
}
