using OxyPlot;

namespace Resonalyze;

internal sealed partial class TimeAlignmentPanelController
{
    private void WireEvents()
    {
        // Both radios fire CheckedChanged; react to the arriving one only.
        void OnRadio(object? sender, EventArgs _)
        {
            if (!applyingOptions && sender is RadioButton { Checked: true })
            {
                ApplyBandpassOptionChange();
            }
        }
        void OnNumeric(object? sender, EventArgs _)
        {
            if (!applyingOptions)
            {
                ApplyBandpassOptionChange();
            }
        }
        bandModeFullRadio.CheckedChanged += OnRadio;
        bandModeAutoRadio.CheckedChanged += OnRadio;
        bandModeManualRadio.CheckedChanged += OnRadio;
        bandpassCenterNumeric.ValueChanged += OnNumeric;
        bandpassPassOctavesNumeric.ValueChanged += OnNumeric;
        bandpassFadeOctavesNumeric.ValueChanged += OnNumeric;
    }

    private void ApplyBandpassOptionChange()
    {
        UpdateOptionsFromControls();
        RefreshAnalysis();
        saveSettings();
    }

    // Writing controls raises the user-edit events, which would save the controls back into the options.
    private bool applyingOptions;

    private void ApplyOptionsToControls()
    {
        applyingOptions = true;
        try
        {
            bandModeFullRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.FullBand;
            bandModeAutoRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.AutoBand;
            bandModeManualRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.ManualBand;
            bandpassCenterNumeric.Value =
                bandpassCenterNumeric.ClampValue(session.Options.BandpassCenterHz);
            bandpassPassOctavesNumeric.Value =
                bandpassPassOctavesNumeric.ClampValue(session.Options.BandpassPassOctaves);
            bandpassFadeOctavesNumeric.Value =
                bandpassFadeOctavesNumeric.ClampValue(session.Options.BandpassFadeOctaves);
        }
        finally
        {
            applyingOptions = false;
        }

        UpdateBandpassControlStates();
    }

    private void UpdateOptionsFromControls()
    {
        session.Options.BandMode =
            bandModeAutoRadio.Checked ? TimeAlignmentBandMode.AutoBand
            : bandModeManualRadio.Checked ? TimeAlignmentBandMode.ManualBand
            : TimeAlignmentBandMode.FullBand;
        session.Options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        session.Options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        session.Options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
        UpdateBandpassControlStates();
    }

    private void UpdateBandpassControlStates()
    {
        bool manual = bandModeManualRadio.Checked;
        bandpassCenterNumeric.Enabled = manual;
        bandpassPassOctavesNumeric.Enabled = manual;
        bandpassFadeOctavesNumeric.Enabled = manual;
    }

    private void UpdateAutoBandLabel()
    {
        autoBandLabel.Text = TimeAlignmentBand.AutoBandCaption(session);
    }

    private void UpdateBandpassPreview()
    {
        PlotModel model = TimeAlignmentPreviews.Bandpass(
            session,
            (double)bandpassCenterNumeric.Value,
            (double)bandpassPassOctavesNumeric.Value,
            (double)bandpassFadeOctavesNumeric.Value);
        bandpassViewports.Show(model, Mode.TimeAlignment);
    }
}
