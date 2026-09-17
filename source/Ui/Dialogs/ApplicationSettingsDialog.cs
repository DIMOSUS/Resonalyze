namespace Resonalyze;

/// <summary>Application-wide settings, reached from the title bar. Everything mode-specific lives in its own
/// settings panel; this is for what the whole window obeys.</summary>
internal sealed partial class ApplicationSettingsDialog : Form
{
    private readonly UiTheme persistedTheme;

    public ApplicationSettingsDialog(AppearanceSettingsFile appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        InitializeComponent();

        Appearance = appearance;
        persistedTheme = appearance.Theme;
        radioThemeLight.Checked = appearance.Theme == UiTheme.Light;
        radioThemeDark.Checked = !radioThemeLight.Checked;

        AcceptButton = buttonOk;
        CancelButton = buttonCancel;
    }

    public AppearanceSettingsFile Appearance { get; }

    /// <summary>True once the user has confirmed a theme that only the next start can show.</summary>
    public bool RestartRequested { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (DialogResult == DialogResult.OK)
        {
            Apply(radioThemeLight.Checked ? UiTheme.Light : UiTheme.Dark);
        }

        base.OnFormClosing(e);
    }

    // What is on disk and what is running can differ: a theme chosen and then not restarted into leaves the file
    // ahead of the window. Saving answers the first, offering a restart answers the second.
    private void Apply(UiTheme chosen)
    {
        if (chosen != persistedTheme)
        {
            Appearance.Theme = chosen;
            if (!Appearance.TrySave())
            {
                // The file still holds the old theme, so a restart would land back in it.
                MessageBox.Show(
                    Appearance.SaveWarning,
                    "Resonalyze",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        if (chosen != UiPalette.Theme)
        {
            RestartRequested = AskToRestart();
        }
    }

    // The palette is read while every control is built, so the theme in force is decided once, at startup.
    private static bool AskToRestart() =>
        MessageBox.Show(
            "The theme is applied when Resonalyze starts.\r\n\r\nRestart now?",
            "Resonalyze",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;
}
