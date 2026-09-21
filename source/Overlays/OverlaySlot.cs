using OxyPlot;

namespace Resonalyze;

/// <summary>One overlay slot as the session holds it: its content and what its controls show. Written by <see cref="OverlaySession"/>.</summary>
internal sealed class OverlaySlot
{
    public OverlaySlot(int index, OverlaySlotState empty)
    {
        Index = index;
        Empty = empty;
        State = empty;
    }

    public int Index { get; }

    /// <summary>What a cleared slot holds: its default colour and offset.</summary>
    public OverlaySlotState Empty { get; }

    public OverlaySlotState State { get; set; }

    public Mode SeriesMode => State.Mode;

    public string Title => State.Title;

    public OverlayKind Kind => State.Kind;

    /// <summary>Shown on the plot, or armed to show once a live source appears.</summary>
    public bool Checked { get; set; }

    public bool CheckEnabled { get; set; }

    public bool OffsetEnabled { get; set; }

    /// <summary>A settings dialog is previewing; periodic redraws (live target refresh) must not stomp it.</summary>
    public bool PreviewActive { get; set; }

    public bool SavePending { get; set; }

    /// <summary>A captured slot's curve as drawn (smoothed, offset); what operations and targets read as its source.</summary>
    public DataPoint[]? DrawPoints { get; set; }

    /// <summary>What a dialog for <paramref name="kind"/> opens with: the slot's own look, or the new-overlay look.</summary>
    public OverlayDialogSeed DialogSeed(OverlayKind kind) => State.Kind == kind
        ? new OverlayDialogSeed(State.Title, State.Appearance.Color, State.Appearance.LineStyle)
        : new OverlayDialogSeed(
            kind == OverlayKind.Target ? $"Target {Index}" : $"Calculated overlay {Index}",
            Empty.Appearance.Color,
            OverlayLineStyle.Dash);

    public OverlayOperationSettings OperationSeed =>
        State.Operation ?? OverlayOperationSettings.Default with { UseAmplitudeSpace = true };

    public OverlayTargetSettings TargetSeed => State.Target ?? new OverlayTargetSettings(
        0,
        OverlayTargets.DefaultPreset,
        TargetCurveSpec.FromPreset(OverlayTargets.DefaultPreset),
        3,
        TargetDeviationMode.Deviation);

    // Caches shape and tolerance so the ~30 fps live redraw skips the grid math.
    public TargetOverlayCurveBuilder TargetBuilder { get; } = new();
}

internal sealed record OverlayDialogSeed(string Title, Color Color, OverlayLineStyle LineStyle);
