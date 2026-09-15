using System;
using System.Windows.Forms;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>Base for option panels with a live IR preview: measurement subscription, UI-thread marshal, Disposed cleanup
/// (an unshown dialog never raises FormClosed), Init render suppression, and the shared Tukey/gate control groups.</summary>
public class ImpulsePreviewOptionsForm : Form
{
    protected readonly WrappingToolTip toolTip = new();
    private bool initializingControls;

    private (DarkNumericUpDown Window, DarkNumericUpDown Left, DarkNumericUpDown Right)? lengths;
    private (DarkNumericUpDown Offset, CheckBox AutoFit, Label MinFrequency)? gate;

    public ImpulsePreviewOptionsForm()
    {
        Disposed += (_, _) =>
        {
            DetachMeasurement();
            toolTip.Dispose();
        };
    }

    protected ExpSweepMeasurement? Measurement { get; private set; }

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

    /// <summary>Suppresses the several renders each ValueChanged would trigger before Init's final render.</summary>
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

    protected virtual void RenderIrPreview()
    {
    }

    /// <summary>Call once from the constructor. <paramref name="afterWindowChanged"/>: extra work a window edit implies.</summary>
    protected void BindTukeyWindowControls(
        DarkNumericUpDown window,
        DarkNumericUpDown left,
        DarkNumericUpDown right,
        Action? afterWindowChanged = null)
    {
        lengths = (window, left, right);
        window.ValueChanged += (_, _) =>
        {
            RefreshTukeyWindowLimits();
            afterWindowChanged?.Invoke();
            UpdateIrPreview();
        };
        left.ValueChanged += TukeyFadeChanged;
        right.ValueChanged += TukeyFadeChanged;
    }

    protected void RefreshTukeyWindowLimits()
    {
        if (lengths is { } l)
        {
            TukeyWindowControlHelper.ClampAndUpdateLimits(l.Window, l.Left, l.Right);
        }
    }

    protected void BindGateControls(
        DarkNumericUpDown offset,
        CheckBox autoFit,
        DarkNumericUpDown left,
        DarkNumericUpDown window,
        DarkNumericUpDown right,
        Label minFrequency)
    {
        gate = (offset, autoFit, minFrequency);
        lengths = (window, left, right);

        offset.ValueChanged += (_, _) => UpdateIrPreview();
        autoFit.CheckedChanged += (_, _) =>
        {
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

    /// <summary>CheckedChanged fires only on a transition, so Init calls this for a false -> false init.</summary>
    protected void SyncGateOffsetEnabled()
    {
        if (gate is { } g)
        {
            g.Offset.Enabled = !g.AutoFit.Checked;
        }
    }

    protected void UpdateMinFrequencyLabel()
    {
        if (lengths is not { } l || gate is not { } g)
        {
            return;
        }

        double hz = FrequencyResponseOptions.GateMinReliableFrequencyHz(
            (double)l.Left.Value,
            (double)l.Window.Value,
            (double)l.Right.Value);
        g.MinFrequency.Text = hz > 0
            ? $"Reliable from ≈ {hz:0}+ Hz"
            : "Reliable from ≈ — Hz";
    }

    /// <summary>Band-limited first-arrival front, memoized per IR in TransferIrStartCache.</summary>
    protected void ApplyAutoGateOffset()
    {
        if (gate is { } g &&
            Measurement is
            {
                Transfer.ImpulseResponse.Length: > 0,
                SampleRate: > 0
            } measurement)
        {
            g.Offset.Value = g.Offset.ClampValue(
                TransferIrStartCache.ResolveStartMs(
                    measurement.Transfer.ImpulseResponse,
                    measurement.SampleRate,
                    measurement.Transfer.PeakIndex));
        }
    }

    /// <summary>Auto re-snaps the offset on every refresh, including every ImpulseResponseChanged.</summary>
    protected void RenderGatedIrPreview(PlotView plotView, CompareAnalysisSource? compare)
    {
        if (gate?.AutoFit.Checked == true)
        {
            ApplyAutoGateOffset();
        }

        OnGatePreviewRendering();

        if (Measurement is not { SampleRate: > 0 } measurement ||
            lengths is not { } l ||
            gate is not { } g)
        {
            return;
        }

        ImpulseWindowPreview.UpdateGated(
            plotView,
            measurement,
            (double)g.Offset.Value,
            (double)l.Left.Value,
            (double)l.Window.Value,
            (double)l.Right.Value,
            IrPreviewSource.Primary,
            compare);
    }

    protected virtual void OnGatePreviewRendering()
    {
    }

    protected void ApplyGateToolTips()
    {
        if (lengths is not { } l || gate is not { } g)
        {
            return;
        }

        g.Offset.ApplyToolTip(
            toolTip,
            "Gate position: time from the IR start to the end of the left Tukey shoulder. Auto keeps it snapped to the detected IR start.");
        toolTip.SetToolTip(
            g.AutoFit,
            "Keep the gate offset snapped to the detected IR start (band-limited first-arrival front), following every new measurement. Release to set the offset manually.");
        l.Window.ApplyToolTip(
            toolTip,
            "Flat (weight 1) part of the gate after the peak, in milliseconds.");
        l.Left.ApplyToolTip(
            toolTip,
            "Tukey fade-in before the peak, in milliseconds. Keep short.");
        l.Right.ApplyToolTip(
            toolTip,
            "Tukey fade-out gate after the plateau, in milliseconds. End it before the first reflection.");
        toolTip.SetToolTip(
            g.MinFrequency,
            "Lowest frequency the current gate can resolve (≈ 1 / gate length). Below it the curve is not reliable.");
    }

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
