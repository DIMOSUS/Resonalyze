namespace Resonalyze;

internal sealed class ModeController
{
    private readonly Func<Mode, Task> changeModeAsync;
    private readonly Action<ModeTab> activeTabChanged;
    private readonly Action<bool> drawSelectedMode;
    private readonly Action restoreActiveOverlays;
    private readonly Func<bool> canDrawCurrentMeasurement;
    private readonly Func<ModeTab, Mode> getMode;
    private readonly Func<ModeTab, bool> supportsCurveDrawing;

    public ModeController(
        Func<Mode, Task> changeModeAsync,
        Action<ModeTab> activeTabChanged,
        Action<bool> drawSelectedMode,
        Action restoreActiveOverlays,
        Func<bool> canDrawCurrentMeasurement,
        Func<ModeTab, Mode> getMode,
        Func<ModeTab, bool> supportsCurveDrawing)
    {
        this.changeModeAsync = changeModeAsync;
        this.activeTabChanged = activeTabChanged;
        this.drawSelectedMode = drawSelectedMode;
        this.restoreActiveOverlays = restoreActiveOverlays;
        this.canDrawCurrentMeasurement = canDrawCurrentMeasurement;
        this.getMode = getMode;
        this.supportsCurveDrawing = supportsCurveDrawing;
    }

    public ModeTab ActiveTab { get; private set; } = ModeTab.Frequency;

    private Task selectChain = Task.CompletedTask;

    // Switches queue: interleaving with a slow switch leaves ActiveTab and Form1.CurrentMode disagreeing.
    public Task SelectAsync(ModeTab tab)
    {
        Task current = SelectAfterAsync(selectChain, tab);
        selectChain = current;
        return current;
    }

    private async Task SelectAfterAsync(Task previous, ModeTab tab)
    {
        try
        {
            await previous;
        }
        catch
        {
        }

        await changeModeAsync(getMode(tab));
        ActiveTab = tab;
        activeTabChanged(tab);

        bool includeCurves = supportsCurveDrawing(tab) && canDrawCurrentMeasurement();
        drawSelectedMode(includeCurves);
        restoreActiveOverlays();
    }
}
