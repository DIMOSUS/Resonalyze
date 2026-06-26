namespace Resonalyze
{
    /// <summary>
    /// A consistent display snapshot of the live analyzer state. Magnitude is a
    /// linear amplitude curve (one value per FFT bin). Coherence is the
    /// magnitude-squared coherence γ² in [0, 1] and is only present in
    /// transfer-function mode.
    /// </summary>
    public sealed record LiveSpectrumSnapshot(
        double[] Magnitude,
        double[]? Coherence);
}
