using Resonalyze.Options;

namespace Resonalyze;

// The phase view's gate dialog; the phase rules and curves live in EqWizardPhase and EqWizardPreviews.
// See docs/tech/eq-auto-tuner.md#phase-mode.
public partial class EqWizardPanel
{
    /// <summary>The Virtual DSP gate dialog, so a window reads the same in both tools.</summary>
    private void OpenPhaseGateDialog()
    {
        if (session.Source is not { } source || session.PhaseContext is not { } context)
        {
            return;
        }

        int sampleRate = source.Measurement!.SampleRate;
        var traces = new List<IrPreviewTrace>
        {
            new(
                EqWizardPhase.EditedResponse(source, session.Bank.Curve, session.ProcessorSampleRateHz),
                EqWizardPhaseRender.EditedChannelTitle,
                EqWizardPhaseRender.EditedChannelColor)
        };
        traces.AddRange(context.Neighbours.Select(neighbour =>
            new IrPreviewTrace(neighbour.ImpulseResponse, neighbour.Name, neighbour.Color)));

        bool committedPin = session.PhaseGatePinned;
        using var dialog = new VirtualCrossoverGateDialog();
        // The plot tracks the dialog live, like Virtual DSP: a gate is placed by watching its effect.
        dialog.PreviewChanged = (offsetMs, autoOffset, leftMs, plateauMs, rightMs,
            windowMode, fdwCycles, detrendMode, detrendMs) =>
        {
            session.ApplyPhaseGate(
                context, offsetMs, autoOffset, leftMs, plateauMs, rightMs,
                windowMode, fdwCycles, detrendMode, detrendMs);
            if (session.PhaseMode)
            {
                Redraw();
            }
        };
        dialog.Init(
            traces,
            sampleRate,
            context.GateOffsetMs,
            context.Gate.LeftMs,
            context.Gate.PlateauMs,
            context.Gate.RightMs,
            context.DetrendMs,
            context.Gate.WindowMode,
            context.Gate.FdwCycles,
            context.Gate.DetrendMode,
            fitToMs: EqWizardPhase.AutoGateFitOffsetMs(context),
            autoOffset: !committedPin);
        DialogResult result = dialog.ShowDialog(FindForm());
        dialog.PreviewChanged = null;
        if (result == DialogResult.OK)
        {
            session.ApplyPhaseGate(
                context, dialog.GateOffsetMs, dialog.AutoOffset, dialog.LeftMs,
                dialog.PlateauMs, dialog.RightMs, dialog.WindowMode, dialog.FdwCycles,
                dialog.DetrendMode, dialog.DetrendMs);
        }
        else
        {
            session.RestorePhaseGate(context, committedPin);
        }

        Redraw();
    }
}
