namespace Resonalyze;

/// <summary>What shows a mode: the main plot, which draws the open measurement itself.</summary>
internal interface IModeView
{
    /// <summary>Remembers what the mode being left had on screen (its overlay slots).</summary>
    void Leave();

    /// <summary>Takes the new mode, empty.</summary>
    void Enter(ModeDescriptor mode);

    /// <summary>Draws the entered mode from the open measurement and brings back what it had on screen.</summary>
    void Present();
}

/// <summary>Switches tabs one at a time: the view leaves, running work stops, the view enters, the window lays out, the view draws.</summary>
internal sealed class ModeController
{
    private readonly IModeView view;
    private readonly Func<Task> stopRunningAsync;
    private readonly Action<ModeDescriptor> showSurfaces;
    private Task selectChain = Task.CompletedTask;

    /// <param name="stopRunningAsync">Stops what the mode being left runs (a sweep, a live capture).</param>
    /// <param name="showSurfaces">Shows the new tab's panels and buttons, before the view draws.</param>
    public ModeController(IModeView view, Func<Task> stopRunningAsync, Action<ModeDescriptor> showSurfaces)
    {
        this.view = view;
        this.stopRunningAsync = stopRunningAsync;
        this.showSurfaces = showSurfaces;
    }

    public ModeTab ActiveTab { get; private set; } = ModeTab.Frequency;

    // Switches queue: interleaving with a slow switch leaves ActiveTab and the view's mode disagreeing.
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
        view.Leave();
        await stopRunningAsync();
        view.Enter(descriptor);
        ActiveTab = tab;
        showSurfaces(descriptor);
        view.Present();
    }
}
