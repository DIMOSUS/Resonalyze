using System.ComponentModel;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class PeqSlotMenuEventArgs : EventArgs
{
    public PeqSlotMenuEventArgs(Point screenPoint)
    {
        ScreenPoint = screenPoint;
    }

    public Point ScreenPoint { get; }
}

public partial class PeqSlotControl : UserControl
{
    private static readonly Color DraggingBackColor = UiPalette.SunkenSurface;

    private int slotNumber = 1;
    private PeqBandType bandType = PeqBandType.Peaking;
    private double sampleRateHz = 48_000;
    private bool suppressGainSync;
    private bool selected;
    private bool dragging;
    private bool dragArmed;
    private Point dragOrigin;

    public PeqSlotControl()
    {
        InitializeComponent();
        // The session holds every band as these fields show it, so both take their ranges from one place.
        frequencyInput.ApplyFieldRange(EqWizardLimits.BandFrequency);
        qInput.ApplyFieldRange(EqWizardLimits.BandQ);
        WireGainFader();
        // Hooked unconditionally: the strip may become an all-pass at any time.
        frequencyInput.ValueChanged += (_, _) => UpdateGroupDelayReadout();
        qInput.ValueChanged += (_, _) => UpdateGroupDelayReadout();
        HookActivation(this);
        // The number strip is the only drag handle: fader and fields own the mouse, and WinForms mouse events do not bubble.
        HookDragHandle(slotLabel);
        slotLabel.Cursor = Cursors.SizeAll;
        ApplyStripColor();
    }

    public event EventHandler? Activated;

    // The host runs the drag loop: only it knows the bank.
    public event EventHandler? DragStartRequested;

    internal event EventHandler<PeqSlotMenuEventArgs>? TypeMenuRequested;

    public void SetSelected(bool isSelected)
    {
        selected = isSelected;
        ApplyStripColor();
    }

    internal void SetDragging(bool isDragging)
    {
        dragging = isDragging;
        ApplyStripColor();
    }

    private void ApplyStripColor()
    {
        Color color = dragging
            ? DraggingBackColor
            : selected
                ? PeqBandPalette.SelectedStrip(bandType)
                : PeqBandPalette.Strip(bandType);
        BackColor = color;
        slotLayout.BackColor = color;
        // The fader paints from the strip colour and gates click-to-drag on selection.
        fader.StripActive = selected;
        fader.BackColor = color;
        fader.Invalidate();
    }

    // Whole strip registered, so a drag over a child window reaches the host instead of dying there.
    internal void EnableDropTarget(DragEventHandler dragOver, DragEventHandler drop)
    {
        foreach (Control control in SelfAndDescendants(this))
        {
            control.AllowDrop = true;
            control.DragEnter += dragOver;
            control.DragOver += dragOver;
            control.DragDrop += drop;
        }
    }

    private static IEnumerable<Control> SelfAndDescendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (Control descendant in SelfAndDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private void HookDragHandle(Control handle)
    {
        handle.MouseDown += (sender, args) =>
        {
            if (args.Button != MouseButtons.Left)
            {
                return;
            }

            dragArmed = true;
            dragOrigin = ((Control)sender!).PointToScreen(args.Location);
        };
        handle.MouseMove += (sender, args) =>
        {
            if (!dragArmed || args.Button != MouseButtons.Left)
            {
                return;
            }

            Point current = ((Control)sender!).PointToScreen(args.Location);
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(current.X - dragOrigin.X) < threshold.Width &&
                Math.Abs(current.Y - dragOrigin.Y) < threshold.Height)
            {
                return;
            }

            dragArmed = false;
            DragStartRequested?.Invoke(this, EventArgs.Empty);
        };
        handle.MouseUp += (sender, args) =>
        {
            dragArmed = false;
            if (args.Button == MouseButtons.Right)
            {
                TypeMenuRequested?.Invoke(
                    this,
                    new PeqSlotMenuEventArgs(((Control)sender!).PointToScreen(args.Location)));
            }
        };
    }

    // The numeric field is the source of truth; the fader is a view over it.
    private void WireGainFader()
    {
        fader.Minimum = (double)gainInput.Minimum;
        fader.Maximum = (double)gainInput.Maximum;
        fader.Increment = (double)gainInput.Increment;
        fader.Value = (double)gainInput.Value;

        gainInput.ValueChanged += (_, _) =>
        {
            if (suppressGainSync)
            {
                return;
            }

            suppressGainSync = true;
            try
            {
                fader.Value = (double)gainInput.Value;
            }
            finally
            {
                suppressGainSync = false;
            }
        };
        fader.ValueChanged += (_, _) =>
        {
            if (suppressGainSync)
            {
                return;
            }

            suppressGainSync = true;
            try
            {
                gainInput.Value = gainInput.ClampValue(fader.Value);
            }
            finally
            {
                suppressGainSync = false;
            }
        };
    }

    private void HookActivation(Control control)
    {
        control.Click += RaiseActivated;
        control.Enter += RaiseActivated;
        foreach (Control child in control.Controls)
        {
            HookActivation(child);
        }
    }

    private void RaiseActivated(object? sender, EventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    [DefaultValue(1)]
    public int SlotNumber
    {
        get => slotNumber;
        set
        {
            slotNumber = Math.Max(1, value);
            UpdateSlotLabel();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal PeqBandType BandType
    {
        get => bandType;
        set
        {
            bandType = value;
            UpdateSlotLabel();
            ApplyStripColor();
            UpdateBandTypeAppearance();
        }
    }

    private void UpdateSlotLabel() =>
        slotLabel.Text = $"{slotNumber} {PeqBandToken.Of(bandType)}";

    // All-pass hides gain in favour of its corner group delay; the gain VALUE stays in the hidden field so switching back restores the bell.
    private void UpdateBandTypeAppearance()
    {
        bool allPass = bandType.IsAllPass();
        gainInput.Visible = !allPass;
        fader.Visible = !allPass;
        groupDelayLabel.Visible = allPass;
        // First-order all-pass has no Q; greyed, not hidden, so the strip keeps its shape.
        qInput.Enabled = bandType != PeqBandType.AllPassFirstOrder;
        UpdateGroupDelayReadout();
    }

    private void UpdateGroupDelayReadout()
    {
        if (!bandType.IsAllPass())
        {
            return;
        }

        // At the realised corner (Nyquist-clamped) and rate; formats match the Virtual DSP channel card.
        double ms = AllPassFilter.CornerGroupDelaySeconds(
            PeqBiquad.ToAllPassSpec(new PeqBand(
                (double)frequencyInput.Value, (double)qInput.Value, 0, bandType)),
            sampleRateHz) * 1_000.0;
        groupDelayLabel.Text = ms < 100 ? $"= {ms:0.00} ms" : $"= {ms:0} ms";
    }

    /// <summary>Biquad realisation rate for the GD readout, pushed by the panel (which knows the rate's owner).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal double SampleRateHz
    {
        get => sampleRateHz;
        set
        {
            if (value <= 0 || value == sampleRateHz)
            {
                return;
            }

            sampleRateHz = value;
            UpdateGroupDelayReadout();
        }
    }

    internal void SetGainRange(decimal minimum, decimal maximum)
    {
        gainInput.Minimum = minimum;
        gainInput.Maximum = maximum;
        fader.Minimum = (double)minimum;
        fader.Maximum = (double)maximum;
        fader.Value = (double)gainInput.Value;
    }

    /// <summary>Lands typed text without focus loss, for teardown with the caret still in a field.</summary>
    internal void CommitPendingText()
    {
        frequencyInput.CommitText();
        qInput.CommitText();
        gainInput.CommitText();
    }

    internal Control SlotLabel => slotLabel;

    internal ThemedNumericUpDown FrequencyInput => frequencyInput;

    internal ThemedNumericUpDown QInput => qInput;

    internal ThemedNumericUpDown GainInput => gainInput;

    internal Control GroupDelayReadout => groupDelayLabel;
}
