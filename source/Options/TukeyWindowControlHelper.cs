namespace Resonalyze.Options;

internal static class TukeyWindowControlHelper
{
    public static void ClampAndUpdateLimits(
        NumericUpDown window,
        NumericUpDown left,
        NumericUpDown right)
    {
        decimal windowLength = window.Value;

        left.Maximum = windowLength;
        right.Maximum = windowLength;

        if (left.Value + right.Value > windowLength)
        {
            right.Value = Math.Max(right.Minimum, windowLength - left.Value);
        }

        left.Maximum = Math.Max(left.Minimum, windowLength - right.Value);
        right.Maximum = Math.Max(right.Minimum, windowLength - left.Value);
    }
}
