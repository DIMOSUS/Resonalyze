namespace Resonalyze
{
    /// <summary>Magnitude: linear H1 transfer (empty mic-only); Coherence: transfer mode only; InputMagnitude: reference-free mic spectrum.</summary>
    /// <param name="FrameCount">Read under the lock that cloned the spectra, so a stored capture never claims more integration than it holds.</param>
    public sealed record LiveSpectrumSnapshot(
        double[] Magnitude,
        double[]? Coherence,
        double[]? InputMagnitude = null,
        int FrameCount = 0);
}
