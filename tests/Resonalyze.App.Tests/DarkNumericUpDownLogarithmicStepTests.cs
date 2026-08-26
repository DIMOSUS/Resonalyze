using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// A frequency field used to move by a fixed 10 Hz, which is nearly half an octave at
/// 30 Hz and a rounding error at 15 kHz. With
/// <see cref="DarkNumericUpDown.LogarithmicFrequencyStep"/> one step is a 96th of an
/// octave instead — the same distance wherever it is taken on a logarithmic frequency
/// axis — floored at the one unit a whole-Hz field can actually show.
/// </summary>
public sealed class DarkNumericUpDownLogarithmicStepTests
{
    // 2 ^ (1/96), the ratio one step multiplies the value by.
    private const double StepRatio = 1.0072464014332754;

    [Theory]
    // Under about 69 Hz a 96th of an octave is less than half a Hz, so a whole-Hz
    // field steps by the 1 Hz it can show.
    [InlineData(10, 11)]
    [InlineData(20, 21)]
    [InlineData(50, 51)]
    // Above it the ratio itself decides: 0.72 Hz at 100 Hz, 7.2 at 1 kHz, 145 at 20 kHz.
    [InlineData(100, 101)]
    [InlineData(500, 504)]
    [InlineData(1000, 1007)]
    [InlineData(10_000, 10_072)]
    [InlineData(20_000, 20_145)]
    public void StepUp_MovesTheValueBy_A96thOfAnOctave(int from, int expected)
    {
        using DarkNumericUpDown control = NewFrequencyControl();
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
        using DarkNumericUpDown control = NewFrequencyControl();
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
        using DarkNumericUpDown control = NewFrequencyControl();
        control.Value = from;

        PressUp(control);
        PressDown(control);

        // Why the steps walk an anchored ladder rather than measuring a fresh step off
        // the value each time: at 10 kHz the step is 72 Hz and grows by half a Hz across
        // one step of its own, so a re-measured step rounds to 73 coming back and hands
        // the value back one Hz short.
        Assert.Equal(from, control.Value);
    }

    [Theory]
    // 347 Hz is the case that showed the way down and the way up disagreeing: down to
    // 345, and back up to 348. It is reachable by stepping, not only by typing — a
    // step down from 350 lands on it.
    [InlineData(347)]
    [InlineData(1320)]
    [InlineData(4100)]
    [InlineData(18_000)]
    [InlineData(20)]
    [InlineData(63)]
    [InlineData(1000)]
    public void AStepDownAndStraightBackUp_LandsWhereItStarted(int from)
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.Value = from;

        PressDown(control);
        PressUp(control);

        Assert.Equal(from, control.Value);
    }

    [Fact]
    public void EveryWholeHzInTheBand_ReturnsFromAStepAndAStepBack_BothWaysRound()
    {
        // The range is opened up so nothing clamps: clamping is its own behaviour and
        // would mask what this sweep is for.
        using var control = new DarkNumericUpDown
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
    public void TheModeReplacesTheFixedIncrement()
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.Increment = 10;
        control.Value = 100;

        PressUp(control);

        // 10 Hz is what this field carried before the mode existed.
        Assert.Equal(101m, control.Value);
    }

    [Fact]
    public void WithoutTheMode_TheFixedIncrementStillRules()
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.LogarithmicFrequencyStep = false;
        control.Increment = 10;
        control.Value = 100;

        PressUp(control);

        Assert.Equal(110m, control.Value);
    }

    [Fact]
    public void TheStepFollowsTypedTextRatherThanTheValueItReplaced()
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.Value = 100;
        // Typed but not yet committed: stepping commits first, so the step must be
        // measured off 5000 (36 Hz) and not off the 100 Hz still held in Value.
        Editor(control).Text = 5000.ToString(CultureInfo.CurrentCulture);

        PressUp(control);

        Assert.Equal(5036m, control.Value);
    }

    [Fact]
    public void WalkingTheWholeBand_KeepsEveryStepOneRoundingUnitFromA96thOfAnOctave()
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.Value = 20;
        decimal firstStep = 0;
        decimal lastStep = 0;
        int steps = 0;

        while (control.Value < 20_000m)
        {
            decimal before = control.Value;
            PressUp(control);
            decimal step = control.Value - before;

            // A rung sits within half a unit of the exact 96th of an octave at each end
            // of the step, so the pair can be a shade over one unit apart. That is also
            // why the whole-Hz width alternates — 1, 1, 2, 1, 2 either side of 141 Hz,
            // where a 96th of an octave is 1.02 Hz — which is the field's resolution
            // showing, not the spacing changing.
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

        // The 1 Hz the field can show at the bottom, a 96th of an octave at the top.
        Assert.Equal(1m, firstStep);
        Assert.InRange(lastStep, 143m, 147m);
        // Ten octaves at 96 steps each, less the low end where the 1 Hz floor is wider.
        Assert.InRange(steps, 700, 900);
    }

    [Fact]
    public void AFieldWithDecimals_StepsOnItsOwnResolution()
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.DecimalPlaces = 1;
        control.Value = 1000;

        PressUp(control);

        // A tenth-Hz field shows the 7.2464 Hz the ratio asks for as 7.2.
        Assert.Equal(1007.2m, control.Value);
    }

    [Fact]
    public void AFieldWithDecimals_StillMovesWhereWholeHzWouldNot()
    {
        using DarkNumericUpDown control = NewFrequencyControl();
        control.DecimalPlaces = 1;
        control.Value = 20;

        PressUp(control);

        // 0.1449 Hz rounds to 0.1 here, where a whole-Hz field has to spend a whole Hz.
        Assert.Equal(20.1m, control.Value);
    }

    private static DarkNumericUpDown NewFrequencyControl() => new()
    {
        DecimalPlaces = 0,
        Minimum = 10,
        Maximum = 24_000,
        Increment = 10,
        LogarithmicFrequencyStep = true,
        Value = 1000
    };

    private static TextBox Editor(DarkNumericUpDown control) =>
        control.Controls.OfType<TextBox>().Single();

    private static void PressUp(DarkNumericUpDown control) => PressKey(control, Keys.Up);

    private static void PressDown(DarkNumericUpDown control) => PressKey(control, Keys.Down);

    // ProcessCmdKey is the arrow-key route into the same StepUp/StepDown the spin
    // buttons and the wheel take.
    private static void PressKey(DarkNumericUpDown control, Keys key)
    {
        MethodInfo method = typeof(DarkNumericUpDown).GetMethod(
            "ProcessCmdKey",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessCmdKey is missing.");
        var message = new Message
        {
            Msg = 0x0100, // WM_KEYDOWN
            WParam = (IntPtr)key
        };
        object[] arguments = [message, key];
        Assert.True((bool)method.Invoke(control, arguments)!);
    }
}
