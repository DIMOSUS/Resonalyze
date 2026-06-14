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

            double[] halfWindow = TukeyWindow(window / 2, leftTukeyWindow * 2, rightTukeyWindow * 2);
            halfWindow.CopyTo(windowFunction, 0);

            return windowFunction;
        }
    }
}
