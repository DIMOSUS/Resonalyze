namespace Resonalyze;

public sealed class AudioDeviceStoppedEventArgs : EventArgs
{
    public AudioDeviceStoppedEventArgs(Exception? exception = null)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }
}
