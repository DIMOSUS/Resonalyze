using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Resonalyze.Dsp
{
    public static class Windowing
    {
        public static double ADCtoPdB(double val)
        {
            return 20.0 * Math.Log10(Math.Max(val, 0.00000001));
        }

        public static double PdBtoADC(double val)
        {
            return Math.Pow(10.0, val / 20.0);
        }

        public static double[] TukeyWindow(int window, double leftTukeyWindow, double rightTukeyWindow)
        {
            double[] winFunc = new double[window];
            int nLeft = (int)Math.Round(leftTukeyWindow * (window - 1.0) * 0.5);
            int nRight = (int)Math.Round((window - 1.0) * (1.0 - rightTukeyWindow * 0.5));

            for (int i = 0; i < nLeft; i++)
            {
                winFunc[i] = 0.5 * (1 + Math.Cos(Math.PI * (2.0 * i / (leftTukeyWindow * (window - 1.0)) - 1.0)));
            }

            for (int i = nLeft; i < nRight; i++)
            {
                winFunc[i] = 1.0;
            }

            for (int i = nRight; i < window; i++)
            {
                winFunc[i] = 0.5 * (1 + Math.Cos(Math.PI * (2.0 * i / (rightTukeyWindow * (window - 1.0)) - 2.0 / rightTukeyWindow + 1.0)));
                if (!winFunc[i].IsFinite())
                    winFunc[i] = 1;
            }

            return winFunc;
        }

        public static double[] TukeyWindowHalfZeroPadded(int window, double leftTukeyWindow, double rightTukeyWindow)
        {
            double[] winFunc = new double[window];

            double[] halfWinFunc = TukeyWindow(window / 2, leftTukeyWindow * 2, rightTukeyWindow * 2);
            halfWinFunc.CopyTo(winFunc, 0);

            return winFunc;
        }
    }
}
