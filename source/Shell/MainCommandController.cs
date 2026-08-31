namespace Resonalyze;

internal sealed class MainCommandController
{
    private readonly Button saveButton;
    private readonly Button loadButton;
    private readonly Button rewExportButton;
    private readonly Button modeSettingsButton;
    private readonly Button recordSettingsButton;
    private readonly Button historyButton;
    private readonly Func<bool> hasActiveModeSettings;
    private readonly Func<bool> canExportToRew;
    private readonly Func<bool> isHandleCreated;
    private bool modeSettingsPressed;
    private bool recordSettingsPressed;
    private bool historyPressed;

    public MainCommandController(
        Button saveButton,
        Button loadButton,
        Button rewExportButton,
        Button modeSettingsButton,
        Button recordSettingsButton,
        Button historyButton,
        Func<bool> hasActiveModeSettings,
        Func<bool> canExportToRew,
        Func<bool> isHandleCreated)
    {
        this.saveButton = saveButton;
        this.loadButton = loadButton;
        this.rewExportButton = rewExportButton;
        this.modeSettingsButton = modeSettingsButton;
        this.recordSettingsButton = recordSettingsButton;
        this.historyButton = historyButton;
        this.hasActiveModeSettings = hasActiveModeSettings;
        this.canExportToRew = canExportToRew;
        this.isHandleCreated = isHandleCreated;
    }

    public void Initialize()
    {
        SetSaveAvailable(false);
        SetLoadAvailable(true);
    }

    /// <summary>
    /// Save, and with it the REW export, which is offered exactly when Save is —
    /// narrowed by what only the export cares about (a transfer response, a run that
    /// has finished, and not MMM, whose Save belongs to that mode's own capture).
    /// </summary>
    /// <remarks>
    /// Both are set here rather than beside each caller because there are eight
    /// callers: a button that has to track another one cannot be left to each of them
    /// to remember. Silently doing nothing was tolerable while the export was a hidden
    /// right-click; for a visible button it would be a defect.
    /// </remarks>
    public void SetSaveAvailable(bool available)
    {
        SetButtonFrozen(saveButton, !available);
        SetButtonFrozen(rewExportButton, !(available && canExportToRew()));
    }

    public void SetLoadAvailable(bool available) =>
        SetButtonFrozen(loadButton, !available);

    public void FreezeSaveLoad()
    {
        SetSaveAvailable(false);
        SetLoadAvailable(false);
    }

    public void UpdateModeSettingsButton(bool pressed = false)
    {
        if (!isHandleCreated())
        {
            return;
        }

        modeSettingsPressed = pressed;
        bool hasSettings = hasActiveModeSettings();

        if (!hasSettings)
        {
            SetButtonFrozen(modeSettingsButton, true);
            return;
        }

        SetButtonPressed(modeSettingsButton, modeSettingsPressed);
    }

    public void UpdateRecordSettingsButton(bool pressed = false)
    {
        if (!isHandleCreated())
        {
            return;
        }

        recordSettingsPressed = pressed;
        SetButtonPressed(recordSettingsButton, recordSettingsPressed);
    }

    public void UpdateHistoryButton(bool pressed = false)
    {
        if (!isHandleCreated())
        {
            return;
        }

        historyPressed = pressed;
        SetButtonPressed(historyButton, historyPressed);
    }

    public static void SetButtonFrozen(Button button, bool frozen)
    {
        if (frozen)
        {
            button.Enabled = false;
            button.BackColor = UiPalette.ButtonDisabledBackground;
            button.ForeColor = UiPalette.TextDisabled;
        }
        else
        {
            button.BackColor = UiPalette.ButtonBackground;
            button.ForeColor = Color.White;
            button.Enabled = true;
        }
    }

    public static void SetButtonPressed(Button button, bool pressed)
    {
        button.Enabled = true;
        button.BackColor = pressed
            ? UiPalette.ButtonPressedBackground
            : UiPalette.ButtonBackground;
        button.ForeColor = Color.White;
        button.Padding = pressed ? new Padding(1, 1, 0, 0) : Padding.Empty;
    }
}
