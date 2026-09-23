using System.ComponentModel;

namespace Resonalyze;

public partial class VirtualCrossoverChannelControl
{
    /// <summary>Hiding the row does not clear an angle already dialled in; it stays in the project and simulated.</summary>
    [DefaultValue(false)]
    public bool PhaseControlShown
    {
        get => phaseControlShown;
        set
        {
            if (phaseControlShown == value)
            {
                return;
            }

            phaseControlShown = value;
            ApplyOptionalRows();
        }
    }

    /// <summary>Hiding the row does not detach a loaded kernel.</summary>
    [DefaultValue(false)]
    public bool FirControlShown
    {
        get => firControlShown;
        set
        {
            if (firControlShown == value)
            {
                return;
            }

            firControlShown = value;
            ApplyOptionalRows();
        }
    }

    [DefaultValue(false)]
    public bool Collapsed
    {
        get => collapsed;
        set
        {
            if (collapsed == value)
            {
                return;
            }

            collapsed = value;
            ApplyCollapsedState();
        }
    }

    // Read off live controls, not pixel literals: rows are DPI-scaled.
    private int FoldLine => comboBoxCrossoverKind.Top;

    private int RowPitch => numericPhase.Top - buttonPeqMenu.Top;

    // Hidden rather than clipped: a clipped field stays in the tab order and counts towards the fold height.
    private bool IsPhaseRow(Control child) =>
        ReferenceEquals(child, labelPhase) ||
        ReferenceEquals(child, numericPhase) ||
        ReferenceEquals(child, labelPhaseInfo);

    private bool IsFirRow(Control child) =>
        ReferenceEquals(child, labelFir) ||
        ReferenceEquals(child, buttonFir) ||
        ReferenceEquals(child, labelFirInfo);

    // The FIR row moves up into the phase row's place when that row is hidden.
    private void PlaceFirRow()
    {
        int offset = phaseControlShown ? RowPitch : 0;
        labelFir.Top = labelPhase.Top + offset;
        buttonFir.Top = numericPhase.Top + offset;
        labelFirInfo.Top = numericPhase.Top + offset;
    }

    private void ApplyCollapsedState(bool raiseChanged = true)
    {
        buttonCollapse.Text = collapsed ? "+" : "−";
        int keptBottom = 0;
        SuspendLayout();
        PlaceFirRow();
        foreach (Control child in Controls)
        {
            bool kept = (!collapsed || child.Top < FoldLine) &&
                (phaseControlShown || !IsPhaseRow(child)) &&
                (firControlShown || !IsFirRow(child));
            child.Visible = kept;
            if (kept)
            {
                keptBottom = Math.Max(keptBottom, child.Bottom);
            }
        }

        ResumeLayout(false);
        // The border is painted inside the client area, so end one designer margin below the lowest shown control,
        // measured off live children (rows scale and round independently at other DPIs).
        int height = keptBottom + bottomMargin;
        // One suspended parent layout: a list reflowed against a half-moved pin stacks the next block over this one.
        Control? parent = Parent;
        parent?.SuspendLayout();
        try
        {
            // Move the bound in the way first (Min == Max pins the block). Never through zero: the flow list reads it as no height.
            if (height < MinimumSize.Height)
            {
                MinimumSize = new Size(MinimumSize.Width, height);
                MaximumSize = new Size(MaximumSize.Width, height);
            }
            else
            {
                MaximumSize = new Size(MaximumSize.Width, height);
                MinimumSize = new Size(MinimumSize.Width, height);
            }

            Height = height;
        }
        finally
        {
            parent?.ResumeLayout(performLayout: true);
        }

        if (raiseChanged)
        {
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // The base does not scale the parked margin; follow only calls that actually scale height.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        if ((specified & BoundsSpecified.Height) != 0)
        {
            bottomMargin = (int)Math.Round(bottomMargin * factor.Height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RoundedSurface.Paint(
            this,
            e.Graphics,
            RoundedSurface.DefaultCornerRadius,
            UiPalette.Border);

        base.OnPaint(e);
    }

    // Runs through the collapse path: moving the size pin anywhere else races the flow list's reflow.
    private void ApplyOptionalRows()
    {
        // Without the fold event: the host would persist a fold state nobody asked for.
        ApplyCollapsedState(raiseChanged: false);
        UpdatePhaseReadout();
        UpdateFirReadout();
    }
}
