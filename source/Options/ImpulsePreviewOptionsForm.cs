using System;
using System.Windows.Forms;

namespace Resonalyze.Options;

/// <summary>
/// Base class for the options panels with a live impulse-response preview
/// (FROptions, GDOpt, PROpt, BDOpt, WaterfallOptions). Owns the plumbing that
/// had drifted across five copies: the measurement subscription swap, the
/// UI-thread marshal of ImpulseResponseChanged, the Disposed cleanup (a dialog
/// disposed without ever having been shown never raises FormClosed), the
/// suppression of redundant preview renders while Init writes control values,
/// and the numeric clamp helper.
/// </summary>
public class ImpulsePreviewOptionsForm : Form
{
    protected readonly ToolTip toolTip = new();
    private bool initializingControls;

    public ImpulsePreviewOptionsForm()
    {
        Disposed += (_, _) =>
        {
            DetachMeasurement();
            toolTip.Dispose();
        };
    }

    protected ExpSweepMeasurement? Measurement { get; private set; }

    /// <summary>
    /// Points the panel at a measurement, re-targeting the
    /// ImpulseResponseChanged subscription when the instance changes.
    /// </summary>
    protected void AttachMeasurement(ExpSweepMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (ReferenceEquals(Measurement, measurement))
        {
            return;
        }

        DetachMeasurement();
        Measurement = measurement;
        measurement.ImpulseResponseChanged += HandleImpulseResponseChanged;
    }

    /// <summary>
    /// Runs Init's control writes with preview renders suppressed: every
    /// ValueChanged would otherwise rebuild the preview several times before
    /// Init's final explicit render.
    /// </summary>
    protected void InitializeControls(Action applyValues)
    {
        ArgumentNullException.ThrowIfNull(applyValues);
        initializingControls = true;
        try
        {
            applyValues();
        }
        finally
        {
            initializingControls = false;
        }
    }

    protected void UpdateIrPreview()
    {
        if (initializingControls)
        {
            return;
        }

        RenderIrPreview();
    }

    /// <summary>
    /// The panel's actual preview render; only called outside Init suppression.
    /// </summary>
    protected virtual void RenderIrPreview()
    {
    }

    protected static decimal ClampToControl(DarkNumericUpDown control, double value)
    {
        decimal candidate = double.IsFinite(value) ? (decimal)value : 0m;
        return Math.Clamp(candidate, control.Minimum, control.Maximum);
    }

    private void DetachMeasurement()
    {
        if (Measurement != null)
        {
            Measurement.ImpulseResponseChanged -= HandleImpulseResponseChanged;
            Measurement = null;
        }
    }

    private void HandleImpulseResponseChanged()
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

        UpdateIrPreview();
    }
}
