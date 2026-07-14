namespace Resonalyze.Audio;

internal static class EventPublisher
{
    public static void Publish(Action? handlers)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // Notifications are observational. A broken UI or third-party
                // subscriber must not terminate capture or invalidate samples.
            }
        }
    }

    public static void Publish<T>(Action<T>? handlers, T value)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch
            {
                // Notifications are observational. A broken UI or third-party
                // subscriber must not terminate capture or invalidate samples.
            }
        }
    }
}
