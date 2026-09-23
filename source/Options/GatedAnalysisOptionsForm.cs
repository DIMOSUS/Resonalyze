using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>Base of the Phase and Group Delay panels: binds the gate, window, smoothing and coherence fields they share to
/// a <see cref="GatedAnalysisSettingsSession"/>, and draws the gate over the transfer IR.</summary>
public class GatedAnalysisOptionsForm : ImpulsePreviewOptionsForm
{
    private GatedAnalysisSettingsSession session = GatedAnalysisSettingsSession.ForGroupDelay();
    private Func<CompareAnalysisSource?>? getCompare;
    private ThemedNumericUpDown? offset;
    private CheckBox? auto;
    private ThemedNumericUpDown? left;
    private ThemedNumericUpDown? plateau;
    private ThemedNumericUpDown? right;
    private Label? minFrequency;
    private ThemedComboBox? windowMode;
    private ThemedComboBox? cycles;
    private ThemedComboBox? smoothing;
    private CheckBox? coherence;
    private PlotView? preview;

    private protected GatedAnalysisSettingsSession Session => session;

    public void RefreshComparePreview() => Redraw();

    private protected void BindGated(
        GatedAnalysisSettingsSession gatedSession,
        ThemedNumericUpDown gateOffset,
        CheckBox autoFit,
        ThemedNumericUpDown leftFade,
        ThemedNumericUpDown gatePlateau,
        ThemedNumericUpDown rightFade,
        Label minFrequencyLabel,
        ThemedComboBox windowModeList,
        ThemedComboBox cyclesList,
        ThemedComboBox smoothingList,
        CheckBox showCoherence,
        PlotView previewView)
    {
        session = gatedSession;
        (offset, auto, left, plateau, right, minFrequency) =
            (gateOffset, autoFit, leftFade, gatePlateau, rightFade, minFrequencyLabel);
        (windowMode, cycles, smoothing, coherence, preview) =
            (windowModeList, cyclesList, smoothingList, showCoherence, previewView);
        PlotInteraction.Enable(previewView);
        gateOffset.ApplyFieldRange(ModeSettingsLimits.GateOffsetMs);
        foreach (ThemedNumericUpDown length in new[] { leftFade, gatePlateau, rightFade })
        {
            length.ApplyFieldRange(ModeSettingsLimits.GateLengthMs);
        }

        smoothingList.FillSmoothingPresets();
        ApplyDefaults();
        Bind(gateOffset, value => session.Gate.OffsetMs = value);
        Bind(autoFit, session.SetAuto);
        Bind(leftFade, value => session.Gate.LeftMs = value);
        Bind(gatePlateau, value => session.Gate.PlateauMs = value);
        Bind(rightFade, value => session.Gate.RightMs = value);
        BindIndex(windowModeList, index =>
            session.WindowMode = session.WindowMode with { Mode = WindowModeChoice.ModeAt(index) });
        BindItem<int>(cyclesList, value => session.WindowMode = session.WindowMode with { Cycles = value });
        BindItem<int>(smoothingList, value => session.SmoothingInverseOctaves = value);
        Bind(showCoherence, on => session.Curves.ShowCoherence = on);
        gateOffset.ApplyToolTip(toolTip, GateReadout.Offset);
        toolTip.SetToolTip(autoFit, GateReadout.Auto);
        gatePlateau.ApplyToolTip(toolTip, GateReadout.Plateau);
        leftFade.ApplyToolTip(toolTip, GateReadout.Left);
        rightFade.ApplyToolTip(toolTip, GateReadout.Right);
        toolTip.SetToolTip(minFrequencyLabel, GateReadout.MinFrequency);
    }

    private protected void InitGated(
        AnalyzerDocument document,
        int configuredSampleRate,
        FrequencyResponseOptions options,
        CurveVisibilityOptions visibility,
        Func<CompareAnalysisSource?>? compare)
    {
        Follow(document, configuredSampleRate);
        getCompare = compare;
        session.Follow(OpenMeasurement);
        session.Load(options, visibility);
        Redraw();
    }

    private protected void WriteGated(FrequencyResponseOptions options, CurveVisibilityOptions visibility)
    {
        session.WriteTo(options, visibility);
        Redraw();
    }

    private protected override void OnMeasurementChanged()
    {
        session.Follow(OpenMeasurement);
        Redraw();
    }

    private protected override PlotView? PreviewView => preview;

    private protected override ImpulsePreviewInput PreviewInput => session.Preview(getCompare?.Invoke());

    private protected override void PresentControls()
    {
        if (offset == null || auto == null || left == null || plateau == null || right == null || minFrequency == null ||
            windowMode == null || cycles == null || smoothing == null || coherence == null)
        {
            return;
        }

        Show(offset, session.Gate.OffsetMs);
        offset.Enabled = session.Gate.OffsetEditable;
        auto.Checked = session.Gate.Auto;
        Show(left, session.Gate.LeftMs);
        Show(plateau, session.Gate.PlateauMs);
        Show(right, session.Gate.RightMs);
        minFrequency.Text = session.Gate.ReliableFrom;
        ShowIndex(windowMode, session.WindowMode.ModeIndex);
        ShowItem(cycles, session.WindowMode.Cycles);
        cycles.Enabled = session.WindowMode.CyclesEditable;
        ShowItem(smoothing, session.SmoothingInverseOctaves);
        coherence.Checked = session.Curves.ShowCoherence;
    }

    private void ApplyDefaults()
    {
        GatedAnalysisDefaults defaults = session.Defaults;
        if (defaults.WindowMode is { } mode && windowMode != null && cycles != null)
        {
            windowMode.DefaultSelectedItem = windowMode.Items[mode.ModeIndex];
            cycles.DefaultSelectedItem = mode.Cycles;
        }

        left!.DefaultValue = defaults.LeftMs;
        plateau!.DefaultValue = defaults.PlateauMs;
        right!.DefaultValue = defaults.RightMs;
        smoothing!.DefaultSelectedItem = defaults.SmoothingInverseOctaves;
    }
}
