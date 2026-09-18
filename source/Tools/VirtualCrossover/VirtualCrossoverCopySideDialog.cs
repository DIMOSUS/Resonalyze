namespace Resonalyze;

/// <summary>Picker for the L-to-R / R-to-L copy. Defaults to crossover + PEQ (driver shape); alignment scopes (gain, delay, polarity,
/// all-pass, phase) start off because they are tuned against each side's own geometry. PEQ and All-pass split one band list by type.</summary>
internal sealed class VirtualCrossoverCopySideDialog : Form
{
    private readonly List<CheckBox> channelBoxes = new();
    private readonly CheckBox gainBox = CreateScopeBox("Gain", checkedByDefault: false);
    private readonly CheckBox delayBox = CreateScopeBox("Delay", checkedByDefault: false);
    private readonly CheckBox invertBox = CreateScopeBox("Invert", checkedByDefault: false);
    private readonly CheckBox crossoverBox = CreateScopeBox("Crossover", checkedByDefault: true);
    private readonly CheckBox allPassBox = CreateScopeBox("All-pass", checkedByDefault: false);
    private readonly CheckBox phaseBox = CreateScopeBox("Phase", checkedByDefault: false);
    private readonly CheckBox peqBox = CreateScopeBox("PEQ", checkedByDefault: true);
    // Off by default: a kernel is usually designed against one side's response.
    private readonly CheckBox firBox = CreateScopeBox("FIR", checkedByDefault: false);
    private readonly Button copyButton =
        UiStyle.CreateDialogButton("Copy", DialogResult.OK, accent: true);

    public VirtualCrossoverCopySideDialog(
        bool fromRightToLeft,
        IReadOnlyList<string> channelLabels)
    {
        ArgumentNullException.ThrowIfNull(channelLabels);

        SuspendLayout();
        UiStyle.ApplyDialogChrome(
            this,
            new Size(340, 300),
            fromRightToLeft ? "Copy R → L" : "Copy L → R");
        // AutoSize: the list length varies, and hand-computed 96-DPI coordinates would be overrun by a scaled font.
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
            ForeColor = UiPalette.TextDefault,
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
        scopeTable.Controls.Add(firBox, 1, 2);
        scopeTable.Controls.Add(allPassBox, 0, 3);
        scopeTable.Controls.Add(phaseBox, 0, 4);
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

    public IReadOnlyList<int> SelectedIndices => channelBoxes
        .Select((box, index) => (box.Checked, index))
        .Where(item => item.Checked)
        .Select(item => item.index)
        .ToList();

    public VirtualCrossoverCopyScope Scope => new(
        Gain: gainBox.Checked,
        Delay: delayBox.Checked,
        InvertPolarity: invertBox.Checked,
        Crossover: crossoverBox.Checked,
        AllPass: allPassBox.Checked,
        Phase: phaseBox.Checked,
        Peq: peqBox.Checked,
        Fir: firBox.Checked);

    private IEnumerable<CheckBox> ScopeBoxes =>
        [gainBox, delayBox, invertBox, crossoverBox, allPassBox, phaseBox, peqBox, firBox];

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

    private void UpdateCopyEnabled()
    {
        copyButton.Enabled = channelBoxes.Exists(box => box.Checked) && !Scope.IsEmpty;
    }
}
