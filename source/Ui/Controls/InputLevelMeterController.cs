namespace Resonalyze;

internal sealed class InputLevelMeterController : IDisposable
{
    private readonly Form owner;
    private readonly InputLevelMeterPanel panel;
    private readonly ExpSweepMeasurement sweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    // Coalesced to one queued UI update; folded rather than dropped, since the skipped window is often the loudest.
    private readonly CoalescingDispatcher<InputLevelMeterSnapshot> dispatcher;
    private bool disposed;

    public InputLevelMeterController(
        Form owner,
        InputLevelMeterPanel panel,
        ExpSweepMeasurement sweepMeasurement,
        NoiseMeasurement noiseMeasurement)
    {
        this.owner = owner;
        this.panel = panel;
        this.sweepMeasurement = sweepMeasurement;
        this.noiseMeasurement = noiseMeasurement;
        dispatcher = new CoalescingDispatcher<InputLevelMeterSnapshot>(
            TryPostToOwner,
            ApplyOnUiThread,
            static (superseded, newest) => superseded.Merge(newest));

        sweepMeasurement.LevelsAvailable += HandleLevels;
        noiseMeasurement.LevelsAvailable += HandleLevels;
    }

    /// <summary>A stored result's levels, as its capture left them.</summary>
    public void Show(InputLevelMeterSnapshot levels) => HandleLevels(levels);

    public void Clear()
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        try
        {
            owner.BeginInvoke((MethodInvoker)panel.ClearLevels);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sweepMeasurement.LevelsAvailable -= HandleLevels;
        noiseMeasurement.LevelsAvailable -= HandleLevels;
    }

    private void HandleLevels(InputLevelMeterSnapshot snapshot)
    {
        if (disposed)
        {
            return;
        }

        dispatcher.Offer(snapshot);
    }

    private bool TryPostToOwner(Action drain)
    {
        if (disposed || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return false;
        }

        try
        {
            owner.BeginInvoke(drain);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplyOnUiThread(InputLevelMeterSnapshot snapshot)
    {
        if (!disposed && !panel.IsDisposed)
        {
            panel.SetLevels(snapshot);
        }
    }
}
