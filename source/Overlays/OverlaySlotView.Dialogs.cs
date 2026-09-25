namespace Resonalyze;

internal sealed partial class OverlaySlotView
{
    /// <returns>False for an empty or other-mode slot; the long press then leaves the click to the menu.</returns>
    private bool OpenSettings()
    {
        if (!Session.CanConfigure(slot))
        {
            return false;
        }

        switch (slot.Kind)
        {
            case OverlayKind.Operation:
                ConfigureOperation();
                break;
            case OverlayKind.Target:
                ConfigureTarget();
                break;
            default:
                ConfigureCaptured();
                break;
        }

        return true;
    }

    internal void ConfigureCaptured()
    {
        OverlaySlotState state = slot.State;
        bool previewShown = false;
        bool wasCheckedBefore = slot.Checked;
        using var dialog = new OverlaySettingsDialog(
            state.Mode,
            state.Title,
            state.Appearance.Color,
            state.Appearance.StrokeThickness,
            state.Appearance.LineStyle,
            state.Appearance.OpacityPercent,
            state.SmoothingInverseOctaves,
            settings =>
            {
                previewShown = true;
                Session.BeginPreview(slot);
                Session.PreviewCaptured(slot, settings);
            },
            allowPsychoacousticSmoothing: Session.Curves.MagnitudeSmoothingSemantics(slot));
        DialogResult result = dialog.ShowDialog(owner.Form);
        Session.EndPreview(slot);
        if (result != DialogResult.OK)
        {
            if (previewShown)
            {
                Session.RestoreAfterPreview(slot, wasCheckedBefore);
            }

            return;
        }

        if (dialog.ClearRequested)
        {
            Session.Clear(slot);
            return;
        }

        Session.ApplyCaptured(
            slot,
            dialog.OverlayName,
            new OverlayAppearance(dialog.SelectedColor, dialog.StrokeThickness, dialog.LineStyle, dialog.OpacityPercent),
            dialog.SmoothingInverseOctaves);
    }

    internal void ConfigureOperation()
    {
        OverlaySlotState state = slot.State;
        OverlayOperationSettings operation = slot.OperationSeed;
        OverlayDialogSeed seed = slot.DialogSeed(OverlayKind.Operation);
        IReadOnlyList<OverlaySlotOption> sources = Session.CaptureSourceOptions(slot);
        IReadOnlyList<LiveCurveOption> liveCurves = Session.Sources.LiveCurveOptions();

        bool previewShown = false;
        bool wasCheckedBefore = slot.Checked;
        using var dialog = new OverlayOperationSettingsDialog(
            state.Mode,
            seed.Title,
            operation.SourceSlotA,
            operation.SourceCurveKeyA,
            operation.SourceSlotB,
            operation.SourceCurveKeyB,
            operation.Operation,
            operation.BlendFrequencyHz,
            operation.BlendWidthOctaves,
            operation.UseAmplitudeSpace,
            operation.TiltEnabled,
            operation.TiltDbPerOctave,
            operation.TiltPivotHz,
            operation.CompareDelayMs,
            operation.CompareInvertPolarity,
            seed.Color,
            state.Appearance.StrokeThickness,
            seed.LineStyle,
            state.Appearance.OpacityPercent,
            state.SmoothingInverseOctaves,
            sources,
            liveCurves,
            settings =>
            {
                previewShown = true;
                Session.BeginPreview(slot);
                Session.PreviewOperation(slot, settings);
            });
        DialogResult result = dialog.ShowDialog(owner.Form);
        Session.EndPreview(slot);
        if (result != DialogResult.OK)
        {
            if (previewShown)
            {
                Session.RestoreAfterPreview(slot, wasCheckedBefore);
            }

            return;
        }

        Session.ApplyOperation(
            slot,
            dialog.OverlayName,
            new OverlayOperationSettings(
                dialog.SourceSlotA,
                dialog.SourceCurveKeyA,
                dialog.SourceSlotB,
                dialog.SourceCurveKeyB,
                dialog.Operation,
                dialog.BlendFrequencyHz,
                dialog.BlendWidthOctaves,
                dialog.UseAmplitudeSpace,
                dialog.TiltEnabled,
                dialog.TiltDbPerOctave,
                dialog.TiltPivotHz,
                dialog.CompareDelayMs,
                dialog.CompareInvertPolarity),
            new OverlayAppearance(dialog.SelectedColor, dialog.StrokeThickness, dialog.LineStyle, dialog.OpacityPercent),
            dialog.SmoothingInverseOctaves);
    }

    internal void ConfigureTarget()
    {
        OverlaySlotState state = slot.State;
        if (!OverlayTargets.SupportsMode(state.Mode))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        IReadOnlyList<OverlaySlotOption> sources = Session.CaptureSourceOptions(slot);
        OverlayTargetSettings target = slot.TargetSeed;
        OverlayDialogSeed seed = slot.DialogSeed(OverlayKind.Target);

        bool previewShown = false;
        bool wasCheckedBefore = slot.Checked;
        using var dialog = new OverlayTargetSettingsDialog(
            state.Mode,
            seed.Title,
            target.SourceSlot,
            target.Preset,
            target.Spec,
            target.ToleranceDb,
            target.DeviationMode,
            seed.Color,
            state.Appearance.StrokeThickness,
            seed.LineStyle,
            state.Appearance.OpacityPercent,
            state.SmoothingInverseOctaves,
            sources,
            settings =>
            {
                previewShown = true;
                Session.BeginPreview(slot);
                Session.PreviewTarget(slot, settings);
            });
        DialogResult result = dialog.ShowDialog(owner.Form);
        Session.EndPreview(slot);
        if (result != DialogResult.OK)
        {
            if (previewShown)
            {
                Session.RestoreAfterPreview(slot, wasCheckedBefore);
            }

            return;
        }

        Session.ApplyTarget(
            slot,
            dialog.OverlayName,
            new OverlayTargetSettings(
                dialog.SourceSlot,
                dialog.Preset,
                dialog.Spec,
                dialog.ToleranceDb,
                dialog.DeviationMode),
            new OverlayAppearance(dialog.SelectedColor, dialog.StrokeThickness, dialog.LineStyle, dialog.OpacityPercent),
            dialog.SmoothingInverseOctaves);
    }
}
