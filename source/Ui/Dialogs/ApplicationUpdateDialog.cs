using Resonalyze.Ui;

namespace Resonalyze.Ui.Dialogs;

internal enum UpdatePromptChoice
{
    Cancel,
    Manual,
    Automatic
}

internal sealed class ApplicationUpdateDialog : Form
{
    private readonly bool supportsAutomaticUpdate;

    public ApplicationUpdateDialog(
        string currentVersion,
        string? latestVersion,
        bool supportsAutomaticUpdate)
    {
        this.supportsAutomaticUpdate = supportsAutomaticUpdate;
        InitializeDialog(currentVersion, latestVersion);
    }

    public new UpdatePromptChoice ShowDialog(IWin32Window? owner)
    {
        return base.ShowDialog(owner) switch
        {
            DialogResult.Yes => UpdatePromptChoice.Automatic,
            DialogResult.No => UpdatePromptChoice.Manual,
            _ => UpdatePromptChoice.Cancel
        };
    }

    private void InitializeDialog(
        string currentVersion,
        string? latestVersion)
    {
        SuspendLayout();

        UiStyle.ApplyDarkDialog(
            this,
            new Size(500, supportsAutomaticUpdate ? 235 : 215),
            title: "Update available",
            fixedDialog: true,
            padding: new Padding(20));

        var titleLabel = UiStyle.CreateLabel(
            "A new Resonalyze release is available.",
            new Point(20, 20),
            UiPalette.TextPrimary,
            new Font("Segoe UI", 12F, FontStyle.Bold));

        string versionLine = string.IsNullOrWhiteSpace(latestVersion)
            ? $"Current version: {currentVersion}"
            : $"Current version: {currentVersion}\r\nLatest version: v{latestVersion.TrimStart('v', 'V')}";
        var versionLabel = UiStyle.CreateLabel(
            versionLine,
            new Point(20, 58),
            UiPalette.TextSecondaryAlt,
            new Font("Segoe UI", 9.5F));

        string bodyText = supportsAutomaticUpdate
            ? "You can run the installer automatically from inside Resonalyze, or open the GitHub releases page and download it manually."
            : "This copy looks like a portable build, so automatic setup-based update is not available here. Open the GitHub releases page to download the latest installer manually.";
        var bodyLabel = UiStyle.CreateLabel(
            bodyText,
            new Point(20, 108),
            UiPalette.TextHighlight,
            new Font("Segoe UI", 9.5F),
            autoSize: false);
        bodyLabel.Size = new Size(460, supportsAutomaticUpdate ? 46 : 56);

        Button manualButton = UiStyle.CreateDialogButton(
            "Download Manually",
            DialogResult.No,
            accent: false,
            new Size(150, 32));
        manualButton.Location = new Point(180, supportsAutomaticUpdate ? 178 : 168);

        Button cancelButton = UiStyle.CreateDialogButton(
            "Not Now",
            DialogResult.Cancel,
            accent: false,
            new Size(100, 32));
        cancelButton.Location = new Point(340, supportsAutomaticUpdate ? 178 : 168);

        Controls.Add(titleLabel);
        Controls.Add(versionLabel);
        Controls.Add(bodyLabel);
        Controls.Add(manualButton);
        Controls.Add(cancelButton);

        if (supportsAutomaticUpdate)
        {
            Button automaticButton = UiStyle.CreateDialogButton(
                "Automatic Update",
                DialogResult.Yes,
                accent: true,
                new Size(150, 32));
            automaticButton.Location = new Point(20, 178);
            Controls.Add(automaticButton);
            AcceptButton = automaticButton;
        }
        else
        {
            AcceptButton = manualButton;
        }

        CancelButton = cancelButton;
        // Same runtime DPI scaling as the other hand-laid-out dialogs; without
        // it the fixed pixel geometry clips the scaled text at 150 %+.
        OverlayDialogControls.ApplyRuntimeDpiScale(this);
        ResumeLayout(false);
    }
}
