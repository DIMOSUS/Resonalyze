namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The channel phase control against the bench that reverse-engineered it (issue
/// #88, HELIX DSP ULTRA S, ~60 electrical sweeps). The numbers below are that
/// session's fitted corners and the angles its ceiling delivers; they are literals
/// here rather than a data file, because what has to stay true is the LAW, and the
/// law is six numbers wide.
/// </summary>
public sealed class PhaseRotationControlTests
{
    private const double Rate96 = 96_000;
    private const double Rate48 = 48_000;

    [Theory]
    // setting, reference, the corner the hardware was measured at
    [InlineData(90, 5_000, 7_961.2)]
    [InlineData(180, 5_000, 4_999.0)]
    [InlineData(270, 5_000, 3_109.4)]
    [InlineData(180, 2_000, 2_000.2)]
    [InlineData(90, 2_000, 3_226.9)]
    [InlineData(180, 500, 500.19)]
    public void Realize_LandsOnTheCornerTheHardwareWasMeasuredAt(
        double degrees,
        double referenceHz,
        double measuredCornerHz)
    {
        AllPassSpec? spec = PhaseRotationControl.Realize(
            new PhaseRotationSpec(degrees, referenceHz), Rate96);
        Assert.NotNull(spec);

        // The bench resolves the corner to about 0.2 %; anything inside that is the
        // measurement's own spread rather than a disagreement with the model.
        Assert.Equal(
            measuredCornerHz,
            spec.FrequencyHz,
            measuredCornerHz * 0.002);
        Assert.Equal(PhaseRotationControl.SectionQ, spec.Q);
        Assert.Equal(AllPassType.SecondOrder, spec.Type);
    }

    [Theory]
    [InlineData(5_000)]
    [InlineData(2_000)]
    [InlineData(500)]
    [InlineData(65)]
    public void Realize_At180Degrees_PutsTheCornerExactlyOnTheReference(double referenceHz)
    {
        // The cleanest check the law offers: a second-order all-pass sits at exactly
        // -180° at its own corner, so the 180° setting must place the corner ON the
        // crossover — at any rate, since both sides of that identity are the same
        // digital filter.
        foreach (double rate in new[] { Rate96, Rate48 })
        {
            AllPassSpec? spec = PhaseRotationControl.Realize(
                new PhaseRotationSpec(180, referenceHz), rate);
            Assert.NotNull(spec);
            Assert.Equal(referenceHz, spec.FrequencyHz, referenceHz * 1e-6);
        }
    }

    [Fact]
    public void Realize_SolvesInTheDigitalDomain_SoTheCornerFollowsTheProcessorsRate()
    {
        // The same 90° is a different filter on a 48 kHz device: the corner is placed
        // by the digital phase, and the bilinear warping at 8 kHz is not the same at
        // the two rates. A model that solved an analog prototype would return one
        // number for both, and would be 1.4 % out at 96 kHz against the bench.
        double at96 = PhaseRotationControl
            .Realize(new PhaseRotationSpec(90, 5_000), Rate96)!.FrequencyHz;
        double at48 = PhaseRotationControl
            .Realize(new PhaseRotationSpec(90, 5_000), Rate48)!.FrequencyHz;

        Assert.Equal(7_976.9, at96, 0.5);
        Assert.Equal(7_674.1, at48, 0.5);
    }

    [Theory]
    // reference, the smallest rotation still reachable, and how many of the 63
    // settings collapse. The angles are this model's, computed at an exactly 18 kHz
    // ceiling; the bench's own figures — fitted to its measured 18009 Hz — agree to
    // a few hundredths (11.30, 17.12, 29.46, 51.04), and the counts agree exactly.
    [InlineData(500, 5.625, 0)]
    [InlineData(1_000, 5.625, 0)]
    [InlineData(2_000, 11.31, 2)]
    [InlineData(3_000, 17.14, 3)]
    [InlineData(5_000, 29.49, 5)]
    [InlineData(8_000, 51.08, 9)]
    public void TheCeiling_CollapsesTheSmallestSettingsOntoOneFilter(
        double referenceHz,
        double smallestRotationDeg,
        int settingsLost)
    {
        // Measured three ways at two references: the corner will not go above about
        // 18 kHz, so at a high crossover the first few positions of the control are
        // one and the same filter and deliver an angle that is not even on the
        // control's 5.625° grid. Below a 1 kHz reference nothing is capped.
        double delivered = PhaseRotationControl.DeliveredDegrees(
            new PhaseRotationSpec(PhaseRotationControl.StepDegrees, referenceHz), Rate96);
        Assert.Equal(smallestRotationDeg, delivered, 0.05);

        int lost = 0;
        for (int step = 1; step < PhaseRotationControl.StepCount; step++)
        {
            double asked = step * PhaseRotationControl.StepDegrees;
            if (PhaseRotationControl.DeliveredDegrees(
                    new PhaseRotationSpec(asked, referenceHz), Rate96) > asked + 1e-6)
            {
                lost++;
            }
        }

        Assert.Equal(settingsLost, lost);
    }

    [Fact]
    public void DeliveredDegrees_IsWhatWasAskedFor_WhereNothingIsCapped()
    {
        // A subwoofer channel and any midrange crossed under a kilohertz keep the
        // whole control, so the readout must agree with the dial to the last step.
        foreach (double reference in new[] { 65.0, 250.0, 500.0 })
        {
            for (int step = 1; step < PhaseRotationControl.StepCount; step++)
            {
                double asked = step * PhaseRotationControl.StepDegrees;
                Assert.Equal(
                    asked,
                    PhaseRotationControl.DeliveredDegrees(
                        new PhaseRotationSpec(asked, reference), Rate96),
                    1e-6);
            }
        }
    }

    [Fact]
    public void Realize_LeavesTheMagnitudeAlone()
    {
        // Measured flat to 0.016-0.028 dB rms across every setting. Here it is exact:
        // the section is the library's own RBJ all-pass, whose numerator is its
        // denominator reversed.
        var rotation = new PhaseRotationSpec(270, 110);
        AllPassSpec? spec = PhaseRotationControl.Realize(rotation, Rate96);
        Assert.NotNull(spec);
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 300))
        {
            Assert.Equal(
                1.0,
                AllPassFilter.Response(spec, frequency, Rate96).Magnitude,
                9);
        }
    }

    [Fact]
    public void ATransparentSetting_BuildsNothing()
    {
        Assert.Null(PhaseRotationControl.Realize(new PhaseRotationSpec(0, 5_000), Rate96));
        // No crossover to state the angle against: the control has no reference and
        // cannot mean anything, so it does not silently pick one.
        Assert.Null(PhaseRotationControl.Realize(new PhaseRotationSpec(90, 0), Rate96));
        Assert.Equal(
            0,
            PhaseRotationControl.DeliveredDegrees(new PhaseRotationSpec(0, 5_000), Rate96));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 5.625)]
    [InlineData(56.25, 56.25)]
    [InlineData(57, 56.25)]
    [InlineData(359, 354.375)]
    [InlineData(1_000, 354.375)]
    [InlineData(-5, 0)]
    [InlineData(double.NaN, 0)]
    public void SnapToGrid_KeepsTheControlOnItsOwnPositions(double asked, double expected)
    {
        Assert.Equal(expected, PhaseRotationControl.SnapToGrid(asked), 9);
    }

    [Fact]
    public void TheGrid_IsSixtyFourPositionsEndingJustShortOfAFullTurn()
    {
        Assert.Equal(5.625, PhaseRotationControl.StepDegrees, 9);
        Assert.Equal(354.375, PhaseRotationControl.MaximumDegrees, 9);
        Assert.Equal(18_000, PhaseRotationControl.MaximumCornerHz(Rate96), 6);
    }

    [Fact]
    public void AChain_CarriesTheRotation_AndThePreparedResponseAgreesWithIt()
    {
        // The two ways a chain is evaluated — point by point for a plot, and as a
        // prepared biquad cascade for the FFT path — have to realize the same filter,
        // and the cascade is where a new stage is easiest to forget.
        var chain = new DspChannelChain(
            Crossover: CrossoverSpec.Off,
            PhaseRotation: new PhaseRotationSpec(90, 110));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, (int)Rate96);

        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 200))
        {
            System.Numerics.Complex direct = chain.Response(frequency, Rate96);
            System.Numerics.Complex viaCascade = prepared.Response(frequency);
            Assert.Equal(direct.Real, viaCascade.Real, 9);
            Assert.Equal(direct.Imaginary, viaCascade.Imaginary, 9);
        }

        // And it is a real rotation, not a no-op: 90° at the reference, as dialled.
        Assert.Equal(
            -90.0,
            chain.Response(110, Rate96).Phase * 180.0 / Math.PI,
            0.01);
    }
}
