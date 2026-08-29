namespace Resonalyze;

/// <summary>
/// The picker behind the Virtual DSP "L→R" / "R→L" commands: which pairs have
/// their settings copied from one side onto the other, and which parts of the
/// chain travel with them. Mono pairs never appear here — they have a single
/// settings set by definition. Sources are never copied: each side picks its
/// own measurement.
/// <para>
/// The default selection is the crossover and the PEQ — the magnitude shape,
/// which describes the driver. Everything that aligns one side against its own
/// level and geometry is offered too but starts off: gain, delay, polarity and
/// the all-pass. The all-pass lives inside the PEQ bank as bands now, but it
/// still belongs with the alignment scopes rather than with the filters, because
/// it exists exactly where a delay and a polarity flip are too blunt — it is
/// tuned against that side's own junction, and a left tweeter's arrival is not a
/// right tweeter's. So the two checkboxes split one band list by type: PEQ moves
/// the gain-bearing bands, All-pass the phase-only ones, and whichever kind is
/// not copied survives on the target side.
/// </para>
/// </summary>
internal sealed class VirtualCrossoverCopySideDialog : Form
{
    private readonly List<CheckBox> channelBoxes = new();
    private readonly CheckBox gainBox = CreateScopeBox("Gain", checkedByDefault: false);
    private readonly CheckBox delayBox = CreateScopeBox("Delay", checkedByDefault: false);
    private readonly CheckBox invertBox = CreateScopeBox("Invert", checkedByDefault: false);
    private readonly CheckBox crossoverBox = CreateScopeBox("Crossover", checkedByDefault: true);
    private readonly CheckBox allPassBox = CreateScopeBox("All-pass", checkedByDefault: false);
    private readonly CheckBox peqBox = CreateScopeBox("PEQ", checkedByDefault: true);
    private readonly Button copyButton =
        UiStyle.CreateDialogButton("Copy", DialogResult.OK, accent: true);

    public VirtualCrossoverCopySideDialog(
        bool fromRightToLeft,
        IReadOnlyList<string> channelLabels)
    {
        ArgumentNullException.ThrowIfNull(channelLabels);

        SuspendLayout();
        UiStyle.ApplyDarkDialog(
            this,
            new Size(340, 300),
            fromRightToLeft ? "Copy R → L" : "Copy L → R");
        // The channel list is as long as the project has stereo pairs, so the
        // dialog sizes itself around the finished layout instead of around
        // hand-computed 96-DPI coordinates that a scaled font would overrun.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiPalette.TextHighlight,
            Margin = new Padding(0, 0, 0, 12),
            Text = "Pick the channels, then the parts of the chain to copy.\n" +
                "Sources always stay with their side; gain, delay, polarity and\n" +
                "the all-pass are tuned per side, so they start off."
        });

        layout.Controls.Add(CreateSectionLabel("Channels"));
        var channelList = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(4, 0, 0, 12),
            WrapContents = false
        };
        foreach (string label in channelLabels)
        {
            var box = new ReleaseClickCheckBox
            {
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 2, 0, 2),
                Text = label
            };
            box.CheckedChanged += (_, _) => UpdateCopyEnabled();
            channelBoxes.Add(box);
            channelList.Controls.Add(box);
        }

        layout.Controls.Add(channelList);

        layout.Controls.Add(CreateSectionLabel("What to copy"));
        // Two columns that read as the split they are: what aligns this side
        // (left, off) against what shapes the driver (right, on).
        var scopeTable = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = new Padding(4, 0, 0, 12)
        };
        scopeTable.Controls.Add(gainBox, 0, 0);
        scopeTable.Controls.Add(crossoverBox, 1, 0);
        scopeTable.Controls.Add(delayBox, 0, 1);
        scopeTable.Controls.Add(peqBox, 1, 1);
        scopeTable.Controls.Add(invertBox, 0, 2);
        scopeTable.Controls.Add(allPassBox, 0, 3);
        foreach (CheckBox box in ScopeBoxes)
        {
            box.CheckedChanged += (_, _) => UpdateCopyEnabled();
        }

        layout.Controls.Add(scopeTable);

        var buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0),
            WrapContents = false
        };
        Button cancelButton = UiStyle.CreateDialogButton(
            "Cancel",
            DialogResult.Cancel,
            accent: false);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(copyButton);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = copyButton;
        CancelButton = cancelButton;
        UpdateCopyEnabled();
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>Indices (into the constructor's label list) the user left checked.</summary>
    public IReadOnlyList<int> SelectedIndices => channelBoxes
        .Select((box, index) => (box.Checked, index))
        .Where(item => item.Checked)
        .Select(item => item.index)
        .ToList();

    /// <summary>The parts of the chain the user asked to carry over.</summary>
    public VirtualCrossoverCopyScope Scope => new(
        Gain: gainBox.Checked,
        Delay: delayBox.Checked,
        InvertPolarity: invertBox.Checked,
        Crossover: crossoverBox.Checked,
        AllPass: allPassBox.Checked,
        Peq: peqBox.Checked);

    private IEnumerable<CheckBox> ScopeBoxes =>
        [gainBox, delayBox, invertBox, crossoverBox, allPassBox, peqBox];

    private static CheckBox CreateScopeBox(string text, bool checkedByDefault)
    {
        return new ReleaseClickCheckBox
        {
            AutoSize = true,
            Checked = checkedByDefault,
            Margin = new Padding(0, 2, 20, 2),
            Text = text
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = UiPalette.TextSecondary,
            Margin = new Padding(0, 0, 0, 6),
            Text = text
        };
    }

    // A copy with no channel or no part selected would do nothing at all, which
    // is worth saying with the button rather than with a silent no-op.
    private void UpdateCopyEnabled()
    {
        copyButton.Enabled = channelBoxes.Exists(box => box.Checked) && !Scope.IsEmpty;
    }
}

/// <summary>
/// Which parts of a channel's chain a side-to-side copy carries. Everything the
/// user did not tick is left as the target side had it.
/// </summary>
internal readonly record struct VirtualCrossoverCopyScope(
    bool Gain,
    bool Delay,
    bool InvertPolarity,
    bool Crossover,
    bool AllPass,
    bool Peq)
{
    /// <summary>True when the copy would carry nothing.</summary>
    public bool IsEmpty =>
        !Gain && !Delay && !InvertPolarity && !Crossover && !AllPass && !Peq;
}
