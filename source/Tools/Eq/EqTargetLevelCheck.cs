using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A target level far from the source makes the fit correct level instead of shape, so the wizard asks first.</summary>
internal static class EqTargetLevelCheck
{
    /// <summary>Median dB above source needing broadband boost (cuts-only cannot reach it at all).</summary>
    public const double BoostWarningDb = 3;

    public const double CutWarningDb = 10;

    /// <summary>Median of target minus source over the window; points with mismatched frequencies are skipped.</summary>
    public static double? TargetAboveSourceDb(
        IReadOnlyList<SignalPoint> source,
        IReadOnlyList<SignalPoint> target,
        double minHz,
        double maxHz)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var differences = new List<double>();
        int count = Math.Min(source.Count, target.Count);
        for (int index = 0; index < count; index++)
        {
            SignalPoint measured = source[index];
            SignalPoint wanted = target[index];
            if (measured.X < minHz || measured.X > maxHz ||
                Math.Abs(measured.X - wanted.X) > measured.X * 1e-6)
            {
                continue;
            }

            double difference = wanted.Y - measured.Y;
            if (double.IsFinite(difference))
            {
                differences.Add(difference);
            }
        }

        if (differences.Count == 0)
        {
            return null;
        }

        // Median: a junction dip or modal null is shape, not level.
        differences.Sort();
        int middle = differences.Count / 2;
        return differences.Count % 2 == 1
            ? differences[middle]
            : (differences[middle - 1] + differences[middle]) / 2;
    }

    /// <param name="bankCannotLift">Boosts Off or refilling: the preamp stops at 0 dB and the bank never lifts the curve.</param>
    public static string? Warning(
        double? targetAboveSourceDb,
        bool bankCannotLift,
        double minHz,
        double maxHz)
    {
        if (targetAboveSourceDb is not { } offset)
        {
            return null;
        }

        string window = $"{minHz:0}–{maxHz:0} Hz";
        if (offset >= BoostWarningDb)
        {
            return bankCannotLift
                ? $"The target sits {offset:0.0} dB above the source over {window} " +
                  "(median). With these Boosts the fit cannot raise the curve: it will leave " +
                  "it below the target, and a bump that stays under the target line " +
                  "is not a cut it will make." + Environment.NewLine +
                  Environment.NewLine +
                  "Lower the Target Level to the curve. Tune anyway?"
                : $"The target sits {offset:0.0} dB above the source over {window} " +
                  "(median). The fit will boost across the whole window and spend " +
                  "headroom on level rather than on shape." + Environment.NewLine +
                  Environment.NewLine +
                  "Lower the Target Level. Tune anyway?";
        }

        if (-offset >= CutWarningDb)
        {
            return
                $"The target sits {-offset:0.0} dB below the source over {window} " +
                "(median). The fit will cut the whole window, and that level has " +
                "to come back from the amplifier gain, with its noise." +
                Environment.NewLine + Environment.NewLine +
                "Raise the Target Level. Tune anyway?";
        }

        return null;
    }
}
