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
    /// <param name="FrameCount">
    /// How many analysis frames these spectra are the average of, read under the same
    /// lock that cloned them. It travels WITH the data rather than being read back off
    /// the analyzer, because the two answer different questions once the accumulation
    /// has moved on: a capture that stores this snapshot's bins beside a later count
    /// claims more integration than it holds, and the count is exactly what a stored
    /// spatial average is judged by.
    /// </param>
    public sealed record LiveSpectrumSnapshot(
        double[] Magnitude,
        double[]? Coherence,
        double[]? InputMagnitude = null,
        int FrameCount = 0);
}
