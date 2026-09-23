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

    private (ThemedNumericUpDown Window, ThemedNumericUpDown Left, ThemedNumericUpDown Right)? lengths;
    private (ThemedNumericUpDown Offset, CheckBox AutoFit, Label MinFrequency)? gate;

    public ImpulsePreviewOptionsForm()
    {
        Disposed += (_, _) =>
        {
            DetachMeasurement();
            toolTip.Dispose();
        };
    }

    private protected AnalyzerDocument? Document { get; private set; }

    private int configuredSampleRate;

    private protected MeasurementResult? Measurement => Document?.Result;

    /// <summary>The open result's rate, or the one the next run is configured for when nothing is open.</summary>
    protected int SampleRate => Measurement?.SampleRate ?? configuredSampleRate;

    private protected void AttachMeasurement(AnalyzerDocument document, int configuredSampleRate)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.configuredSampleRate = configuredSampleRate;
        if (ReferenceEquals(Document, document))
        {
            return;
        }

        DetachMeasurement();
        Document = document;
        document.Changed += HandleImpulseResponseChanged;
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
        ThemedNumericUpDown window,
        ThemedNumericUpDown left,
        ThemedNumericUpDown right,
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
        ThemedNumericUpDown offset,
        CheckBox autoFit,
        ThemedNumericUpDown left,
        ThemedNumericUpDown window,
        ThemedNumericUpDown right,
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

        g.MinFrequency.Text = GateReadout.ReliableFrom(
            (double)l.Left.Value,
            (double)l.Window.Value,
            (double)l.Right.Value);
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

        if (Document == null ||
            lengths is not { } l ||
            gate is not { } g)
        {
            return;
        }

        ImpulseWindowPreview.UpdateGated(
            plotView,
            Measurement,
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

        g.Offset.ApplyToolTip(toolTip, GateReadout.Offset);
        toolTip.SetToolTip(g.AutoFit, GateReadout.Auto);
        l.Window.ApplyToolTip(toolTip, GateReadout.Plateau);
        l.Left.ApplyToolTip(toolTip, GateReadout.Left);
        l.Right.ApplyToolTip(toolTip, GateReadout.Right);
        toolTip.SetToolTip(g.MinFrequency, GateReadout.MinFrequency);
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
        if (Document != null)
        {
            Document.Changed -= HandleImpulseResponseChanged;
            Document = null;
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
            BeginInvoke((MethodInvoker)OnMeasurementChanged);
            return;
        }

        OnMeasurementChanged();
    }

    /// <summary>The open measurement changed: the preview redraws; a panel showing more of it adds to this.</summary>
    protected virtual void OnMeasurementChanged() => UpdateIrPreview();
}
