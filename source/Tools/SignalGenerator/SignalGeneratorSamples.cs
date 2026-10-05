namespace Resonalyze;

internal enum SignalGeneratorType
{
    PinkPeriodicNoise,
    PinkNoise,
    BrownNoise,
    WhiteNoise,
    Sine
}

/// <summary>The one channel the signal generator plays, exactly the requested length.</summary>
internal static class SignalGeneratorSamples
{
    public static float[] CreateMono(
        int sampleRate,
        int durationSeconds,
        SignalGeneratorType signalType,
        double frequencyHz,
        double level)
    {
        int samples = checked(sampleRate * durationSeconds);
        var result = new float[samples];
        level = Math.Clamp(level, 0.0, 1.0);

        if (signalType != SignalGeneratorType.Sine)
        {
            using var signal = new NoiseSignal();
            signal.FillData(
                durationSeconds,
                24,
                sampleRate,
                ToNoiseColor(signalType));
            // Periodic noise is a whole number of periods, rarely the requested length; repeating it has no seam.
            float[] noise = signal.FloatData;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (float)Math.Clamp(noise[i % noise.Length] * level * 2.0, -1.0, 1.0);
            }

            return result;
        }

        double angularStep = 2.0 * Math.PI * frequencyHz / sampleRate;
        for (int sampleIndex = 0; sampleIndex < result.Length; sampleIndex++)
        {
            result[sampleIndex] = (float)(Math.Sin(sampleIndex * angularStep) * level);
        }

        return result;
    }

    private static NoiseColor ToNoiseColor(SignalGeneratorType type) =>
        type switch
        {
            SignalGeneratorType.PinkNoise => NoiseColor.Pink,
            SignalGeneratorType.BrownNoise => NoiseColor.Brown,
            SignalGeneratorType.WhiteNoise => NoiseColor.White,
            _ => NoiseColor.PinkPeriodic
        };
}
