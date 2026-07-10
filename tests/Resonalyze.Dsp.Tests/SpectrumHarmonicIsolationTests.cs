using System.Numerics;

namespace Resonalyze.Dsp.Tests;

// SpectrumCurveSelectionTests only pins WHICH curves GetSpectrum returns. This pins
// that each harmonic curve isolates the RIGHT region of the impulse response: the
// HDn window is centred on HarmonicIROffset(n), so a distortion packet sitting only
// at the HD2 offset must light up HD2 (and the HD2..HD5 THD window) while HD3/HD4 —
// whose windows bracket empty IR regions — stay on the floor. A shifted window,
// a wrong harmonic-offset mapping, or a degenerate window would break this.
public sealed class SpectrumHarmonicIsolationTests
{
    private const int SampleRate = 48_000;
    private const int PeakIndex = 8_000;

    // A linear offset in harmonic-number space: HarmonicIROffset(n) = 200*(n-1), so
    // HD2 sits 200 samples before the peak, HD3 at 400, HD4 at 600. GetSpectrum uses
    // the same mapping to place its windows, so placement and isolation stay consistent.
    private static SyntheticMeasurement WithHd2PacketOnly()
    {
        var ir = new Complex[16_384];
        ir[PeakIndex] = Complex.One; // linear arrival

        // Fill the HD2 window region [peak-206, peak-100] with a tone. The HD3 window
        // ([peak-406, peak-300]) and HD4 window ([peak-606, peak-500]) stay empty.
        for (int i = PeakIndex - 205; i <= PeakIndex - 101; i++)
        {
            ir[i] = new Complex(Math.Sin(2.0 * Math.PI * 12.0 * (i - (PeakIndex - 205)) / 105.0), 0.0);
        }

        return new SyntheticMeasurement(
            ir, SampleRate, PeakIndex, harmonicOffset: h => 200.0 * (h - 1));
    }

    private static double MaxDb(AnalysisCurve curve) => curve.Points.Max(p => p.Y);

    [Fact]
    public void GetSpectrum_IsolatesTheHarmonicWindowToTheCorrectIrRegion()
    {
        IReadOnlyList<AnalysisCurve> curves = DataHelper.GetSpectrum(
            WithHd2PacketOnly(),
            new FrequencyResponseOptions(),
            calibration: null,
            SpectrumCurves.SecondHarmonic
                | SpectrumCurves.ThirdHarmonic
                | SpectrumCurves.FourthHarmonic
                | SpectrumCurves.ThdPlusNoise);

        double hd2 = MaxDb(curves.Single(c => c.Kind == AnalysisCurveKind.SecondHarmonic));
        double hd3 = MaxDb(curves.Single(c => c.Kind == AnalysisCurveKind.ThirdHarmonic));
        double hd4 = MaxDb(curves.Single(c => c.Kind == AnalysisCurveKind.FourthHarmonic));
        double thd = MaxDb(curves.Single(c => c.Kind == AnalysisCurveKind.ThdPlusNoise));

        // The packet lives only in the HD2 window, so HD2 carries real energy while
        // the empty HD3/HD4 windows collapse to the -160 dB floor.
        Assert.True(hd2 > -100.0, $"HD2 should carry the packet's energy, was {hd2:0.#} dB.");
        Assert.True(hd3 < -140.0, $"HD3 window is empty, should be near the floor, was {hd3:0.#} dB.");
        Assert.True(hd4 < -140.0, $"HD4 window is empty, should be near the floor, was {hd4:0.#} dB.");
        Assert.True(hd2 > hd3 + 40.0 && hd2 > hd4 + 40.0, "HD2 must dominate the empty windows.");

        // The THD+N window spans HD2..HD5, so it also captures the packet.
        Assert.True(thd > -100.0, $"THD+N should include the HD2 packet, was {thd:0.#} dB.");
        Assert.True(thd > hd3 + 40.0, "THD+N must dominate the empty HD3 window.");
    }

    [Fact]
    public void GetSpectrum_DrawsHd2AtTheExcitationFrequency()
    {
        // The HD2 packet here is a tone at ~5.5 kHz — a second-harmonic PRODUCT
        // at 5.5 kHz, which the driver produced while the sweep fundamental was
        // at ~2.7 kHz. The standard distortion axis is the excitation
        // frequency, so the curve's peak must draw near 2.7 kHz (it used to
        // draw at the product's 5.5 kHz), and the curve cannot extend past
        // Nyquist/2, where the product would pass Nyquist.
        IReadOnlyList<AnalysisCurve> curves = DataHelper.GetSpectrum(
            WithHd2PacketOnly(),
            new FrequencyResponseOptions(),
            calibration: null,
            SpectrumCurves.SecondHarmonic);
        AnalysisCurve hd2 = curves.Single(
            c => c.Kind == AnalysisCurveKind.SecondHarmonic);

        SignalPoint peak = hd2.Points.MaxBy(p => p.Y);
        // Packet tone: 12 cycles over 105 samples at 48 kHz ≈ 5486 Hz output,
        // ≈ 2743 Hz excitation. Generous range: the short packet is spectrally
        // broad and the display smoothing widens it further.
        Assert.InRange(peak.X, 1_800, 4_000);
        Assert.True(
            hd2.Points[^1].X <= SampleRate * 0.5 / 2 + 1,
            $"HD2 axis must end at Nyquist/2, ended at {hd2.Points[^1].X:0} Hz.");
    }
}
