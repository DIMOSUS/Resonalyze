namespace Resonalyze.Dsp
{
    /// <summary>
    /// Creates asymmetric Tukey windows used to isolate impulse-response regions.
    /// </summary>
    public static class Windowing
    {
        public static double[] TukeyWindow(int window, double leftTukeyWindow, double rightTukeyWindow)
        {
            double[] windowFunction = new double[window];
            int nLeft = (int)Math.Round(leftTukeyWindow * (window - 1.0) * 0.5);
            int nRight = (int)Math.Round((window - 1.0) * (1.0 - rightTukeyWindow * 0.5));

            for (int i = 0; i < nLeft; i++)
            {
                windowFunction[i] = 0.5 * (1 + Math.Cos(Math.PI * (2.0 * i / (leftTukeyWindow * (window - 1.0)) - 1.0)));
            }

            for (int i = nLeft; i < nRight; i++)
            {
                windowFunction[i] = 1.0;
            }

            for (int i = nRight; i < window; i++)
            {
                windowFunction[i] = 0.5 * (1 + Math.Cos(Math.PI * (2.0 * i / (rightTukeyWindow * (window - 1.0)) - 2.0 / rightTukeyWindow + 1.0)));
                if (!double.IsFinite(windowFunction[i]))
                    windowFunction[i] = 1;
            }

            return windowFunction;
        }

        public static double[] TukeyWindowHalfZeroPadded(int window, double leftTukeyWindow, double rightTukeyWindow)
        {
            double[] windowFunction = new double[window];
            int halfWindowLength = window / 2;
            int leftFadeSamples = GetFadeSampleCount(window, leftTukeyWindow);
            int rightFadeSamples = GetFadeSampleCount(window, rightTukeyWindow);
            NormalizeFadeSampleCounts(
                halfWindowLength,
                ref leftFadeSamples,
                ref rightFadeSamples);

            for (int i = 0; i < halfWindowLength; i++)
            {
                windowFunction[i] =
                    GetLeftFadeWeight(i, leftFadeSamples) *
                    GetRightFadeWeight(i, halfWindowLength, rightFadeSamples);
            }

            return windowFunction;
        }

        private static int GetFadeSampleCount(int window, double tukeyWindow)
        {
            if (window <= 1 || tukeyWindow <= 0)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(tukeyWindow * (window - 1.0) * 0.5));
        }

        private static void NormalizeFadeSampleCounts(
            int windowLength,
            ref int leftFadeSamples,
            ref int rightFadeSamples)
        {
            int fadeTotal = leftFadeSamples + rightFadeSamples;
            if (windowLength <= 0 || fadeTotal <= windowLength)
            {
                return;
            }

            double scale = windowLength / (double)fadeTotal;
            leftFadeSamples = (int)Math.Round(leftFadeSamples * scale);
            rightFadeSamples = Math.Max(0, windowLength - leftFadeSamples);
        }

        private static double GetLeftFadeWeight(int index, int fadeSamples)
        {
            if (fadeSamples <= 0 || index >= fadeSamples)
            {
                return 1.0;
            }

            return 0.5 * (1.0 - Math.Cos(Math.PI * index / fadeSamples));
        }

        private static double GetRightFadeWeight(
            int index,
            int windowLength,
            int fadeSamples)
        {
            int distanceFromRightEdge = windowLength - 1 - index;
            if (fadeSamples <= 0 || distanceFromRightEdge >= fadeSamples)
            {
                return 1.0;
            }

            return 0.5 * (1.0 - Math.Cos(Math.PI * distanceFromRightEdge / fadeSamples));
        }
    }
}
