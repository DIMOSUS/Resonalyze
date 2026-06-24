namespace Resonalyze;

internal sealed class MainCommandController
{
    private readonly Button saveButton;
    private readonly Button loadButton;
    private readonly Button drawButton;
    private readonly Button clearButton;
    private readonly Button modeSettingsButton;
    private readonly Func<ModeTab> getActiveTab;
    private readonly Func<bool> canDrawCurrentMeasurement;
    private readonly Func<bool> hasPlotCurves;
    private readonly Func<bool> isHandleCreated;
    private bool modeSettingsPressed;

    public MainCommandController(
        Button saveButton,
        Button loadButton,
        Button drawButton,
        Button clearButton,
        Button modeSettingsButton,
        Func<ModeTab> getActiveTab,
        Func<bool> canDrawCurrentMeasurement,
        Func<bool> hasPlotCurves,
        Func<bool> isHandleCreated)
    {
        this.saveButton = saveButton;
        this.loadButton = loadButton;
        this.drawButton = drawButton;
        this.clearButton = clearButton;
        this.modeSettingsButton = modeSettingsButton;
        this.getActiveTab = getActiveTab;
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
        bool hasSettings = getActiveTab() is not (
            ModeTab.LiveSpectrum or
            ModeTab.Autocorrelation or
            ModeTab.TimeAlignment);

        if (!hasSettings)
        {
            SetButtonFrozen(modeSettingsButton, true);
            return;
        }

        SetButtonPressed(modeSettingsButton, modeSettingsPressed);
    }

    public static void SetButtonFrozen(Button button, bool frozen)
    {
        if (frozen)
        {
            button.Enabled = false;
            button.BackColor = Color.FromArgb(55, 60, 70);
            button.ForeColor = Color.FromArgb(120, 125, 135);
        }
        else
        {
            button.BackColor = Color.FromArgb(50, 55, 80);
            button.ForeColor = Color.White;
            button.Enabled = true;
        }
    }

    private static void SetButtonPressed(Button button, bool pressed)
    {
        button.Enabled = true;
        button.BackColor = pressed
            ? Color.FromArgb(40, 45, 68)
            : Color.FromArgb(50, 55, 80);
        button.ForeColor = Color.White;
        button.Padding = pressed ? new Padding(1, 1, 0, 0) : Padding.Empty;
    }

    private bool ShouldFreezeDrawButton()
    {
        if (getActiveTab() is ModeTab.LiveSpectrum or ModeTab.TimeAlignment)
        {
            return true;
        }

        return !canDrawCurrentMeasurement();
    }
}
