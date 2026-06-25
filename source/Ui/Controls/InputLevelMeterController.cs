namespace Resonalyze;

internal sealed class InputLevelMeterController : IDisposable
{
    private readonly Form owner;
    private readonly InputLevelMeterPanel panel;
    private readonly ExpSweepMeasurement sweepMeasurement;
    private readonly NoiseMeasurement noiseMeasurement;
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

        sweepMeasurement.LevelsAvailable += ApplyLevels;
        noiseMeasurement.LevelsAvailable += ApplyLevels;
    }

    public void Clear()
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        owner.BeginInvoke((MethodInvoker)panel.ClearLevels);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sweepMeasurement.LevelsAvailable -= ApplyLevels;
        noiseMeasurement.LevelsAvailable -= ApplyLevels;
    }

    private void ApplyLevels(InputLevelMeterSnapshot snapshot)
    {
        if (disposed || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        owner.BeginInvoke((MethodInvoker)(() => panel.SetLevels(snapshot)));
    }
}
