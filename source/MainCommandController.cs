namespace Resonalyze;

internal sealed class MainCommandController
{
    private readonly Button saveButton;
    private readonly Button loadButton;
    private readonly Button drawButton;
    private readonly Button clearButton;
    private readonly Button modeSettingsButton;
    private readonly Func<ModeTab> getActiveTab;
    private readonly Func<bool> isLiveSpectrumRunning;
    private readonly Func<bool> canDrawCurrentMeasurement;
    private readonly Func<bool> hasPlotCurves;
    private readonly Func<bool> isHandleCreated;

    public MainCommandController(
        Button saveButton,
        Button loadButton,
        Button drawButton,
        Button clearButton,
        Button modeSettingsButton,
        Func<ModeTab> getActiveTab,
        Func<bool> isLiveSpectrumRunning,
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
        this.isLiveSpectrumRunning = isLiveSpectrumRunning;
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

        drawButton.Text = getActiveTab() == ModeTab.LiveSpectrum
            ? isLiveSpectrumRunning() ? "Stop Live" : "Start Live"
            : "Restore Curves";
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

    public void UpdateModeSettingsButton()
    {
        if (!isHandleCreated())
        {
            return;
        }

        bool hasSettings = getActiveTab() is not (
            ModeTab.LiveSpectrum or
            ModeTab.Autocorrelation or
            ModeTab.TimeAlignment);

        SetButtonFrozen(modeSettingsButton, !hasSettings);
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

    private bool ShouldFreezeDrawButton()
    {
        if (getActiveTab() == ModeTab.TimeAlignment)
        {
            return true;
        }

        if (getActiveTab() == ModeTab.LiveSpectrum)
        {
            return false;
        }

        return !canDrawCurrentMeasurement();
    }
}
