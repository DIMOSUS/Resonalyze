namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The front-rise contract of the first-arrival search: texture on a flat
/// shelf ahead of the arrival must not be read as a front, however that shelf
/// got there.
/// </summary>
/// <remarks>
/// The shelf is what a zero-phase bandpass builds ahead of a reverberant
/// record: every later sample's backward skirt, superposed. Measured on a
/// field subwoofer at 32.5-130 Hz it sat at -20 dB under the peak — 4.7 dB
/// ABOVE the per-peak ring ceiling (kernel ring -30.7 dB at that distance,
/// doubled by the superposition margin), because no single-peak ceiling can
/// price a whole tail's superposition. Its micro-ripples are local maxima, and
/// exactly one of them survives the sidelobe walk: the one whose only in-reach
/// accepted peak is the main arrival itself, whose ceiling it beats. The
/// search then reported 0.02 ms on a record whose driver first played at
/// 27 ms, and the alignment predictor built impossible negative arrivals from
/// reads like it.
/// <para>
/// The rise gate (RisesOutOfItsApproach) reads the shelf instead of
/// predicting it: a front must climb out of the floor of its own approach —
/// a kernel core's reach behind it — and ripple on a flat shelf climbs by
/// hundredths of a dB where a genuine front climbs by whole ones. This pins
/// the failing geometry at the search's own level, with the field case's
/// proportions.
/// </para>
/// </remarks>
public sealed class AcausalPedestalTests
{
    private const int SampleRate = 96_000;

    [Fact]
    public void RippleOnAFlatShelf_IsNotReadAsAnArrival()
    {
        const int length = 8_192;
        const int shelfEnd = 600;
        const int peakIndex = 1_000;
        const double shelfLevel = 0.1;

        var envelope = new double[length];
        for (int i = 0; i < shelfEnd; i++)
        {
            // The flat shelf, textured: a local maximum every 15 samples, one
            // percent proud — far more relief than the field's micro-ripples
            // had, so the gate is exercised above its own margin of luck.
            envelope[i] = shelfLevel * (i % 15 == 7 ? 1.01 : 1.0);
        }
        for (int i = shelfEnd; i <= peakIndex; i++)
        {
            // A monotone climb out of the shelf into the arrival.
            envelope[i] = shelfLevel +
                (1.0 - shelfLevel) * (i - shelfEnd) / (peakIndex - shelfEnd);
        }
        for (int i = peakIndex + 1; i < length; i++)
        {
            // The decay, dropping under the shelf quickly and staying quiet, so
            // the noise-floor quantile reads the tail rather than the shelf.
            envelope[i] = Math.Max(1e-6, Math.Exp(-(i - peakIndex) / 150.0));
        }

        // A kernel whose ring at the shelf's distance from the peak is far
        // below the shelf — the field geometry: ring(2568) was -30.7 dB there
        // against a -20 dB shelf, so the per-peak ceiling (ring doubled) could
        // not name the ripple a sidelobe, and it reached the packet tests as a
        // "genuine" early arrival.
        var kernelEnvelope = new double[length];
        for (int d = 0; d < length; d++)
        {
            kernelEnvelope[d] = Math.Exp(-d / 60.0);
        }

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            SampleRate,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                AnalysisKernelEnvelope = kernelEnvelope
            });

        // Nothing arrives on the shelf: the first arrival is the climb's peak.
        Assert.True(
            result.SelectedIndex >= shelfEnd,
            $"the search selected index {result.SelectedIndex} — a ripple on " +
            "the flat shelf ahead of the arrival, not a front that rises");
        Assert.Equal(peakIndex, result.StrongestIndex);
    }
}
