using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class FrequencyDependentMagnitudeTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Fdw_KeepsTheFixedResponseWhileTheWindowStillSpansTheReflection()
    {
        // Below ~800 Hz the FDW window holds the whole reflection, so it must equal the fixed window.
        IReadOnlyList<SignalPoint> fixedCurve = Spectrum(Options(PhaseWindowMode.Fixed));
        IReadOnlyList<SignalPoint> fdwCurve = Spectrum(
            Options(PhaseWindowMode.FrequencyDependent));

        Assert.Equal(fixedCurve.Count, fdwCurve.Count);
        foreach ((SignalPoint expected, SignalPoint actual) in fixedCurve.Zip(fdwCurve)
                     .Where(pair => pair.First.X is >= 100 and <= 500))
        {
            Assert.Equal(expected.X, actual.X, precision: 9);
            Assert.True(Math.Abs(actual.Y - expected.Y) < 0.1,
                $"FDW moved the clamped band by {actual.Y - expected.Y:0.###} dB " +
                $"at {expected.X:0.#} Hz.");
        }
    }

    [Fact]
    public void Fdw_SuppressesTheLateReflectionRippleAtHighFrequency()
    {
        // A 2 ms reflection combs the treble ~7 dB; a 6-cycle window at 8+ kHz is under 1 ms.
        double fixedRipple = PeakToPeakDb(
            Spectrum(Options(PhaseWindowMode.Fixed)), 8_000, 16_000);
        double fdwRipple = PeakToPeakDb(
            Spectrum(Options(PhaseWindowMode.FrequencyDependent)), 8_000, 16_000);

        Assert.True(fixedRipple > 5.0,
            $"Fixture lost its comb: fixed ripple is only {fixedRipple:0.##} dB.");
        Assert.True(fdwRipple < fixedRipple * 0.25,
            $"FDW ripple {fdwRipple:0.##} dB vs fixed {fixedRipple:0.##} dB.");
    }

    [Fact]
    public void Fdw_FewerCyclesSuppressTheReflectionMore()
    {
        double fourCycles = PeakToPeakDb(
            Spectrum(Options(PhaseWindowMode.FrequencyDependent, cycles: 4)),
            3_000,
            6_000);
        double eightCycles = PeakToPeakDb(
            Spectrum(Options(PhaseWindowMode.FrequencyDependent, cycles: 8)),
            3_000,
            6_000);

        Assert.True(eightCycles > 0.5,
            $"Fixture lost its transition band: 8-cycle ripple is {eightCycles:0.##} dB.");
        Assert.True(fourCycles < eightCycles,
            $"4 cycles ({fourCycles:0.##} dB) did not suppress more than 8 " +
            $"({eightCycles:0.##} dB).");
    }

    [Fact]
    public void Fdw_InvalidCyclesFallsBackToSix()
    {
        IReadOnlyList<SignalPoint> invalid = Spectrum(
            Options(PhaseWindowMode.FrequencyDependent, cycles: 123));
        IReadOnlyList<SignalPoint> six = Spectrum(
            Options(PhaseWindowMode.FrequencyDependent, cycles: 6));

        Assert.Equal(six, invalid);
    }

    [Fact]
    public void GatedSpectrum_MatchesTheFrequencyResponseFdwCurveOnTheSameGate()
    {
        // The Virtual DSP gate-driven magnitude and the FR-mode FDW curve must be bit-equal when gates coincide (start anchor).
        SyntheticMeasurement measurement = ReflectedImpulse();
        int anchor = TransferIrStartCache.ResolveStartIndex(
            measurement.ImpulseResponse!, SampleRate, measurement.PeakIndex);
        FrequencyResponseOptions options =
            Options(PhaseWindowMode.FrequencyDependent);
        double toMilliseconds = 1_000.0 / SampleRate;
        var settings = new PhaseAnalysisSettings(
            PhaseWindowMode.FrequencyDependent,
            FdwCycles: 6,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: anchor * toMilliseconds,
            LeftMs: 256 * toMilliseconds,
            PlateauMs: (4_096 - 512) * toMilliseconds,
            RightMs: 256 * toMilliseconds,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        AnalysisCurve frequencyResponse = DataHelper.GetPrimarySpectrum(
            measurement, options, calibration: null);
        AnalysisCurve gated = DataHelper.GetGatedPrimarySpectrum(
            ReflectedImpulse(), settings, calibration: null,
            smoothingInverseOctaves: 0.0);

        Assert.Equal(frequencyResponse.Points, gated.Points);
    }

    [Fact]
    public void GatedSpectrum_FixedGateOnADelayedDeltaIsFlat()
    {
        var impulse = new Complex[8_192];
        impulse[960] = Complex.One; // 20 ms
        var measurement = new SyntheticMeasurement(impulse, SampleRate, 960);
        var settings = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            FdwCycles: 6,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 20.0,
            LeftMs: 1.0,
            PlateauMs: 5.0,
            RightMs: 3.0,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        AnalysisCurve gated = DataHelper.GetGatedPrimarySpectrum(
            measurement, settings, calibration: null, smoothingInverseOctaves: 0.0);

        Assert.All(gated.Points, point => Assert.True(Math.Abs(point.Y) < 0.1,
            $"{point.Y:0.###} dB at {point.X:0.#} Hz for a unit delta."));
    }

    private static SyntheticMeasurement ReflectedImpulse()
    {
        var impulse = new Complex[8_192];
        impulse[480] = Complex.One;
        impulse[576] = new Complex(0.4, 0.0);
        return new SyntheticMeasurement(impulse, SampleRate, 480);
    }

    private static FrequencyResponseOptions Options(
        PhaseWindowMode mode,
        int cycles = 6) => new()
    {
        Window = 4_096,
        LeftTukeyWindow = 256,
        RightTukeyWindow = 256,
        SmoothingInverseOctaves = 0,
        UseCalibration = false,
        MagnitudeWindowMode = mode,
        MagnitudeFdwCycles = cycles
    };

    private static IReadOnlyList<SignalPoint> Spectrum(
        FrequencyResponseOptions options) =>
        DataHelper.GetPrimarySpectrum(ReflectedImpulse(), options, calibration: null)
            .Points;

    private static double PeakToPeakDb(
        IReadOnlyList<SignalPoint> curve,
        double low,
        double high)
    {
        List<double> band = curve
            .Where(point => point.X >= low && point.X <= high)
            .Select(point => point.Y)
            .ToList();
        return band.Max() - band.Min();
    }
}
