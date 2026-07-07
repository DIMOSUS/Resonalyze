namespace Resonalyze;

internal sealed class InputLevelMeterController : IDisposable
{
    private readonly Form owner;
    private readonly InputLevelMeterPanel panel;
    private readonly ExpSweepMeasurement sweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
    // Levels arrive on audio worker threads on every buffer, which outpaces the
    // message pump at small ASIO buffer sizes; coalesce to a single queued UI
    // update carrying the newest snapshot.
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
            ApplyOnUiThread);

        sweepMeasurement.LevelsAvailable += HandleLevels;
        noiseMeasurement.LevelsAvailable += HandleLevels;
    }

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
            // The handle was destroyed between the guard and the call.
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
