namespace Resonalyze
{
    /// <param name="FrameCount">Read under the lock that cloned the spectra, so a stored capture never claims more integration than it holds.</param>
    public sealed record LiveSpectrumSnapshot(
        double[] Magnitude,
        double[]? Coherence,
        double[]? InputMagnitude = null,
        int FrameCount = 0);
}
