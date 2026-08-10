using System.Collections;

namespace Resonalyze;

/// <summary>
/// Captured samples presented as the <see cref="double"/> sequence the transfer
/// estimator reads, without the copy that converting them would cost.
/// <para>
/// The estimator fills its own <c>Complex[]</c> FFT buffers from whatever it is
/// handed, so materializing a <c>double[]</c> first only adds a full second copy
/// of the signal beside the spectra — 157 MB at the longest supported sweep, on
/// top of the buffers that dominate. Reading through this view converts exactly
/// once, while the FFT buffer is filled, which is what the DSP layer's precision
/// rule asks for.
/// </para>
/// </summary>
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

/// <summary>
/// The excitation as the estimator's reference channel: the sweep at the start,
/// silence for the rest of the analyzed window. Also a view rather than a buffer
/// — the padding is a length, not megabytes of zeros.
/// </summary>
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
