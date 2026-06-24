namespace Resonalyze;

internal sealed class MainCommandController
{
    private readonly Button saveButton;
    private readonly Button loadButton;
    private readonly Button drawButton;
    private readonly Button clearButton;
    private readonly Button modeSettingsButton;
    private readonly Button recordSettingsButton;
    private readonly Func<bool> hasActiveModeSettings;
    private readonly Func<bool> supportsManualDraw;
    private readonly Func<bool> canDrawCurrentMeasurement;
    private readonly Func<bool> hasPlotCurves;
    private readonly Func<bool> isHandleCreated;
    private bool modeSettingsPressed;
    private bool recordSettingsPressed;

    public MainCommandController(
        Button saveButton,
        Button loadButton,
        Button drawButton,
        Button clearButton,
        Button modeSettingsButton,
        Button recordSettingsButton,
        Func<bool> hasActiveModeSettings,
        Func<bool> supportsManualDraw,
        Func<bool> canDrawCurrentMeasurement,
        Func<bool> hasPlotCurves,
        Func<bool> isHandleCreated)
    {
        this.saveButton = saveButton;
        this.loadButton = loadButton;
        this.drawButton = drawButton;
        this.clearButton = clearButton;
        this.modeSettingsButton = modeSettingsButton;
        this.recordSettingsButton = recordSettingsButton;
        this.hasActiveModeSettings = hasActiveModeSettings;
        this.supportsManualDraw = supportsManualDraw;
        this.canDrawCurrentMeasurement = canDrawCurrentMeasurement;
        this.hasPlotCurves = hasPlotCurves;
        this.isHandleCreated = isHandleCreated;
    }

    public bool IsDrawFrozen => ShouldFreezeDrawButton();

    public void Initialize()
    {
        SetSaveAvailable(false);
        SetLoadAvailable(true);
        SetButtonFrozen(drawButton, frozen: true);
        UpdateDrawButton();
    }

    public void SetSaveAvailable(bool available) =>
        SetButtonFrozen(saveButton, !available);

    public void SetLoadAvailable(bool available) =>
        SetButtonFrozen(loadButton, !available);

    public void FreezeSaveLoadDraw()
    {
        SetSaveAvailable(false);
        SetLoadAvailable(false);
        SetButtonFrozen(drawButton, frozen: true);
    }

    public void UpdateDrawButton()
    {
        if (!isHandleCreated())
        {
            return;
        }

        drawButton.Text = "Restore Curves";
        SetButtonFrozen(drawButton, ShouldFreezeDrawButton());
    }

    public void UpdateClearButton()
    {
        if (!isHandleCreated())
        {
            return;
        }

        SetButtonFrozen(clearButton, !hasPlotCurves());
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

    public static void SetButtonFrozen(Button button, bool frozen)
    {
        if (frozen)
        {
            button.Enabled = false;
            button.BackColor = UiPalette.ButtonDisabledBackground;
            button.ForeColor = UiPalette.TextMuted;
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

    private bool ShouldFreezeDrawButton()
    {
        if (!supportsManualDraw())
        {
            return true;
        }

        return !canDrawCurrentMeasurement();
    }
}
