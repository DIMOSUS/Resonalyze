using Resonalyze.Ui;

namespace Resonalyze.Ui.Dialogs;

internal enum RewTimingOffsetChoice
{
    Cancel,

    Stated,

    /// <summary>Imports the shape without claiming the measurement's position.</summary>
    Unknown
}

/// <summary>Asks the timing offset REW's text export cannot carry; "I do not know" is a valid outcome, not a cancel.</summary>
internal sealed class RewTimingOffsetDialog : Form
{
    private readonly ThemedNumericUpDown offsetInput = new();

    public RewTimingOffsetDialog(double impliedArrivalMs)
    {
        InitializeDialog(impliedArrivalMs);
    }

    public double OffsetSeconds => (double)offsetInput.Value / 1000.0;

    public new RewTimingOffsetChoice ShowDialog(IWin32Window? owner)
    {
        return base.ShowDialog(owner) switch
        {
            DialogResult.OK => RewTimingOffsetChoice.Stated,
            DialogResult.No => RewTimingOffsetChoice.Unknown,
            _ => RewTimingOffsetChoice.Cancel
        };
    }

    private void InitializeDialog(double impliedArrivalMs)
    {
        SuspendLayout();

        UiStyle.ApplyDialogChrome(
            this,
            new Size(560, 300),
            title: "REW timing offset",
            fixedDialog: true,
            padding: new Padding(20));

        var titleLabel = UiStyle.CreateLabel(
            "What timing offset was REW measuring with?",
            new Point(20, 20),
            UiPalette.TextPrimary,
            new Font("Segoe UI", 12F, FontStyle.Bold));

        var bodyLabel = UiStyle.CreateLabel(
            "REW folds its timing offset into the export's start time and records it " +
            "nowhere else, so this file cannot be asked. Stating it here takes it back " +
            "out and places the measurement on this session's time base, where its " +
            "arrival can be compared with every other measurement.\r\n\r\n" +
            "Most measurements were taken with no offset: leave it at 0.",
            new Point(20, 58),
            UiPalette.TextDefault,
            new Font("Segoe UI", 9.5F),
            autoSize: false);
        bodyLabel.Size = new Size(520, 82);

        var offsetLabel = UiStyle.CreateLabel(
            "Timing offset (ms):",
            new Point(20, 152),
            UiPalette.TextDefault,
            new Font("Segoe UI", 9.5F));

        offsetInput.BeginInit();
        offsetInput.DecimalPlaces = 4;
        offsetInput.Increment = 0.1M;
        offsetInput.Minimum = -1000M;
        offsetInput.Maximum = 1000M;
        offsetInput.Value = 0M;
        offsetInput.Location = new Point(160, 149);
        offsetInput.Size = new Size(110, 26);
        offsetInput.EndInit();

        var arrivalLabel = UiStyle.CreateLabel(
            FormattableString.Invariant(
                $"Without an offset this file's arrival would be {impliedArrivalMs:0.###} ms. If that is not what REW showed, the difference is the offset."),
            new Point(20, 184),
            UiPalette.TextSecondary,
            new Font("Segoe UI", 9F),
            autoSize: false);
        arrivalLabel.Size = new Size(520, 34);

        Button unknownButton = UiStyle.CreateDialogButton(
            "I don't know",
            DialogResult.No,
            accent: false,
            new Size(120, 32));
        unknownButton.Location = new Point(20, 232);

        Button cancelButton = UiStyle.CreateDialogButton(
            "Cancel",
            DialogResult.Cancel,
            accent: false,
            new Size(100, 32));
        cancelButton.Location = new Point(320, 232);

        Button importButton = UiStyle.CreateDialogButton(
            "Import",
            DialogResult.OK,
            accent: true,
            new Size(100, 32));
        importButton.Location = new Point(430, 232);

        Controls.AddRange(
            titleLabel,
            bodyLabel,
            offsetLabel,
            offsetInput,
            arrivalLabel,
            unknownButton,
            cancelButton,
            importButton);

        AcceptButton = importButton;
        CancelButton = cancelButton;
        ResumeLayout(performLayout: false);
    }
}
