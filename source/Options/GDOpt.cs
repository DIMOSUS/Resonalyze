using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

public partial class GDOpt : Form
{
    private readonly ToolTip toolTip = new();
    private ExpSweepMeasurement? expSweepMeasurement;
    private bool initializing;

    public GDOpt()
    {
        InitializeComponent();

        numericLeftWindow.ValueChanged += SettingsValueChanged;
        numericRightWindow.ValueChanged += SettingsValueChanged;
        numericOffset.ValueChanged += SettingsValueChanged;
        SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
        InitializeToolTips();
        FormClosed += GDOpt_FormClosed;
    }

    public void Init(ExpSweepMeasurement expSweepMeasurement, FrequencyResponseOptions opt)
    {
        if (!ReferenceEquals(this.expSweepMeasurement, expSweepMeasurement))
        {
            if (this.expSweepMeasurement != null)
            {
                this.expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
            }

            this.expSweepMeasurement = expSweepMeasurement;
            this.expSweepMeasurement.ImpulseResponseChanged += ExpSweepMeasurement_ImpulseResponseChanged;
        }

        initializing = true;
        try
        {
            numericWindow.Value = opt.Window;
            numericLeftWindow.Value = opt.LeftTukeyWindow;
            numericRightWindow.Value = opt.RightTukeyWindow;
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(opt.SmoothingInverseOctaves);
            numericOffset.Value = opt.Offset;
        }
        finally
        {
            initializing = false;
        }

        UpdateTukeyWindowLimits();
        UpdateOffsetAvailability();
        UpdateIrPreview();
    }

    public void SetOptions(FrequencyResponseOptions opt)
    {
        opt.Window = (int)numericWindow.Value;
        opt.LeftTukeyWindow = (int)numericLeftWindow.Value;
        opt.RightTukeyWindow = (int)numericRightWindow.Value;
        opt.SmoothingInverseOctaves =
            comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                ? inverseOctaves
                : SmoothingPresetOptions.SupportedInverseOctaves[0];
        opt.Offset = (int)numericOffset.Value;
        UpdateIrPreview();
    }

    private void numericWindow_ValueChanged(object sender, EventArgs e)
    {
        UpdateTukeyWindowLimits();
        UpdateIrPreview();
    }

    private void SettingsValueChanged(object? sender, EventArgs e)
    {
        UpdateTukeyWindowLimits();
        UpdateIrPreview();
    }

    private void ExpSweepMeasurement_ImpulseResponseChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke((MethodInvoker)UpdateIrPreview);
            return;
        }

        UpdateOffsetAvailability();
        UpdateIrPreview();
    }

    private void GDOpt_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (expSweepMeasurement != null)
        {
            expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
        }

        toolTip.Dispose();
    }

    private void UpdateIrPreview()
    {
        if (initializing || expSweepMeasurement == null)
        {
            return;
        }

        ImpulseWindowPreview.Update(
            irPlotView,
            expSweepMeasurement,
            (int)numericWindow.Value,
            (int)numericLeftWindow.Value,
            (int)numericRightWindow.Value,
            expSweepMeasurement.TransferImpulseResponse is { Length: > 0 }
                ? 0
                : (int)numericOffset.Value,
            IrPreviewSource.TransferFromStart);
    }

    private void UpdateOffsetAvailability()
    {
        bool useTransfer = expSweepMeasurement?.TransferImpulseResponse is { Length: > 0 };
        numericOffset.Enabled = !useTransfer;
        toolTip.SetToolTip(
            numericOffset,
            useTransfer
                ? "Offset is disabled for transfer-function IR because Group Delay is referenced to the start of the IR."
                : "Shifts the analysis window relative to the detected sweep-deconvolution IR peak.");
    }

    private void UpdateTukeyWindowLimits()
    {
        TukeyWindowControlHelper.ClampAndUpdateLimits(
            numericWindow,
            numericLeftWindow,
            numericRightWindow);
    }

    private void InitializeToolTips()
    {
        toolTip.SetToolTip(
            numericWindow,
            "Sets the analysis window length used for Group Delay calculation.");
        toolTip.SetToolTip(
            numericLeftWindow,
            "Controls the fade-in part of the Tukey window before the selected impulse region.");
        toolTip.SetToolTip(
            numericRightWindow,
            "Controls the fade-out part of the Tukey window after the selected impulse region.");
        toolTip.SetToolTip(
            comboSmoothingInverseOctaves,
            "Applies octave smoothing to the resulting Group Delay curve.");
        toolTip.SetToolTip(
            irPlotView,
            "Preview of the IR used for Group Delay together with the current analysis window.");
    }

}
