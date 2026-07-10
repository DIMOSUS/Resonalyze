namespace Resonalyze.Dsp
{
    /// <summary>
    /// Analysis window applied before the FFT in spectrum measurements.
    /// </summary>
    public enum WindowType
    {
        Hann,
        FlatTop,
        BlackmanHarris,
        Rectangular
    }

    /// <summary>
    /// Creates asymmetric Tukey windows used to isolate impulse-response regions
    /// and symmetric analysis windows applied before spectral FFTs.
    /// </summary>
    public static class Windowing
    {
        /// <summary>
        /// Builds a symmetric analysis window of the requested type and length.
        /// </summary>
        // One-entry cache for the analysis window: a live session recomputes
        // the same (type, length) window thousands of times — once per
        // analysis frame plus once per UI snapshot — each costing `length`
        // trig calls and an allocation right next to the audio pipeline. The
        // cached array is SHARED: callers must treat it as read-only (all
        // in-repo callers only multiply by its values).
        private sealed record CachedAnalysisWindow(
            WindowType Type,
            int Length,
            double[] Window);

        private static volatile CachedAnalysisWindow? analysisWindowCache;

        /// <summary>
        /// The cached shared window — internal, and the caller MUST NOT write
        /// to the returned array (every in-repo caller only multiplies by its
        /// values). The public <see cref="CreateAnalysisWindow"/> hands out a
        /// private copy instead, so external code cannot corrupt the cache.
        /// </summary>
        internal static double[] SharedAnalysisWindow(WindowType windowType, int length)
        {
            if (length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            CachedAnalysisWindow? cached = analysisWindowCache;
            if (cached != null && cached.Type == windowType && cached.Length == length)
            {
                return cached.Window;
            }

            var window = new double[length];
            for (int i = 0; i < length; i++)
            {
                window[i] = AnalysisWindowValue(windowType, i, length);
            }

            analysisWindowCache = new CachedAnalysisWindow(windowType, length, window);
            return window;
        }

        public static double[] CreateAnalysisWindow(WindowType windowType, int length)
        {
            double[] shared = SharedAnalysisWindow(windowType, length);
            var copy = new double[shared.Length];
            Array.Copy(shared, copy, shared.Length);
            return copy;
        }

        private static double AnalysisWindowValue(WindowType windowType, int index, int length)
        {
            if (length <= 1)
            {
                return 1.0;
            }

            double phase = 2.0 * Math.PI * index / (length - 1);
            return windowType switch
            {
                WindowType.Rectangular => 1.0,
                WindowType.Hann => 0.5 - 0.5 * Math.Cos(phase),
                WindowType.BlackmanHarris =>
                    0.35875
                    - 0.48829 * Math.Cos(phase)
                    + 0.14128 * Math.Cos(2.0 * phase)
                    - 0.01168 * Math.Cos(3.0 * phase),
                // SRS five-term flat-top: excellent amplitude accuracy for tones.
                WindowType.FlatTop =>
                    0.21557895
                    - 0.41663158 * Math.Cos(phase)
                    + 0.277263158 * Math.Cos(2.0 * phase)
                    - 0.083578947 * Math.Cos(3.0 * phase)
                    + 0.006947368 * Math.Cos(4.0 * phase),
                _ => 0.5 - 0.5 * Math.Cos(phase)
            };
        }

        /// <summary>
        /// Asymmetric Tukey window: a cosine fade-in over the leftmost
        /// <c>leftTukeyWindow / 2</c> of the window, a unity plateau, and a cosine
        /// fade-out over the rightmost <c>rightTukeyWindow / 2</c>. Negative fade
        /// fractions are treated as zero; fades that together would exceed the whole
        /// window are scaled down proportionally so they meet without overlapping.
        /// </summary>
        public static double[] TukeyWindow(int window, double leftTukeyWindow, double rightTukeyWindow)
        {
            double[] windowFunction = new double[window];
            if (window == 0)
            {
                return windowFunction;
            }
            if (window == 1)
            {
                windowFunction[0] = 1.0;
                return windowFunction;
            }

            double left = Math.Max(0.0, leftTukeyWindow);
            double right = Math.Max(0.0, rightTukeyWindow);
            double total = left + right;
            if (total > 2.0)
            {
                left *= 2.0 / total;
                right *= 2.0 / total;
            }

            int nLeft = (int)Math.Round(left * (window - 1.0) * 0.5);
            int nRight = right > 0
                ? (int)Math.Round((window - 1.0) * (1.0 - right * 0.5))
                : window;

            for (int i = 0; i < nLeft; i++)
            {
                windowFunction[i] = 0.5 * (1 + Math.Cos(Math.PI * (2.0 * i / (left * (window - 1.0)) - 1.0)));
            }

            for (int i = nLeft; i < Math.Min(nRight, window); i++)
            {
                windowFunction[i] = 1.0;
            }

            for (int i = Math.Max(nLeft, nRight); i < window; i++)
            {
                windowFunction[i] = 0.5 * (1 + Math.Cos(Math.PI * (2.0 * i / (right * (window - 1.0)) - 2.0 / right + 1.0)));
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
