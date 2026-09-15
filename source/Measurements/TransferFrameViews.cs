using System.Collections;

namespace Resonalyze;

/// <summary>Float samples as doubles without a copy (a copy would be 157 MB at the longest sweep); converted once into the FFT buffer.</summary>
internal sealed class RecordedSamplesView(float[] samples) : IReadOnlyList<double>
{
    public double this[int index] => samples[index];

    public int Count => samples.Length;

    public IEnumerator<double> GetEnumerator()
    {
        foreach (float sample in samples)
        {
            yield return sample;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Sweep followed by virtual silence to the window length.</summary>
internal sealed class PaddedExcitationView(float[] excitation, int length)
    : IReadOnlyList<double>
{
    public double this[int index] =>
        index < excitation.Length ? excitation[index] : 0.0;

    public int Count => length;

    public IEnumerator<double> GetEnumerator()
    {
        for (int i = 0; i < length; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
