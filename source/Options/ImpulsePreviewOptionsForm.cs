using System;
using System.Windows.Forms;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>
/// Base class for the options panels with a live impulse-response preview
/// (FROptions, GDOpt, PROpt, BDOpt, WaterfallOptions). Owns the plumbing that
/// had drifted across five copies: the measurement subscription swap, the
/// UI-thread marshal of ImpulseResponseChanged, the Disposed cleanup (a dialog
/// disposed without ever having been shown never raises FormClosed), and the
/// suppression of redundant preview renders while Init writes control values.
/// Control writes go through <see cref="DarkNumericUpDownExtensions.ClampValue"/>.
///
/// It also owns the two control groups the panels share. The Tukey trio
/// (window + left/right fades) is bound by <see cref="BindTukeyWindowControls"/>;
/// the gate group (offset + Auto + the trio + the reliable-frequency label) by
/// <see cref="BindGateControls"/>, which brings the Auto-snap, the
/// reliable-frequency read-out, the gated render and the tooltips that go with
/// them. A panel binds what it has and overrides nothing else.
/// </summary>
public class ImpulsePreviewOptionsForm : Form
{
    protected readonly WrappingToolTip toolTip = new();
    private bool initializingControls;

    // The three length controls both groups share: the plateau/FFT window and
    // its two Tukey fades. Only the Tukey group clamps them against each other;
    // the gate group reads them for the reliable-frequency label and the render.
    private DarkNumericUpDown? windowLength;
    private DarkNumericUpDown? leftFade;
    private DarkNumericUpDown? rightFade;
    private DarkNumericUpDown? gateOffset;
    private CheckBox? gateAutoFit;
    private Label? gateMinFrequency;

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

    /// <summary>
    /// Binds a window/left-fade/right-fade trio: any edit re-clamps the fades
    /// against the window length and re-renders the preview. Call once from the
    /// constructor, after InitializeComponent. <paramref name="afterWindowChanged"/>
    /// carries the panel-specific extra a window edit implies (BurstDecay and
    /// Waterfall recompute their captured-time read-out).
    /// </summary>
    protected void BindTukeyWindowControls(
        DarkNumericUpDown window,
        DarkNumericUpDown left,
        DarkNumericUpDown right,
        Action? afterWindowChanged = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        windowLength = window;
        leftFade = left;
        rightFade = right;

        window.ValueChanged += (_, _) =>
        {
            RefreshTukeyWindowLimits();
            afterWindowChanged?.Invoke();
            UpdateIrPreview();
        };
        left.ValueChanged += TukeyFadeChanged;
        right.ValueChanged += TukeyFadeChanged;
    }

    /// <summary>
    /// Re-clamps the fade limits against the current window length. Init calls
    /// it directly, inside the <see cref="InitializeControls"/> suppression.
    /// </summary>
    protected void RefreshTukeyWindowLimits()
    {
        if (windowLength is { } window && leftFade is { } left && rightFade is { } right)
        {
            TukeyWindowControlHelper.ClampAndUpdateLimits(window, left, right);
        }
    }

    /// <summary>
    /// Binds the gated-mode group (Phase, Group Delay): the Auto checkbox snaps
    /// the offset to the detected IR start and locks the field, every gate edit
    /// refreshes the reliable-frequency read-out, and all of them re-render the
    /// preview.
    /// </summary>
    protected void BindGateControls(
        DarkNumericUpDown offset,
        CheckBox autoFit,
        DarkNumericUpDown left,
        DarkNumericUpDown window,
        DarkNumericUpDown right,
        Label minFrequency)
    {
        ArgumentNullException.ThrowIfNull(offset);
        ArgumentNullException.ThrowIfNull(autoFit);
        ArgumentNullException.ThrowIfNull(minFrequency);
        gateOffset = offset;
        gateAutoFit = autoFit;
        gateMinFrequency = minFrequency;
        windowLength = window;
        leftFade = left;
        rightFade = right;

        offset.ValueChanged += (_, _) => UpdateIrPreview();
        autoFit.CheckedChanged += (_, _) =>
        {
            // Auto pressed: the offset field turns read-only and follows the
            // estimated IR start; released: the field unlocks keeping the last
            // value as the manual starting point.
            SyncGateOffsetEnabled();
            if (autoFit.Checked)
            {
                ApplyAutoGateOffset();
            }
        };
        window.ValueChanged += GateValueChanged;
        left.ValueChanged += GateValueChanged;
        right.ValueChanged += GateValueChanged;
    }

    /// <summary>
    /// CheckedChanged only fires on a transition, so Init calls this to sync the
    /// offset field's enabled state for a false -> false init too.
    /// </summary>
    protected void SyncGateOffsetEnabled()
    {
        if (gateOffset is { } offset && gateAutoFit is { } autoFit)
        {
            offset.Enabled = !autoFit.Checked;
        }
    }

    /// <summary>Rewrites the "reliable from" read-out from the current gate.</summary>
    protected void UpdateMinFrequencyLabel()
    {
        if (gateMinFrequency is not { } label ||
            windowLength is not { } window ||
            leftFade is not { } left ||
            rightFade is not { } right)
        {
            return;
        }

        double hz = FrequencyResponseOptions.GateMinReliableFrequencyHz(
            (double)left.Value,
            (double)window.Value,
            (double)right.Value);
        label.Text = hz > 0
            ? $"Reliable from ≈ {hz:0}+ Hz"
            : "Reliable from ≈ — Hz";
    }

    /// <summary>
    /// Snaps the gate offset to the estimated IR start (band-limited
    /// first-arrival front, memoized per IR in TransferIrStartCache).
    /// </summary>
    protected void ApplyAutoGateOffset()
    {
        if (gateOffset is { } offset &&
            Measurement is { } measurement &&
            TransferIrStartCache.ResolveStartMs(measurement) is { } startMs)
        {
            offset.Value = offset.ClampValue(startMs);
        }
    }

    /// <summary>
    /// The gated panels' whole preview render. Auto mode re-snaps the offset
    /// whenever the preview refreshes — which includes every
    /// ImpulseResponseChanged of the measurement.
    /// </summary>
    protected void RenderGatedIrPreview(PlotView plotView, CompareAnalysisSource? compare)
    {
        if (gateAutoFit?.Checked == true)
        {
            ApplyAutoGateOffset();
        }

        OnGatePreviewRendering();

        if (Measurement is not { SampleRate: > 0 } measurement ||
            gateOffset is not { } offset ||
            windowLength is not { } window ||
            leftFade is not { } left ||
            rightFade is not { } right)
        {
            return;
        }

        ImpulseWindowPreview.UpdateGated(
            plotView,
            measurement,
            (double)offset.Value,
            (double)left.Value,
            (double)window.Value,
            (double)right.Value,
            IrPreviewSource.Primary,
            compare);
    }

    /// <summary>
    /// Runs after the Auto snap and before the render — the hook for a panel
    /// whose own read-outs track the gate (Phase refreshes its resolved tau).
    /// </summary>
    protected virtual void OnGatePreviewRendering()
    {
    }

    /// <summary>Applies the tooltips shared by every gated panel control.</summary>
    protected void ApplyGateToolTips()
    {
        if (gateOffset is not { } offset ||
            gateAutoFit is not { } autoFit ||
            gateMinFrequency is not { } minFrequency ||
            windowLength is not { } window ||
            leftFade is not { } left ||
            rightFade is not { } right)
        {
            return;
        }

        offset.ApplyToolTip(
            toolTip,
            "Gate position: time from the IR start to the end of the left Tukey shoulder. Auto keeps it snapped to the detected IR start.");
        toolTip.SetToolTip(
            autoFit,
            "Keep the gate offset snapped to the detected IR start (band-limited first-arrival front), following every new measurement. Release to set the offset manually.");
        window.ApplyToolTip(
            toolTip,
            "Flat (weight 1) part of the gate after the peak, in milliseconds.");
        left.ApplyToolTip(
            toolTip,
            "Tukey fade-in before the peak, in milliseconds. Keep short.");
        right.ApplyToolTip(
            toolTip,
            "Tukey fade-out gate after the plateau, in milliseconds. End it before the first reflection.");
        toolTip.SetToolTip(
            minFrequency,
            "Lowest frequency the current gate can resolve (≈ 1 / gate length). Below it the curve is not reliable.");
    }

    /// <summary>Re-draws the preview after the Compare selection changes while docked.</summary>
    public void RefreshComparePreview() => UpdateIrPreview();

    private void TukeyFadeChanged(object? sender, EventArgs e)
    {
        RefreshTukeyWindowLimits();
        UpdateIrPreview();
    }

    private void GateValueChanged(object? sender, EventArgs e)
    {
        UpdateMinFrequencyLabel();
        UpdateIrPreview();
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
