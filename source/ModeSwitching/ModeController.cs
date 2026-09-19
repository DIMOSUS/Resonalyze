namespace Resonalyze;

internal sealed class ModeController
{
    private readonly Func<Mode, Task> changeModeAsync;
    private readonly Action<ModeTab> activeTabChanged;
    private readonly Action<bool> drawSelectedMode;
    private readonly Action restoreActiveOverlays;
    private readonly Func<bool> canDrawCurrentMeasurement;

    public ModeController(
        Func<Mode, Task> changeModeAsync,
        Action<ModeTab> activeTabChanged,
        Action<bool> drawSelectedMode,
        Action restoreActiveOverlays,
        Func<bool> canDrawCurrentMeasurement)
    {
        this.changeModeAsync = changeModeAsync;
        this.activeTabChanged = activeTabChanged;
        this.drawSelectedMode = drawSelectedMode;
        this.restoreActiveOverlays = restoreActiveOverlays;
        this.canDrawCurrentMeasurement = canDrawCurrentMeasurement;
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

        ModeDescriptor descriptor = ModeCatalog.For(tab);
        await changeModeAsync(descriptor.Mode);
        ActiveTab = tab;
        activeTabChanged(tab);

        bool includeCurves = descriptor.SupportsCurveDrawing && canDrawCurrentMeasurement();
        drawSelectedMode(includeCurves);
        restoreActiveOverlays();
    }
}
