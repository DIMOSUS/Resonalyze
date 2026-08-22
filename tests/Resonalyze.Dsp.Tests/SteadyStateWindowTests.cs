using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

// The steady-state magnitude window: one definition in milliseconds for every
// magnitude curve the Virtual DSP tool and the EQ Wizard draw, realized in samples
// with the same clamp-and-trim the gated carve applies (ResolveGatePlacement).
public sealed class SteadyStateWindowTests
{
    [Fact]
    public void At48k_TheFullWindowFits()
    {
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(48_000);

        // 2 + 500 + 180 ms at 48 kHz — under the 32768-sample FFT, so nothing trims.
        Assert.Equal(32_736, window);
        Assert.Equal(96, left);
        Assert.Equal(8_640, right);
    }

    [Fact]
    public void AtHighRates_TheCarveClampGovernsAndTheFadesStillFit()
    {
        // 682 ms at 192 kHz would be 130944 samples; the carve holds at most
        // GatedFftLength, and the fades must be cut to fit INSIDE the clamped
        // window the same way ResolveGatePlacement cuts them — a right fade longer
        // than the window would be an invalid Tukey.
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(192_000);

        Assert.Equal(DataHelper.GatedFftLength, window);
        Assert.Equal(384, left);
        Assert.True(right <= window - left);
        Assert.True(left + right <= window);
        // Even clamped, the window stays a steady-state one: ~171 ms — dozens of
        // times the junction gate it replaced, enough for a bass band's ringing.
        Assert.True(window * 1_000.0 / 192_000 > 150);
    }

    // What the window actually delivers on the hardest realistic band — a Q 8 bell at
    // 60 Hz, whose ringing needs ~290 ms to decay 60 dB. The tolerance is per rate
    // BECAUSE the carve clamp is in samples: the window is 682 ms at 48 kHz and
    // shortens to 341 and 171 ms as the rate doubles, so the deepest band is read
    // progressively short. These figures are the measured truth, pinned so a change
    // to the constants, the clamp or the carve cannot move them unnoticed.
    [Theory]
    [InlineData(48_000, 0.15)]
    [InlineData(96_000, 0.70)]
    [InlineData(192_000, 2.00)]
    public void TheWindowResolvesADeepHighQBassBand(
        int sampleRate, double toleranceDb)
    {
        var bank = new EqualizationCurve(new[] { new PeqBand(60, 8, -8) });
        Complex[] impulse = UnitImpulse(sampleRate, out int peak);

        (double windowed, double ideal) = ReadBandDepth(impulse, peak, sampleRate, bank);

        // Against the ideal filter's own depth at its centre — the response the DSP
        // realizes and the ear hears.
        Assert.True(
            Math.Abs(windowed - ideal) < toleranceDb,
            $"at {sampleRate} Hz the window read {windowed:0.00} dB against the " +
            $"filter's {ideal:0.00} dB");
    }

    [Fact]
    public void TheWindowBeatsTheJunctionGateItReplaced()
    {
        // The claim that justifies the whole change, at the rate where the steady-state
        // window is WEAKEST (192 kHz, clamped to 171 ms): even there it reads a deep
        // high-Q bass band far closer to the truth than the old junction gate did.
        // Without this, a future clamp could quietly shrink the window back toward the
        // gate and every per-rate tolerance above would still pass.
        const int rate = 192_000;
        var bank = new EqualizationCurve(new[] { new PeqBand(60, 8, -8) });
        Complex[] impulse = UnitImpulse(rate, out int peak);

        (double steady, double ideal) = ReadBandDepth(impulse, peak, rate, bank);
        (double junction, _) = ReadBandDepth(
            impulse, peak, rate, bank, gateMs: (0.5, 4.0, 1.5));

        double steadyError = Math.Abs(steady - ideal);
        double junctionError = Math.Abs(junction - ideal);
        // Measured: 1.89 dB against 7.23 dB. The assertion is the GAP rather than a
        // ratio — what matters is how many dB of the band's depth the reader would
        // miss, and the ratio flatters a window that is merely less bad.
        Assert.True(
            junctionError - steadyError > 4.0,
            $"steady-state window off by {steadyError:0.00} dB, junction gate by " +
            $"{junctionError:0.00} dB — the gap has closed");
    }

    // A bare unit impulse: with a flat source the windowed reading IS the filter's
    // own response, so any gap from the ideal magnitude is the window's doing and
    // nothing else's.
    private static Complex[] UnitImpulse(int sampleRate, out int peak)
    {
        var impulse = new Complex[DataHelper.GatedFftLength * 2];
        peak = sampleRate / 100;
        impulse[peak] = 1.0;
        return impulse;
    }

    private static (double Windowed, double Ideal) ReadBandDepth(
        Complex[] impulse,
        int peak,
        int sampleRate,
        EqualizationCurve bank,
        (double Left, double Plateau, double Right)? gateMs = null)
    {
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(sampleRate);
        double toMs = 1_000.0 / sampleRate;
        (double leftMs, double plateauMs, double rightMs) = gateMs
            ?? (left * toMs, (window - left - right) * toMs, right * toMs);
        var gate = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: peak * toMs,
            leftMs,
            plateauMs,
            rightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        Complex[] filtered = VirtualCrossoverAnalysis.ApplyChain(
            impulse, new DspChannelChain(Peq: bank), sampleRate);
        AnalysisCurve flat = DataHelper.GetGatedPrimarySpectrum(
            new WindowMeasurement(impulse, peak, sampleRate), gate, null, 0);
        AnalysisCurve corrected = DataHelper.GetGatedPrimarySpectrum(
            new WindowMeasurement(filtered, peak, sampleRate), gate, null, 0);

        int centre = 0;
        double closest = double.MaxValue;
        for (int i = 0; i < flat.Points.Count; i++)
        {
            double distance = Math.Abs(flat.Points[i].X - 60);
            if (distance < closest)
            {
                closest = distance;
                centre = i;
            }
        }

        return (
            corrected.Points[centre].Y - flat.Points[centre].Y,
            DigitalEqualizationResponse.MagnitudeDbAt(
                bank, flat.Points[centre].X, sampleRate));
    }

    private sealed class WindowMeasurement : IImpulseMeasurement
    {
        public WindowMeasurement(Complex[] impulseResponse, int peakIndex, int sampleRate)
        {
            ImpulseResponse = impulseResponse;
            PeakIndex = peakIndex;
            SampleRate = sampleRate;
        }

        public Complex[]? ImpulseResponse { get; }
        public int PeakIndex { get; }
        public int SampleRate { get; }
        public double HarmonicIROffset(double harmonic) => 0;
    }
}
