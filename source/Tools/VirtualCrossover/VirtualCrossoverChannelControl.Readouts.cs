using Resonalyze.Dsp;

namespace Resonalyze;

public partial class VirtualCrossoverChannelControl
{
    /// <summary>Shows the attached spatial average in the button text, since it gates the hybrid view.</summary>
    internal void SetSpatialAverage(
        string? title,
        double? integratedSeconds,
        bool resolved,
        VirtualCrossoverSpatialAverageMode mode,
        DateTimeOffset? measuredAtUtc = null,
        bool file = false)
    {
        VirtualCrossoverChannelAverageReadout readout = VirtualCrossoverChannelAverageReadout.Read(
            title, integratedSeconds, resolved, mode, measuredAtUtc, file);
        buttonSpatialAverage.Text = readout.Text;
        buttonSpatialAverage.ForeColor = readout.Color;
        spatialAverageTooltip = readout.Tooltip;
        tooltipHost?.SetToolTip(buttonSpatialAverage, spatialAverageTooltip);
    }

    /// <summary>A goal for an edge the channel does not run is not shown as stated; the tooltip names it as kept.</summary>
    internal void SetAcousticGoal(
        JunctionAcousticTarget? highPass,
        JunctionAcousticTarget? lowPass,
        bool highPassRuns = true,
        bool lowPassRuns = true)
    {
        VirtualCrossoverChannelGoalReadout readout =
            VirtualCrossoverChannelGoalReadout.Read(highPass, lowPass, highPassRuns, lowPassRuns);
        buttonAcousticGoal.Text = readout.Text;
        buttonAcousticGoal.ForeColor = readout.Color;
        tooltipHost?.SetToolTip(buttonAcousticGoal, readout.Tooltip);
    }

    internal void SetFir(FirFilter? kernel, string? sourceName, FirCrossoverDesign? design = null)
    {
        firKernel = kernel;
        firSourceName = kernel == null || string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        firDesign = kernel == null ? null : design;
        UpdateFirReadout();
    }

    /// <summary>Why the FIR button is red (FIR crossover at a stale rate, or beside an IIR crossover), or null.</summary>
    internal string? FirConflict => VirtualCrossoverChannelFirReadout.ConflictOf(
        firKernel, firDesign, processorSampleRateHz, SelectedCrossoverKind);

    private double PhaseReferenceHz => VirtualCrossoverChannelPhaseReadout.ReferenceHz(
        SelectedZone, (double)numericHighPassHz.Value, (double)numericLowPassHz.Value);

    public void SetAccentColor(Color color)
    {
        labelChannel.ForeColor = color;
        checkBoxShowProcessed.ForeColor = color;
        checkBoxShowRaw.ForeColor = VirtualCrossoverColors.ChannelAccentFaded(color, BackColor);
    }

    /// <summary>Acoustic polarity read from the measured IR, independent of the Invert switch.</summary>
    public void SetMeasuredPolarity(PolarityEstimate polarity)
    {
        (labelMeasuredPolarity.Text, labelMeasuredPolarity.ForeColor) = polarity switch
        {
            PolarityEstimate.Positive => ("IR: Normal", UiPalette.Success),
            PolarityEstimate.Negative => ("IR: Inverted", UiPalette.Error),
            _ => ("IR: Unknown", UiPalette.TextMuted)
        };
    }

    private void UpdateTotalGain() =>
        labelTotalGain.Text = VirtualCrossoverChannelTotalGain.Text((double)numericGain.Value, peqPreampDb);

    private void UpdateFirReadout()
    {
        VirtualCrossoverChannelFirReadout readout = VirtualCrossoverChannelFirReadout.Read(
            firKernel, firSourceName, firDesign, processorSampleRateHz, SelectedCrossoverKind);
        buttonFir.Text = readout.ButtonText;
        buttonFir.ForeColor = readout.ButtonColor;
        labelFirInfo.Text = readout.Info;
        labelFirInfo.ForeColor = readout.InfoColor;
        if (tooltipHost is { } host)
        {
            host.SetToolTip(buttonFir, readout.ButtonTip);
            host.SetToolTip(labelFirInfo, readout.InfoTip);
        }
    }

    private void UpdatePhaseReadout()
    {
        VirtualCrossoverChannelPhaseReadout readout = VirtualCrossoverChannelPhaseReadout.Read(
            (double)numericPhase.Value, PhaseReferenceHz, processorSampleRateHz);
        labelPhaseInfo.Text = readout.Text;
        labelPhaseInfo.ForeColor = readout.Color;
        if (tooltipHost is { } host)
        {
            numericPhase.ApplyToolTip(host, PhaseTooltip());
        }
    }

    private string PhaseTooltip() => VirtualCrossoverChannelPhaseReadout.Tooltip(
        (double)numericPhase.Value, PhaseReferenceHz, processorSampleRateHz);

    // The tooltip host arrives after construction; whichever comes second applies the text.
    private void UpdateDelayTooltip()
    {
        if (tooltipHost is { } host)
        {
            numericDelay.ApplyToolTip(host, VirtualCrossoverChannelDelayReadout.Tooltip((double)numericDelay.Value));
        }
    }
}
