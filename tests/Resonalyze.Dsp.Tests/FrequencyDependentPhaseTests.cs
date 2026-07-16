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
    public void Fdw_IsLinear_SpectrumOfASumIsTheSumOfTheSpectra()
    {
        // Virtual DSP's core invariant: the tool draws per-channel FDW phase
        // next to the FDW phase of the sample-wise summed IR, so the analysis
        // must satisfy FDW(A+B) = FDW(A) + FDW(B) bin for bin — otherwise the
        // drawn Sum need not match the vector sum of the drawn channels. Two
        // channels with DIFFERENT early reflections make the bank spectra
        // rotate differently between window lengths, which is exactly where
        // the earlier log-magnitude/shortest-arc interpolation broke
        // superposition by tens of degrees; the complex-linear interpolation
        // is exact here (the FFT and the window lerp are both linear). The
        // comparison runs on the complex spectra across ALL bins, so it also
        // covers every point BETWEEN the bank centers where the interpolation
        // acts.
        var first = new Complex[4_096];
        first[480] = Complex.One;
        first[480 + 62] = new Complex(0.7, 0.0); // +0.7 at 1.3 ms
        var second = new Complex[4_096];
        second[480] = Complex.One;
        second[480 + 101] = new Complex(-0.5, 0.0); // -0.5 at 2.1 ms
        var summed = new Complex[4_096];
        for (int i = 0; i < summed.Length; i++)
        {
            summed[i] = first[i] + second[i];
        }

        PhaseAnalysisSettings settings = Settings(
            PhaseWindowMode.FrequencyDependent, 6, PhaseDetrendMode.Manual);
        Complex[] firstSpectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(first, SampleRate, 480),
            settings,
            out int firstStart);
        Complex[] secondSpectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(second, SampleRate, 480),
            settings,
            out int secondStart);
        Complex[] sumSpectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new SyntheticMeasurement(summed, SampleRate, 480),
            settings,
            out int sumStart);

        Assert.Equal(firstStart, secondStart);
        Assert.Equal(firstStart, sumStart);
        for (int bin = 1; bin < sumSpectrum.Length / 2; bin++)
        {
            Complex expected = firstSpectrum[bin] + secondSpectrum[bin];
            double error = (sumSpectrum[bin] - expected).Magnitude;
            Assert.True(error <= 1e-9 * (1.0 + expected.Magnitude),
                $"Superposition broken by {error:e} at bin {bin} " +
                $"({bin * (double)SampleRate / sumSpectrum.Length:0.#} Hz).");
        }
    }

    [Fact]
    public void WrappedPhase_MasksBinsBelowTheReliabilityGate()
    {
        // A narrowband tone burst has no energy far above its band: the wrapped
        // phase there is noise and must be blanked (NaN), not drawn as ±180°
        // chaos. In-band bins stay finite.
        var impulse = new Complex[8_192];
        const int Start = 480;
        const int Length = 480; // 10 ms burst at 1 kHz
        for (int i = 0; i < Length; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (Length - 1.0)));
            impulse[Start + i] = new Complex(
                window * Math.Sin(2 * Math.PI * 1_000.0 * i / SampleRate), 0.0);
        }
        var measurement = new SyntheticMeasurement(impulse, SampleRate, Start + Length / 2);

        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual));

        Assert.Contains(phase, point =>
            point.X is >= 800 and <= 1_200 && !double.IsNaN(point.Y));
        Assert.All(
            phase.Where(point => point.X is >= 10_000 and <= 20_000),
            point => Assert.True(double.IsNaN(point.Y),
                $"Unreliable bin at {point.X:0.#} Hz was drawn ({point.Y:0.###} rad)."));
    }

    [Fact]
    public void WrappedPhase_KeepsEveryBinOfAFlatSpectrum()
    {
        // The masking must not over-fire: a pure delay is reliable everywhere,
        // so no bin of its wrapped phase goes missing.
        SyntheticMeasurement measurement = DelayedImpulse(480);
        List<SignalPoint> phase = DataHelper.GetGatedPhaseData(
            measurement,
            Settings(PhaseWindowMode.Fixed, 6, PhaseDetrendMode.Manual));

        Assert.All(
            phase.Where(point => point.X is >= 100 and <= 20_000),
            point => Assert.False(double.IsNaN(point.Y)));
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
