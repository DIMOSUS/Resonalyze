namespace Resonalyze
{
    /// <summary>
    /// A consistent display snapshot of the live analyzer state. Magnitude is a
    /// linear amplitude curve (one value per FFT bin). Coherence is the
    /// magnitude-squared coherence γ² in [0, 1] and is only present in
    /// transfer-function mode. InputMagnitude is the reference-free RTA magnitude
    /// of the measured (microphone) input alone — a plain single-channel spectrum
    /// with no division by the loopback reference, so it carries no coherence and
    /// no phase.
    /// </summary>
    public sealed record LiveSpectrumSnapshot(
        double[] Magnitude,
        double[]? Coherence,
        double[]? InputMagnitude = null);
}
