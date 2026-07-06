namespace Resonalyze.Dsp;

public static class DspMath
{
    /// <summary>
    /// Windowed-sinc (Lanczos) kernel with support <c>|x| &lt; a</c>. The shared
    /// interpolation kernel for log-frequency resampling, calibration lookup and
    /// sub-sample correlation-peak refinement.
    /// </summary>
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
