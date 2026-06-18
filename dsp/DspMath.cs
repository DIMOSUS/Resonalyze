namespace Resonalyze.Dsp;

public static class DspMath
{
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
}
