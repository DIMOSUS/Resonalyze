using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where the target level datum sits against the curve about to be fitted. A
/// target far above the source makes the fit boost across the whole window and
/// spend headroom on level, not on shape; a target far below makes it cut the
/// whole window and hand the level to the amplifier gain, with its noise. Both
/// are a datum set wrong, and the fit will do them faithfully — so the wizard
/// asks first. UI-free so the reading is the same wherever a fit starts.
/// </summary>
internal static class EqTargetLevelCheck
{
    /// <summary>
    /// A target this far above the source (dB, median over the window) needs
    /// broadband boost. A fit that may boost spends headroom to get there; a
    /// Cuts-only fit cannot get there at all — its preamp is capped at 0 dB and
    /// its bands only cut — so the curve stays below the target, and a bump
    /// that stays under the target line is not a cut the fit will make.
    /// </summary>
    public const double BoostWarningDb = 3;

    /// <summary>
    /// A target this far below the source (dB, median over the window) is a
    /// broadband cut, in either mode.
    /// </summary>
    public const double CutWarningDb = 10;

    /// <summary>
    /// The median of target minus source over the window: positive = the target
    /// sits above the source. Null when the window holds no comparable point.
    /// The two curves are expected on one frequency grid, as the wizard builds
    /// its target on the source's own frequencies; a point whose frequencies
    /// disagree is skipped rather than compared.
    /// </summary>
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

        // The median: a junction dip or a modal null in the window is a feature
        // the fit will look at on its own, not a level, and must not move the datum.
        differences.Sort();
        int middle = differences.Count / 2;
        return differences.Count % 2 == 1
            ? differences[middle]
            : (differences[middle - 1] + differences[middle]) / 2;
    }

    /// <summary>
    /// The question to put to the user before the fit, or null when the datum
    /// is close enough that the fit is about shape.
    /// </summary>
    public static string? Warning(
        double? targetAboveSourceDb,
        bool cutsOnly,
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
            return cutsOnly
                ? $"The target sits {offset:0.0} dB above the source over {window} " +
                  "(median). Cuts only cannot raise the curve: the fit will leave " +
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
