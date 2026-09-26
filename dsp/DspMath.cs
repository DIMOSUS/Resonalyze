namespace Resonalyze.Dsp;

public static class DspMath
{
    /// <summary>Lanczos kernel with support <c>|x| &lt; a</c>.</summary>
    public static double LanczosKernel(double x, double a)
    {
        if (Math.Abs(x) < 1e-5)
        {
            return 1.0;
        }
        if (Math.Abs(x) >= a)
        {
            return 0.0;
        }

        double piX = Math.PI * x;
        return a * Math.Sin(piX) * Math.Sin(piX / a) / (piX * piX);
    }

    public static int WrapIndex(int index, int length)
    {
        int wrapped = index % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    /// <summary>A circular record's position as a lag: past the middle it is negative time wrapped to the end.</summary>
    public static double ToSignedLag(double position, int length) =>
        position <= length * 0.5 ? position : position - length;

    /// <summary>How many of a circular record's samples <see cref="ToSignedLag"/> puts before zero.</summary>
    public static int NegativeLagCount(int length) => Math.Max(0, (length - 1) / 2);

    public static int NextPowerOfTwo(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        int result = 1;
        while (result < value)
        {
            if (result > int.MaxValue / 2)
            {
                throw new InvalidOperationException("The requested transform length is too large.");
            }
            result <<= 1;
        }

        return result;
    }

    internal static double RaisedCosineGate(double value, double low, double high)
    {
        if (value <= low)
        {
            return 0.0;
        }
        if (value >= high)
        {
            return 1.0;
        }

        return 0.5 - 0.5 * Math.Cos(Math.PI * (value - low) / (high - low));
    }
}
