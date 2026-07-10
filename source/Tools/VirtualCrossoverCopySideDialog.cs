namespace Resonalyze;

/// <summary>
/// The channel picker behind the Virtual DSP "L→R" / "R→L" commands: which
/// pairs have their DSP settings (gain, crossover, PEQ) copied from one side
/// onto the other. Mono pairs never appear here — they have a single settings
/// set by definition. Sources and delays are deliberately not copied: each
/// side has its own measurement and its own arrival timing.
/// </summary>
internal sealed class VirtualCrossoverCopySideDialog : Form
{
    private readonly List<CheckBox> channelBoxes = new();

    public VirtualCrossoverCopySideDialog(
        bool fromRightToLeft,
        IReadOnlyList<string> channelLabels)
    {
        ArgumentNullException.ThrowIfNull(channelLabels);

        SuspendLayout();
        Text = fromRightToLeft ? "Copy R → L" : "Copy L → R";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(40, 44, 54);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        var caption = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 214, 222),
            Location = new Point(12, 12),
            Text = "Copy gain, crossover and PEQ for the selected channels.\n" +
                "Sources and delays stay with their side."
        };
        Controls.Add(caption);

        int y = 52;
        foreach (string label in channelLabels)
        {
            var box = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Location = new Point(16, y),
                Text = label
            };
            channelBoxes.Add(box);
            Controls.Add(box);
            y += 25;
        }

        var okButton = new Button
        {
            BackColor = Color.FromArgb(46, 51, 67),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Popup,
            Location = new Point(160, y + 10),
            Size = new Size(80, 26),
            Text = "Copy"
        };
        var cancelButton = new Button
        {
            BackColor = Color.FromArgb(46, 51, 67),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Popup,
            Location = new Point(248, y + 10),
            Size = new Size(80, 26),
            Text = "Cancel"
        };
        Controls.Add(okButton);
        Controls.Add(cancelButton);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        ClientSize = new Size(340, y + 46);
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>Indices (into the constructor's label list) the user left checked.</summary>
    public IReadOnlyList<int> SelectedIndices => channelBoxes
        .Select((box, index) => (box.Checked, index))
        .Where(item => item.Checked)
        .Select(item => item.index)
        .ToList();
}
