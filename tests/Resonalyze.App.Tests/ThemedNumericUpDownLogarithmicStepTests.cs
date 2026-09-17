using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class ThemedNumericUpDownLogarithmicStepTests
{
    // 2 ^ (1/96).
    private const double StepRatio = 1.0072464014332754;

    [Theory]
    // Below ~69 Hz a 96th of an octave is under half a Hz, so a whole-Hz field steps by 1 Hz.
    [InlineData(10, 11)]
    [InlineData(20, 21)]
    [InlineData(50, 51)]
    [InlineData(100, 101)]
    [InlineData(500, 504)]
    [InlineData(1000, 1007)]
    [InlineData(10_000, 10_072)]
    [InlineData(20_000, 20_145)]
    public void StepUp_MovesTheValueBy_A96thOfAnOctave(int from, int expected)
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Value = from;

        PressUp(control);

        Assert.Equal(expected, control.Value);
    }

    [Theory]
    [InlineData(21, 20)]
    [InlineData(101, 100)]
    [InlineData(504, 500)]
    [InlineData(1007, 1000)]
    [InlineData(10_072, 10_000)]
    [InlineData(20_145, 20_000)]
    public void StepDown_DividesByTheSameRatio(int from, int expected)
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Value = from;

        PressDown(control);

        Assert.Equal(expected, control.Value);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(63)]
    [InlineData(100)]
    [InlineData(347)]
    [InlineData(1000)]
    [InlineData(3150)]
    [InlineData(9973)]
    [InlineData(19_997)]
    public void AStepUpAndStraightBackDown_LandsWhereItStarted(int from)
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Value = from;

        PressUp(control);
        PressDown(control);

        // Steps walk an anchored ladder: a re-measured step at 10 kHz (72 Hz) rounds to 73 coming back.
        Assert.Equal(from, control.Value);
    }

    [Theory]
    // 347 Hz (a step down from 350) must not go down to 345 and back up to 348.
    [InlineData(347)]
    [InlineData(1320)]
    [InlineData(4100)]
    [InlineData(18_000)]
    [InlineData(20)]
    [InlineData(63)]
    [InlineData(1000)]
    public void AStepDownAndStraightBackUp_LandsWhereItStarted(int from)
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Value = from;

        PressDown(control);
        PressUp(control);

        Assert.Equal(from, control.Value);
    }

    [Fact]
    public void EveryWholeHzInTheBand_ReturnsFromAStepAndAStepBack_BothWaysRound()
    {
        // Range opened so clamping does not mask the sweep.
        using var control = new ThemedNumericUpDown
        {
            DecimalPlaces = 0,
            Minimum = 1,
            Maximum = 1_000_000,
            LogarithmicFrequencyStep = true
        };
        var upThenDown = new List<int>();
        var downThenUp = new List<int>();

        for (int frequency = 10; frequency <= 24_000; frequency++)
        {
            control.Value = frequency;
            PressUp(control);
            PressDown(control);
            if (control.Value != frequency)
            {
                upThenDown.Add(frequency);
            }

            control.Value = frequency;
            PressDown(control);
            PressUp(control);
            if (control.Value != frequency)
            {
                downThenUp.Add(frequency);
            }
        }

        Assert.Empty(upThenDown);
        Assert.Empty(downThenUp);
    }

    [Fact]
    public void AStepTheMaximumCutShort_StillStepsBackToWhereItCameFrom()
    {
        // The 20 kHz limit has no rung above 19 997 Hz; the value must still come back off it.
        using ThemedNumericUpDown control = NewWizardRangeControl();
        control.Value = 19_997;

        PressUp(control);
        Assert.Equal(20_000m, control.Value);

        PressDown(control);
        Assert.Equal(19_997m, control.Value);
    }

    [Fact]
    public void HeldAgainstTheMaximum_TheWayBackIsStillOneStep()
    {
        using ThemedNumericUpDown control = NewWizardRangeControl();
        control.Value = 19_997;

        PressUp(control);
        PressUp(control);
        PressUp(control);
        Assert.Equal(20_000m, control.Value);

        PressDown(control);

        Assert.Equal(19_997m, control.Value);
    }

    [Fact]
    public void AStepTheMinimumCutShort_StillStepsBackToWhereItCameFrom()
    {
        using var control = new ThemedNumericUpDown
        {
            DecimalPlaces = 0,
            Minimum = 1000,
            Maximum = 20_000,
            LogarithmicFrequencyStep = true,
            Value = 1001
        };

        PressDown(control);
        Assert.Equal(1000m, control.Value);

        PressUp(control);

        Assert.Equal(1001m, control.Value);
    }

    [Fact]
    public void TheModeReplacesTheFixedIncrement()
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Increment = 10;
        control.Value = 100;

        PressUp(control);

        Assert.Equal(101m, control.Value);
    }

    [Fact]
    public void WithoutTheMode_TheFixedIncrementStillRules()
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.LogarithmicFrequencyStep = false;
        control.Increment = 10;
        control.Value = 100;

        PressUp(control);

        Assert.Equal(110m, control.Value);
    }

    [Fact]
    public void TheStepFollowsTypedTextRatherThanTheValueItReplaced()
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Value = 100;
        // Stepping commits typed text first, so the step is measured off 5000, not the 100 in Value.
        Editor(control).Text = 5000.ToString(CultureInfo.CurrentCulture);

        PressUp(control);

        Assert.Equal(5036m, control.Value);
    }

    [Fact]
    public void WalkingTheWholeBand_KeepsEveryStepOneRoundingUnitFromA96thOfAnOctave()
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.Value = 20;
        decimal firstStep = 0;
        decimal lastStep = 0;
        int steps = 0;

        while (control.Value < 20_000m)
        {
            decimal before = control.Value;
            PressUp(control);
            decimal step = control.Value - before;

            // Rungs round to whole Hz at each end, so a pair can be a shade over one unit apart.
            Assert.True(
                Math.Abs((double)control.Value - ((double)before * StepRatio)) <= 1.01,
                $"{before} Hz stepped to {control.Value} Hz, off a 96th of an octave.");
            lastStep = step;
            if (steps == 0)
            {
                firstStep = step;
            }

            steps++;
            Assert.True(steps < 5_000, "Stepping up never reached the top of the band.");
        }

        Assert.Equal(1m, firstStep);
        Assert.InRange(lastStep, 143m, 147m);
        Assert.InRange(steps, 700, 900);
    }

    [Fact]
    public void AFieldWithDecimals_StepsOnItsOwnResolution()
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.DecimalPlaces = 1;
        control.Value = 1000;

        PressUp(control);

        Assert.Equal(1007.2m, control.Value);
    }

    [Fact]
    public void AFieldWithDecimals_StillMovesWhereWholeHzWouldNot()
    {
        using ThemedNumericUpDown control = NewFrequencyControl();
        control.DecimalPlaces = 1;
        control.Value = 20;

        PressUp(control);

        Assert.Equal(20.1m, control.Value);
    }

    private static ThemedNumericUpDown NewWizardRangeControl() => new()
    {
        DecimalPlaces = 0,
        Minimum = 20,
        Maximum = 20_000,
        Increment = 10,
        LogarithmicFrequencyStep = true,
        Value = 1000
    };

    private static ThemedNumericUpDown NewFrequencyControl() => new()
    {
        DecimalPlaces = 0,
        Minimum = 10,
        Maximum = 24_000,
        Increment = 10,
        LogarithmicFrequencyStep = true,
        Value = 1000
    };

    private static TextBox Editor(ThemedNumericUpDown control) =>
        control.Controls.OfType<TextBox>().Single();

    private static void PressUp(ThemedNumericUpDown control) => PressKey(control, Keys.Up);

    private static void PressDown(ThemedNumericUpDown control) => PressKey(control, Keys.Down);

    private static void PressKey(ThemedNumericUpDown control, Keys key)
    {
        MethodInfo method = typeof(ThemedNumericUpDown).GetMethod(
            "ProcessCmdKey",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessCmdKey is missing.");
        var message = new Message
        {
            Msg = 0x0100,
            WParam = (IntPtr)key
        };
        object[] arguments = [message, key];
        Assert.True((bool)method.Invoke(control, arguments)!);
    }
}
