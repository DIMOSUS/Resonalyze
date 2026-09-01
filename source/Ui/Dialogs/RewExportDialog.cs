namespace Resonalyze.Ui.Dialogs;

/// <summary>
/// The one dialog of the REW export: what the measurement will be called there, and
/// where REW is listening.
/// </summary>
/// <remarks>
/// It states the two things about the arriving copy that are not obvious and cannot
/// be read off it afterwards — what REW will and will not be able to say about its
/// time, and whether it arrives on an absolute level scale. The address stays
/// editable even when REW did not answer, because the setting that would fix that is
/// the one this dialog holds.
/// </remarks>
internal sealed class RewExportDialog : Form
{
    private readonly TextBox nameInput = new();
    private readonly TextBox addressInput = new();

    public RewExportDialog(
        string suggestedName,
        string baseUrl,
        string? rewVersion,
        double? splOffsetDb,
        TimingReference timingReference)
    {
        InitializeDialog(suggestedName, baseUrl, rewVersion, splOffsetDb, timingReference);
    }

    /// <summary>The name REW will file the measurement under.</summary>
    public string MeasurementName => nameInput.Text.Trim();

    /// <summary>The address the user settled on, whether or not it was changed.</summary>
    public string BaseUrl => addressInput.Text.Trim();

    private void InitializeDialog(
        string suggestedName,
        string baseUrl,
        string? rewVersion,
        double? splOffsetDb,
        TimingReference timingReference)
    {
        SuspendLayout();

        UiStyle.ApplyDarkDialog(
            this,
            new Size(560, 348),
            title: "Send to REW",
            fixedDialog: true,
            padding: new Padding(20));

        var titleLabel = UiStyle.CreateLabel(
            "Send this measurement to REW",
            new Point(20, 20),
            UiPalette.TextPrimary,
            new Font("Segoe UI", 12F, FontStyle.Bold));

        var bodyLabel = UiStyle.CreateLabel(
            "The loopback transfer function is imported over REW's API. Its samples go " +
            "unchanged; the buffer is rolled by a tenth of a second so the wrapped tail " +
            "appears before t = 0, where it belongs.\r\n\r\n" +
            DescribeLevel(splOffsetDb) + "\r\n\r\n" +
            DescribeTiming(timingReference),
            new Point(20, 58),
            UiPalette.TextHighlight,
            new Font("Segoe UI", 9.5F),
            autoSize: false);
        bodyLabel.Size = new Size(520, 112);

        var nameLabel = UiStyle.CreateLabel(
            "Name in REW:",
            new Point(20, 184),
            UiPalette.TextSecondaryAlt,
            new Font("Segoe UI", 9.5F));

        nameInput.Text = suggestedName;
        UiStyle.ApplyTextBox(nameInput, new Point(140, 181), new Size(380, 24));

        var addressLabel = UiStyle.CreateLabel(
            "REW address:",
            new Point(20, 220),
            UiPalette.TextSecondaryAlt,
            new Font("Segoe UI", 9.5F));

        addressInput.Text = baseUrl;
        UiStyle.ApplyTextBox(addressInput, new Point(140, 217), new Size(380, 24));

        var statusLabel = UiStyle.CreateLabel(
            rewVersion is { } version
                ? $"Answering: REW {version}"
                : "Not answering. Start REW and enable its API server in Preferences -> API.",
            new Point(140, 247),
            rewVersion == null ? UiPalette.WarningAmber : UiPalette.TextSecondary,
            new Font("Segoe UI", 9F),
            autoSize: false);
        statusLabel.Size = new Size(380, 32);

        Button cancelButton = UiStyle.CreateDialogButton(
            "Cancel",
            DialogResult.Cancel,
            accent: false,
            new Size(100, 32));
        cancelButton.Location = new Point(320, 280);

        Button sendButton = UiStyle.CreateDialogButton(
            "Send",
            DialogResult.OK,
            accent: true,
            new Size(100, 32));
        sendButton.Location = new Point(430, 280);

        Controls.AddRange(
            titleLabel,
            bodyLabel,
            nameLabel,
            nameInput,
            addressLabel,
            addressInput,
            statusLabel,
            cancelButton,
            sendButton);

        AcceptButton = sendButton;
        CancelButton = cancelButton;
        ResumeLayout(performLayout: false);
    }

    private static string DescribeLevel(double? splOffsetDb) =>
        splOffsetDb is { } offset
            ? FormattableString.Invariant(
                $"It carries this session's SPL anchor, so it arrives on an absolute scale ({offset:+0.0;-0.0;0.0} dB).")
            : "It has no SPL anchor, so no offset is sent. REW then supplies a default " +
                "of its own, which looks like a calibration and is not: the levels there " +
                "are relative.";

    /// <summary>
    /// What REW will be able to say about this measurement's time. Two separate
    /// losses, and a measurement whose own origin was chosen suffers both: REW gives
    /// an imported response no timing reference of its own, and a recorded sweep had
    /// none to give it.
    /// </summary>
    private static string DescribeTiming(TimingReference timingReference) =>
        timingReference == TimingReference.SynchronizedLoopback
            ? "REW files an imported response with no timing reference, so its arrival " +
                "cannot be compared with another REW measurement's. The delays inside it are real."
            : "This measurement's origin was chosen rather than measured, so its arrival " +
                "time is not a delay — in REW no more than here. The intervals inside the " +
                "response are real.";
}
