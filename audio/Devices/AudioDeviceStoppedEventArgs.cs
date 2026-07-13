namespace Resonalyze.Audio;

internal sealed class AudioDeviceStoppedEventArgs : EventArgs
{
    public AudioDeviceStoppedEventArgs(Exception? exception = null)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }
}
