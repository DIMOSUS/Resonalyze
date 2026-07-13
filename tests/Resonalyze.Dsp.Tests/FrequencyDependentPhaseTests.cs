using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class FrequencyDependentPhaseTests
{
    private const int SampleRate = 48_000;

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void PureDelay_AutoDetrendIsFlat(int cycles)
    {
        SyntheticMeasurement measurement = DelayedImpulse(960);
        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent,
            cycles,
            PhaseDetrendMode.Auto,
            gateOffsetMs: 20.0);

        double resolved = DataHelper.ResolvePhaseDetrendMilliseconds(measurement, settings);
        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(measurement, settings);

        Assert.Equal(20.0, resolved, tolerance: 0.06);
        Assert.All(
            phase.Where(point => point.X is >= 100 and <= 15_000),
            point => Assert.True(Math.Abs(point.Y) < 1e-5,
                $"Residual {point.Y:e} rad at {point.X:0.#} Hz."));
    }

    [Fact]
    public void Fdw_InvalidCyclesFallsBackToSix()
    {
        SyntheticMeasurement measurement = ReflectedImpulse();
        List<SignalPoint> invalid = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.FrequencyDependent, 123, PhaseDetrendMode.Manual));
        List<SignalPoint> six = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Manual));

        Assert.Equal(six, invalid);
    }

    [Fact]
    public void Fdw_WhenEveryWindowIsClamped_MatchesFixed()
    {
        SyntheticMeasurement measurement = DelayedImpulse(480);
        PhaseAnalysisSettings fixedSettings = Settings(
            PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual) with
        {
            LeftMs = 0.5,
            PlateauMs = 0.4,
            RightMs = 0.1
        };
        PhaseAnalysisSettings fdwSettings = fixedSettings with
        {
            WindowMode = PhaseWindowMode.FrequencyDependent
        };

        List<SignalPoint> fixedPhase = DataHelper.GetGatedPhaseData(measurement, fixedSettings);
        List<SignalPoint> fdwPhase = DataHelper.GetGatedPhaseData(measurement, fdwSettings);

        Assert.Equal(fixedPhase.Count, fdwPhase.Count);
        foreach ((SignalPoint expected, SignalPoint actual) in fixedPhase.Zip(fdwPhase))
        {
            Assert.Equal(expected.X, actual.X, precision: 12);
            double error = Math.IEEERemainder(actual.Y - expected.Y, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-10);
        }
    }

    [Fact]
    public void Fdw_PartialClampKeepsFixedSpectrumBelowTransition()
    {
        var impulse = new Complex[4_096];
        impulse[480] = Complex.One;
        impulse[576] = new Complex(0.6, 0.0); // 2 ms late reflection
        var measurement = new SyntheticMeasurement(impulse, SampleRate, 480);
        PhaseAnalysisSettings fixedSettings = Settings(
            PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual) with
        {
            LeftMs = 1.0,
            PlateauMs = 1.0,
            RightMs = 4.0
        };
        PhaseAnalysisSettings fdwSettings = fixedSettings with
        {
            WindowMode = PhaseWindowMode.FrequencyDependent
        };

        List<SignalPoint> fixedPhase = DataHelper.GetGatedPhaseData(measurement, fixedSettings);
        List<SignalPoint> fdwPhase = DataHelper.GetGatedPhaseData(measurement, fdwSettings);

        foreach ((SignalPoint expected, SignalPoint actual) in fixedPhase.Zip(fdwPhase)
                     .Where(pair => pair.First.X is >= 100 and <= 700))
        {
            double error = Math.IEEERemainder(actual.Y - expected.Y, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-10,
                $"FDW changed the clamped spectrum by {error:e} rad at {expected.X:0.#} Hz.");
        }

        double highFrequencyDifference = fixedPhase.Zip(fdwPhase)
            .Where(pair => pair.First.X is >= 4_000 and <= 10_000)
            .Average(pair => Math.Abs(Math.IEEERemainder(
                pair.Second.Y - pair.First.Y,
                Math.Tau)));
        Assert.True(highFrequencyDifference > 0.01,
            $"FDW did not shorten above the transition ({highFrequencyDifference:e} rad).");
    }

    [Fact]
    public void CommonDetrendPreservesRelativePhase()
    {
        SyntheticMeasurement first = DelayedImpulse(480);
        SyntheticMeasurement second = DelayedImpulse(504);
        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Manual,
            manualMs: 10.0);
        List<SignalPoint> firstPhase = DataHelper.GetGatedPhaseData(first, settings);
        List<SignalPoint> secondPhase = DataHelper.GetGatedPhaseData(second, settings);

        foreach ((SignalPoint a, SignalPoint b) in firstPhase.Zip(secondPhase)
                     .Where(pair => pair.First.X is >= 200 and <= 10_000))
        {
            double expected = -Math.Tau * a.X * 24 / SampleRate;
            double actual = Math.IEEERemainder(b.Y - a.Y, Math.Tau);
            double error = Math.IEEERemainder(actual - expected, Math.Tau);
            Assert.True(Math.Abs(error) < 1e-5,
                $"Relative-phase error {error:e} at {a.X:0.#} Hz.");
        }
    }

    [Fact]
    public void CommonAutoDetrend_DoesNotIndependentlyFlattenOtherChannels()
    {
        SyntheticMeasurement anchor = DelayedImpulse(480);
        SyntheticMeasurement later = DelayedImpulse(504);
        PhaseAnalysisSettings auto = Settings(
            PhaseWindowMode.FrequencyDependent,
            6,
            PhaseDetrendMode.Auto);
        double common = DataHelper.ResolveCommonPhaseDetrendMilliseconds(anchor, auto);
        PhaseAnalysisSettings shared = auto with
        {
            DetrendMode = PhaseDetrendMode.Manual,
            ManualDetrendMilliseconds = common
        };

        List<SignalPoint> anchorPhase = DataHelper.GetGatedPhaseData(anchor, shared);
        List<SignalPoint> laterPhase = DataHelper.GetGatedPhaseData(later, shared);
        double anchorEnergy = MeanAbsoluteAngle(anchorPhase, 500, 5_000);
        double laterEnergy = MeanAbsoluteAngle(laterPhase, 500, 5_000);

        Assert.True(anchorEnergy < 1e-5);
        Assert.True(laterEnergy > 0.2,
            "The later channel was independently flattened instead of using the common reference.");
    }

    [Fact]
    public void FdwSuppressesLateReflectionMoreAtHighFrequency()
    {
        SyntheticMeasurement measurement = ReflectedImpulse();
        List<SignalPoint> fixedPhase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual));
        List<SignalPoint> fdwPhase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.FrequencyDependent, 4, PhaseDetrendMode.Manual));

        double fixedHigh = MeanAbsoluteAngle(fixedPhase, 8_000, 15_000);
        double fdwHigh = MeanAbsoluteAngle(fdwPhase, 8_000, 15_000);
        Assert.True(fdwHigh < fixedHigh * 0.7,
            $"FDW {fdwHigh:0.###} rad, fixed {fixedHigh:0.###} rad.");
    }

    private static double MeanAbsoluteAngle(
        IEnumerable<SignalPoint> points,
        double low,
        double high) => points
        .Where(point => point.X >= low && point.X <= high)
        .Average(point => Math.Abs(Math.IEEERemainder(point.Y, Math.Tau)));

    private static PhaseAnalysisSettings Settings(
        PhaseWindowMode windowMode,
        int cycles,
        PhaseDetrendMode detrendMode,
        double manualMs = 10.0,
        double gateOffsetMs = 10.0) => new(
            windowMode,
            cycles,
            detrendMode,
            manualMs,
            GateOffsetMs: gateOffsetMs,
            LeftMs: 1.0,
            PlateauMs: 3.0,
            RightMs: 12.0,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

    private static SyntheticMeasurement DelayedImpulse(int sample)
    {
        var impulse = new Complex[4_096];
        impulse[sample] = Complex.One;
        return new SyntheticMeasurement(impulse, SampleRate, sample);
    }

    private static SyntheticMeasurement ReflectedImpulse()
    {
        var impulse = new Complex[4_096];
        impulse[480] = Complex.One;
        impulse[576] = new Complex(0.4, 0.0); // 2 ms late reflection
        return new SyntheticMeasurement(impulse, SampleRate, 480);
    }
}
