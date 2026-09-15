using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public enum FirCrossoverMethod
{
    /// <summary>An IIR slope's magnitude with the phase discarded; an LR pair still sums flat.</summary>
    IirMagnitude,

    /// <summary>Windowed brick wall; window and length alone set the slope.</summary>
    WindowedSinc
}

public enum FirWindow
{
    Rectangular,
    Hann,
    Hamming,
    Blackman,
    Kaiser
}

/// <summary>Parameters of a symmetric, odd-length linear-phase crossover kernel, valid only at <see cref="SampleRateHz"/>.
/// See docs/tech/dsp-fir-crossover-design.md.</summary>
public sealed record FirCrossoverDesign(
    CrossoverKind Kind,
    CrossoverEdge LowPassEdge,
    CrossoverEdge HighPassEdge,
    FirCrossoverMethod Method,
    FirWindow Window,
    double KaiserBeta,
    int TapCount,
    int SampleRateHz)
{
    /// <summary>2^14 − 1; bounds latency (171 ms at 48 kHz). Imported kernels use <see cref="FirFilter.MaximumTaps"/>.</summary>
    public const int MaximumTapCount = 16_383;

    public const int MinimumTapCount = 3;

    public const double MaximumKaiserBeta = 20;

    /// <summary>Chebyshev excluded: a rippled passband defeats a linear-phase crossover.</summary>
    public static IReadOnlyList<CrossoverFilterFamily> IirFamilies { get; } =
    [
        CrossoverFilterFamily.LinkwitzRiley,
        CrossoverFilterFamily.Butterworth,
        CrossoverFilterFamily.Bessel
    ];

    public const int MaximumSlopeDbPerOctave = 96;

    /// <summary>Steeper than hardware (magnitude only): LR by 12, BW by 6 dB up to the max; Bessel's table ends at 48.</summary>
    public static IReadOnlyList<int> SupportedSlopes(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => LinkwitzRileySlopes,
        CrossoverFilterFamily.Butterworth => ButterworthSlopes,
        CrossoverFilterFamily.Bessel => CrossoverFilter.SupportedSlopes(family),
        _ => []
    };

    private static readonly int[] LinkwitzRileySlopes =
        Enumerable.Range(1, MaximumSlopeDbPerOctave / 12).Select(step => step * 12).ToArray();

    private static readonly int[] ButterworthSlopes =
        Enumerable.Range(1, MaximumSlopeDbPerOctave / 6).Select(step => step * 6).ToArray();

    public int LatencySamples => (TapCount - 1) / 2;

    public double LatencyMs => LatencySamples * 1_000.0 / SampleRateHz;

    public bool HasTargetMagnitude => Method == FirCrossoverMethod.IirMagnitude;

    /// <summary>A UI-ready sentence describing what is wrong, or null when the design can be built.</summary>
    public string? Problem()
    {
        if (Kind is not (CrossoverKind.LowPass or CrossoverKind.HighPass or CrossoverKind.BandPass))
        {
            return "A FIR crossover is a low-pass, a high-pass or a band-pass.";
        }
        if (!Enum.IsDefined(Method))
        {
            return "The design method is not one the constructor knows.";
        }
        if (!Enum.IsDefined(Window))
        {
            return "The window is not one the constructor knows.";
        }
        // Both edges for every method: an unknown family is a damaged design.
        if (!Enum.IsDefined(LowPassEdge.Family) || !Enum.IsDefined(HighPassEdge.Family))
        {
            return "A crossover family is not one the program knows.";
        }
        if (SampleRateHz <= 0)
        {
            return "The sample rate must be positive.";
        }
        if (TapCount is < MinimumTapCount or > MaximumTapCount)
        {
            return $"The kernel must have {MinimumTapCount} to {MaximumTapCount} taps.";
        }
        if (TapCount % 2 == 0)
        {
            return "The kernel must have an odd number of taps: an even-length symmetric " +
                "kernel cannot pass anything at Nyquist, so no high-pass can be built from one.";
        }
        if (Window == FirWindow.Kaiser && !(KaiserBeta is >= 0 and <= MaximumKaiserBeta))
        {
            return $"The Kaiser β must be between 0 and {MaximumKaiserBeta}.";
        }

        double nyquist = SampleRateHz / 2.0;
        if (UsesLowPass && EdgeProblem(LowPassEdge, "low-pass", nyquist) is { } lowProblem)
        {
            return lowProblem;
        }
        if (UsesHighPass && EdgeProblem(HighPassEdge, "high-pass", nyquist) is { } highProblem)
        {
            return highProblem;
        }
        if (Kind == CrossoverKind.BandPass && !(HighPassEdge.FrequencyHz < LowPassEdge.FrequencyHz))
        {
            return "A band-pass needs its high-pass corner below its low-pass corner.";
        }

        return null;
    }

    private bool UsesLowPass => Kind is CrossoverKind.LowPass or CrossoverKind.BandPass;

    private bool UsesHighPass => Kind is CrossoverKind.HighPass or CrossoverKind.BandPass;

    private string? EdgeProblem(CrossoverEdge edge, string name, double nyquist)
    {
        if (!(edge.FrequencyHz > 0 && edge.FrequencyHz < nyquist))
        {
            return $"The {name} corner must lie between 0 Hz and Nyquist ({nyquist:0.#} Hz).";
        }
        if (Method != FirCrossoverMethod.IirMagnitude)
        {
            return null;
        }
        if (!IirFamilies.Contains(edge.Family))
        {
            return $"The {name} family must be Linkwitz-Riley, Butterworth or Bessel.";
        }
        if (!SupportedSlopes(edge.Family).Contains(edge.SlopeDbPerOctave))
        {
            return $"The {name} slope is not one {edge.Family} offers.";
        }

        return null;
    }

    /// <summary>Windows the ideal zero-phase response; no gain renormalization (it would break the complementary sum).</summary>
    /// <exception cref="ArgumentException">The design has a <see cref="Problem"/>.</exception>
    public FirFilter Build()
    {
        if (Problem() is { } problem)
        {
            throw new ArgumentException(problem);
        }

        double[] ideal = Method == FirCrossoverMethod.WindowedSinc
            ? IdealSinc()
            : IdealFromMagnitude();
        int centre = LatencySamples;
        var taps = new double[TapCount];
        for (int i = 0; i < TapCount; i++)
        {
            taps[i] = ideal[i] * WindowValue(i - centre, centre);
        }

        return new FirFilter(taps, SampleRateHz);
    }

    public double TargetMagnitude(double frequencyHz)
    {
        if (Method == FirCrossoverMethod.WindowedSinc)
        {
            bool passes = (!UsesHighPass || frequencyHz >= HighPassEdge.FrequencyHz) &&
                (!UsesLowPass || frequencyHz <= LowPassEdge.FrequencyHz);
            return passes ? 1.0 : 0.0;
        }

        double magnitude = 1.0;
        if (UsesLowPass)
        {
            magnitude *= EdgeMagnitude(LowPassEdge, highPass: false, frequencyHz, bessel: null);
        }
        if (UsesHighPass)
        {
            magnitude *= EdgeMagnitude(HighPassEdge, highPass: true, frequencyHz, bessel: null);
        }

        return magnitude;
    }

    /// <summary>Closed-form BW/LR magnitude of the prewarped bilinear design; Bessel read off its sections.
    /// See docs/tech/dsp-fir-crossover-design.md#building-the-ideal-response.</summary>
    private double EdgeMagnitude(
        CrossoverEdge edge,
        bool highPass,
        double frequencyHz,
        IReadOnlyList<BiquadCoefficients>? bessel)
    {
        if (edge.Family == CrossoverFilterFamily.Bessel)
        {
            Complex response = Complex.One;
            foreach (BiquadCoefficients section in
                     bessel ?? CrossoverFilter.BuildSections(edge, highPass, SampleRateHz))
            {
                response *= BiquadResponse.Evaluate(section, frequencyHz, SampleRateHz);
            }

            return response.Magnitude;
        }

        double ratio = Math.Tan(Math.PI * frequencyHz / SampleRateHz) /
            Math.Tan(Math.PI * edge.FrequencyHz / SampleRateHz);
        double stopbandRatio = highPass ? 1.0 / ratio : ratio;
        int order = edge.SlopeDbPerOctave / 6;
        if (edge.Family == CrossoverFilterFamily.LinkwitzRiley)
        {
            return 1.0 / (1.0 + Math.Pow(stopbandRatio, order));
        }

        return 1.0 / Math.Sqrt(1.0 + Math.Pow(stopbandRatio, 2 * order));
    }

    /// <summary>Worst |kernel − target| in dB over bands where the target exceeds <paramref name="floorDb"/>; NaN without a target.</summary>
    public double WorstDeviationDb(FirFilter kernel, double floorDb = -30)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (!HasTargetMagnitude)
        {
            return double.NaN;
        }

        double highHz = Math.Min(20_000, SampleRateHz * 0.45);
        const int Points = 600;
        double worst = 0;
        double floor = Math.Pow(10, floorDb / 20);
        for (int i = 0; i <= Points; i++)
        {
            double frequency = 20 * Math.Pow(highHz / 20, (double)i / Points);
            double target = TargetMagnitude(frequency);
            if (target < floor)
            {
                continue;
            }

            double actual = kernel.Response(frequency, SampleRateHz).Magnitude;
            double deviation = Math.Abs(20 * Math.Log10(Math.Max(actual, 1e-12) / target));
            worst = Math.Max(worst, deviation);
        }

        return worst;
    }

    /// <summary>Large enough that the ideal response's ring decays before it folds back (1.36 s at 96 kHz for 2^18).</summary>
    public int MagnitudeGridLength => DspMath.NextPowerOfTwo(Math.Max(16 * TapCount, 1 << 18));

    private double[] IdealSinc()
    {
        int centre = LatencySamples;
        var ideal = new double[TapCount];
        for (int i = 0; i < TapCount; i++)
        {
            int n = i - centre;
            double value = 0;
            if (UsesLowPass)
            {
                value += Sinc(n, LowPassEdge.FrequencyHz);
            }
            else
            {
                value += n == 0 ? 1.0 : 0.0;
            }
            if (UsesHighPass)
            {
                value -= Sinc(n, HighPassEdge.FrequencyHz);
            }

            ideal[i] = value;
        }

        return ideal;
    }

    private double Sinc(int n, double cornerHz)
    {
        double normalized = 2.0 * cornerHz / SampleRateHz;
        return n == 0 ? normalized : Math.Sin(Math.PI * normalized * n) / (Math.PI * n);
    }

    private double[] IdealFromMagnitude()
    {
        int length = MagnitudeGridLength;
        int half = length / 2;
        var spectrum = new Complex[length];
        // Bessel sections built once, not per bin.
        IReadOnlyList<BiquadCoefficients>? lowBessel = UsesLowPass && LowPassEdge.Family == CrossoverFilterFamily.Bessel
            ? CrossoverFilter.BuildSections(LowPassEdge, highPass: false, SampleRateHz)
            : null;
        IReadOnlyList<BiquadCoefficients>? highBessel = UsesHighPass && HighPassEdge.Family == CrossoverFilterFamily.Bessel
            ? CrossoverFilter.BuildSections(HighPassEdge, highPass: true, SampleRateHz)
            : null;
        for (int k = 0; k <= half; k++)
        {
            double frequency = (double)k * SampleRateHz / length;
            double magnitude = 1.0;
            if (UsesLowPass)
            {
                magnitude *= EdgeMagnitude(LowPassEdge, highPass: false, frequency, lowBessel);
            }
            if (UsesHighPass)
            {
                magnitude *= EdgeMagnitude(HighPassEdge, highPass: true, frequency, highBessel);
            }

            spectrum[k] = magnitude;
            if (k > 0 && k < half)
            {
                spectrum[length - k] = magnitude;
            }
        }

        // Matlab scaling: the inverse is the impulse whose DFT is the magnitude itself.
        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        int centre = LatencySamples;
        var ideal = new double[TapCount];
        for (int i = 0; i < TapCount; i++)
        {
            int n = i - centre;
            ideal[i] = spectrum[(n + length) % length].Real;
        }

        return ideal;
    }

    // Stretched over c + 1 so the ends stop short of zero; exactly 1 at centre (the complementary sum relies on it).
    private double WindowValue(int n, int c)
    {
        double x = (double)n / (c + 1);
        return Window switch
        {
            FirWindow.Hann => 0.5 + 0.5 * Math.Cos(Math.PI * x),
            FirWindow.Hamming => 0.54 + 0.46 * Math.Cos(Math.PI * x),
            FirWindow.Blackman =>
                0.42 + 0.5 * Math.Cos(Math.PI * x) + 0.08 * Math.Cos(2 * Math.PI * x),
            FirWindow.Kaiser =>
                SpecialFunctions.BesselI0(KaiserBeta * Math.Sqrt(1 - x * x)) /
                SpecialFunctions.BesselI0(KaiserBeta),
            _ => 1.0
        };
    }
}
