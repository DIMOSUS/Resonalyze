using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Preferred (persisted IR calibration) vs effective (per loaded source) calibration, testable without WinForms.
/// Persisting the effective one would let loading an RTA overlay erase the saved IR calibration.
/// </summary>
internal static class EqWizardCalibration
{
    /// <summary>Only a choice made with an IR (or nothing) loaded updates the IR preference.</summary>
    public static string? UpdatedIrPreference(
        string? current,
        EqWizardSourceKind? loadedKind,
        EqWizardCalibrationChoice chosen)
    {
        bool appliesToImpulseResponses =
            loadedKind is null or EqWizardSourceKind.ImpulseResponse;
        return appliesToImpulseResponses ? chosen.MicrophoneCalibrationId : current;
    }

    /// <summary>The choice a newly loaded source opens with. See docs/tech/eq-auto-tuner.md#calibration-choice.</summary>
    public static EqWizardCalibrationChoice Choose(
        EqWizardCurveSource source,
        string? preferredIrCalibrationId)
    {
        if (source.HasOwnCalibration)
        {
            return EqWizardCalibrationChoice.OwnCapture;
        }
        if (source.Kind == EqWizardSourceKind.ImpulseResponse)
        {
            return EqWizardCalibrationChoice.Microphone(preferredIrCalibrationId);
        }
        // A handoff is pinned to the correction its panel renders with; the IR preference stays untouched.
        if (source.Kind == EqWizardSourceKind.VirtualDspChannel)
        {
            // Pinned whenever the panel pinned ANY correction, curve or mode (the curve alone misses the average's own correction).
            return source.PinsCorrection
                ? EqWizardCalibrationChoice.PinnedToSource
                : EqWizardCalibrationChoice.Off;
        }

        return EqWizardCalibrationChoice.Off;
    }

    /// <summary>
    /// What the selector offers for a source. Entries resolving to nothing, and a selection the list lost, stay listed:
    /// dropping them would rewrite the user's choice.
    /// </summary>
    public static IReadOnlyList<EqWizardCalibrationOption> Options(
        EqWizardCurveSource? source,
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        EqWizardCalibrationChoice current)
    {
        var options = new List<EqWizardCalibrationOption>
        {
            new(EqWizardCalibrationChoice.Off, "Off")
        };

        if (source is { HasOwnCalibration: true })
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationChoice.OwnCapture, "Own (as captured)"));
        }

        // Listed under the panel's name for it (may be a session curve absent from the wizard's list).
        if (source is { Kind: EqWizardSourceKind.VirtualDspChannel, PinsCorrection: true } pinned)
        {
            options.Add(new EqWizardCalibrationOption(
                EqWizardCalibrationChoice.PinnedToSource,
                pinned.PinnedCalibrationName ??
                    (pinned.SpatialAverageCalibration.Mode == SpatialAverageCalibrationMode.Own
                        ? "Own (as measured)"
                        : "Virtual DSP")));
        }

        // An aggregate (multi-mic) correction offers only Own and Off: one mic's file would apply to positions not read through it.
        if (source is not { CalibrationIsAggregate: true })
        {
            foreach (MicrophoneCalibrationEntry entry in entries)
            {
                options.Add(new EqWizardCalibrationOption(
                    EqWizardCalibrationChoice.Microphone(entry.Id),
                    entry.Available ? entry.Name : $"{entry.Name} (unavailable)"));
            }
        }

        if (!current.Own &&
            !current.IsOff &&
            !entries.Any(entry => string.Equals(
                entry.Id,
                current.CalibrationId,
                StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new EqWizardCalibrationOption(
                current,
                "Deleted calibration (missing)"));
        }

        return options;
    }
}

internal sealed record EqWizardCalibrationOption(
    EqWizardCalibrationChoice Choice,
    string Label)
{
    public override string ToString() => Label;
}

internal readonly record struct EqWizardCalibrationChoice
{
    private EqWizardCalibrationChoice(bool own, bool pinned, string? calibrationId)
    {
        Own = own;
        Pinned = pinned;
        CalibrationId = calibrationId;
    }

    public static EqWizardCalibrationChoice Off => default;

    public static EqWizardCalibrationChoice OwnCapture => new(true, false, null);

    public static EqWizardCalibrationChoice PinnedToSource => new(false, true, null);

    public static EqWizardCalibrationChoice Microphone(string? calibrationId) =>
        new(false, false, MicrophoneCalibrationIds.Normalize(calibrationId));

    public bool Own { get; }

    public bool Pinned { get; }

    /// <summary>Configured calibration id; null for Off, Own and Pinned.</summary>
    public string? CalibrationId { get; }

    public string? MicrophoneCalibrationId => Own || Pinned ? null : CalibrationId;

    public bool IsOff => !Own && !Pinned && MicrophoneCalibrationIds.IsOff(CalibrationId);
}
