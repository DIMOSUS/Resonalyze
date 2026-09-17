namespace Resonalyze;

/// <summary>Application-wide settings, reached from the title bar. Everything mode-specific lives in its own
/// settings panel; this is for what the whole window obeys.</summary>
internal sealed partial class ApplicationSettingsDialog : Form
{
    private readonly UiTheme themeOnOpen;

    public ApplicationSettingsDialog(AppearanceSettingsFile appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        InitializeComponent();

        Appearance = appearance;
        themeOnOpen = appearance.Theme;
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
            UiTheme chosen = radioThemeLight.Checked ? UiTheme.Light : UiTheme.Dark;
            if (chosen != themeOnOpen)
            {
                Appearance.Theme = chosen;
                Appearance.Save();
                RestartRequested = AskToRestart();
            }
        }

        base.OnFormClosing(e);
    }

    // The palette is read while every control is built, so the theme in force is decided once, at startup.
    private static bool AskToRestart() =>
        MessageBox.Show(
            "The theme is applied when Resonalyze starts.\r\n\r\nRestart now?",
            "Resonalyze",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;
}
