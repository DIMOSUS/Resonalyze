namespace Resonalyze;

internal static class SignalFade
{
    /// <summary>Raised-cosine fades in place; an abrupt edge is an audible, tweeter-unfriendly click.</summary>
    public static void ApplyFadeInOut(float[] samples, int fadeSamples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (fadeSamples <= 0 || samples.Length < 2)
        {
            return;
        }

        int fade = Math.Min(fadeSamples, samples.Length / 2);
        for (int i = 0; i < fade; i++)
        {
            float gain = 0.5f * (1.0f - (float)Math.Cos(Math.PI * i / fade));
            samples[i] *= gain;
            samples[samples.Length - 1 - i] *= gain;
        }
    }
}
