using System.ComponentModel;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Where a strip's context menu was asked for, in screen coordinates.</summary>
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
    /// <summary>
    /// The Q range every strip accepts. Held here rather than only in the
    /// designer because the panel needs it to bound Auto Tune even when the bank
    /// holds no strip to read it from.
    /// </summary>
    internal const double MinimumQ = 0.1;

    /// <inheritdoc cref="MinimumQ"/>
    internal const double MaximumQ = 20;

    // The strip being carried is dimmed, so the gap it will leave and the cell it
    // currently sits in read differently from the strips it is passing over. One
    // colour for all three shapes: a strip in flight is a strip in flight, and its
    // type is still named in the header.
    private static readonly Color DraggingBackColor = Color.FromArgb(32, 36, 45);

    private int slotNumber = 1;
    private PeqBandType bandType = PeqBandType.Peaking;
    private bool suppressGainSync;
    private bool selected;
    private bool dragging;
    private bool dragArmed;
    private Point dragOrigin;

    public PeqSlotControl()
    {
        InitializeComponent();
        qInput.Minimum = (decimal)MinimumQ;
        qInput.Maximum = (decimal)MaximumQ;
        WireGainFader();
        HookActivation(this);
        // The number strip is the drag handle. It has to be: the fader owns the
        // mouse for gain and the three fields own it for editing, and WinForms
        // mouse events do not bubble to the parent, so there is nowhere else on
        // the strip a drag could start without stealing a working gesture.
        HookDragHandle(slotLabel);
        slotLabel.Cursor = Cursors.SizeAll;
        // The designer's colours are a starting point for the design surface; the
        // palette is what the strip actually wears.
        ApplyStripColor();
    }

    // Raised when the user clicks the slot or focuses any of its fields, so the
    // host can show this band's individual contribution.
    public event EventHandler? Activated;

    // Raised once the pointer has left the system drag threshold with the number
    // strip held down. The host runs the drag-and-drop loop, because only it
    // knows the bank the strip is being moved within.
    public event EventHandler? DragStartRequested;

    // Raised on a right-click of the number strip: the host offers the band shapes,
    // since it owns the bank the change is an undo step of.
    internal event EventHandler<PeqSlotMenuEventArgs>? TypeMenuRequested;

    public void SetSelected(bool isSelected)
    {
        selected = isSelected;
        ApplyStripColor();
    }

    // Marks the strip as the one currently being carried by the pointer.
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
        // The fader paints its background from the strip colour, so it must be
        // told to repaint when the tint changes. It also gates its click-to-drag
        // on whether this band is the selected one.
        fader.StripActive = selected;
        fader.BackColor = color;
        fader.Invalidate();
    }

    // Registers the whole strip — fields and fader included — as a drop target,
    // so a drag passing over any part of it reaches the host's handlers instead
    // of dying on a child window that never registered one.
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

    // Keeps the vertical fader and the gain field in lock-step: the numeric field
    // stays the source of truth (the host reads it), the fader is a view over it.
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

    /// <summary>
    /// The filter shape this strip holds. The three fields stay the same, but a
    /// shelf reads two of them differently (see <see cref="PeqBandType"/>), so the
    /// header names the type rather than leaving it to be guessed from the curve.
    /// </summary>
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
        }
    }

    /// <summary>Short header token for a band shape: "PK", "LS" or "HS".</summary>
    internal static string DescribeType(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => "LS",
        PeqBandType.HighShelf => "HS",
        _ => "PK"
    };

    private void UpdateSlotLabel() =>
        slotLabel.Text = $"{slotNumber} {DescribeType(bandType)}";

    // Applies a new gain range to both the numeric field and the fader so they
    // keep sharing one scale. The min is <= 0 <= max, so ordering never inverts;
    // the field clamps its value and the fader is re-mirrored to match.
    internal void SetGainRange(decimal minimum, decimal maximum)
    {
        gainInput.Minimum = minimum;
        gainInput.Maximum = maximum;
        fader.Minimum = (double)minimum;
        fader.Maximum = (double)maximum;
        fader.Value = (double)gainInput.Value;
    }

    /// <summary>
    /// Lands text typed into any of the three fields without waiting for the focus
    /// to leave it. A field commits on Leave or Enter, which is enough while the
    /// application is running but not when it is being torn down with the caret
    /// still in the box.
    /// </summary>
    internal void CommitPendingText()
    {
        frequencyInput.CommitText();
        qInput.CommitText();
        gainInput.CommitText();
    }

    // The number strip, which doubles as the drag handle: the host tips it.
    internal Control SlotLabel => slotLabel;

    internal DarkNumericUpDown FrequencyInput => frequencyInput;

    internal DarkNumericUpDown QInput => qInput;

    internal DarkNumericUpDown GainInput => gainInput;
}
