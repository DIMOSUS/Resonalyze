using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// How a linear-phase crossover kernel gets its magnitude.
/// </summary>
public enum FirCrossoverMethod
{
    /// <summary>
    /// The magnitude of an IIR crossover slope — the family, corner and steepness the
    /// Virtual DSP crossovers offer — with the phase thrown away. A Linkwitz-Riley
    /// pair designed this way sums flat, exactly as its IIR original does in
    /// magnitude, but with no phase turn at the corner.
    /// </summary>
    IirMagnitude,

    /// <summary>
    /// An ideal brick wall at the corner, truncated by the window: the classic
    /// windowed-sinc design. The window and the length alone set the slope.
    /// </summary>
    WindowedSinc
}

/// <summary>The window a kernel is truncated with.</summary>
public enum FirWindow
{
    Rectangular,
    Hann,
    Hamming,
    Blackman,
    Kaiser
}

/// <summary>
/// The parameters a linear-phase crossover kernel is designed from — what the FIR
/// Constructor edits and what a session keeps beside the kernel, so the kernel can
/// be opened again as the crossover it is rather than as a list of numbers.
/// </summary>
/// <remarks>
/// <para>
/// Every kernel designed here is SYMMETRIC with an ODD number of taps. Symmetry is
/// what makes it linear-phase: the whole kernel is a pure delay of
/// <see cref="LatencySamples"/> times a real, zero-phase response. The odd length is
/// not a preference: a symmetric kernel of even length has a forced zero at Nyquist,
/// so no high-pass can be built from one, and its delay would fall half a sample off
/// the grid.
/// </para>
/// <para>
/// Designed AT <see cref="SampleRateHz"/>. The taps are the filter only at that rate;
/// a processor running at another convolves the same numbers as another filter, so a
/// session compares this rate with its processor's and flags the kernel for a
/// rebuild rather than trusting it.
/// </para>
/// <para>
/// Both methods keep one property a crossover needs: the window multiplies an ideal
/// zero-phase response, and every window is exactly 1 at the kernel's centre. A
/// low-pass and a high-pass of one corner, one length and one window therefore sum
/// to a pure delay whenever their IDEAL responses add to a unit impulse — always for
/// the windowed sinc, whose high-pass is that impulse minus the low-pass, and for
/// <see cref="CrossoverFilterFamily.LinkwitzRiley"/> in the IIR-magnitude method,
/// whose two magnitudes add to one (a Butterworth pair adds in power, not in
/// magnitude, and sums with a bump).
/// </para>
/// </remarks>
/// <param name="Kind">Low-pass, high-pass or band-pass; never Off.</param>
/// <param name="LowPassEdge">
/// The low-pass corner (read for LowPass and BandPass). Its family and slope are read
/// by <see cref="FirCrossoverMethod.IirMagnitude"/> only.
/// </param>
/// <param name="HighPassEdge">The high-pass corner (read for HighPass and BandPass).</param>
/// <param name="Method">How the magnitude is obtained.</param>
/// <param name="Window">The window the kernel is truncated with.</param>
/// <param name="KaiserBeta">The Kaiser window's β; read only for that window.</param>
/// <param name="TapCount">The kernel length: odd, 3 to <see cref="MaximumTapCount"/>.</param>
/// <param name="SampleRateHz">The rate the kernel is designed for.</param>
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
    /// <summary>
    /// The longest kernel the constructor designs: 16383 taps, 2^14 − 1, the largest odd
    /// count a 16k-tap stage holds. The limit is on latency, not on arithmetic — a
    /// linear-phase kernel delays the channel by half its length, 171 ms at 48 kHz here
    /// — and it binds the constructor only: an imported kernel is held to
    /// <see cref="FirFilter.MaximumTaps"/>.
    /// </summary>
    public const int MaximumTapCount = 16_383;

    /// <summary>The shortest kernel that still has a centre and two sides.</summary>
    public const int MinimumTapCount = 3;

    /// <summary>The largest Kaiser β accepted — far past any useful sidelobe level.</summary>
    public const double MaximumKaiserBeta = 20;

    /// <summary>
    /// The IIR families whose magnitude the constructor offers. Chebyshev is left out:
    /// its ripple is a parameter of its own, and a crossover built on a rippled
    /// passband is not what a linear-phase design is for.
    /// </summary>
    public static IReadOnlyList<CrossoverFilterFamily> IirFamilies { get; } =
    [
        CrossoverFilterFamily.LinkwitzRiley,
        CrossoverFilterFamily.Butterworth,
        CrossoverFilterFamily.Bessel
    ];

    /// <summary>The steepest slope the constructor offers, in dB per octave.</summary>
    public const int MaximumSlopeDbPerOctave = 96;

    /// <summary>
    /// The slopes the constructor offers for an IIR family — steeper than a hardware
    /// crossover's list (see <see cref="CrossoverFilter.SupportedSlopes"/>), because a
    /// kernel only takes the slope's MAGNITUDE and has no sections to run:
    /// Linkwitz-Riley in 12 dB steps and Butterworth in 6 dB steps up to
    /// <see cref="MaximumSlopeDbPerOctave"/>. Bessel keeps the hardware list: its
    /// prototype is a table that ends at 48 dB per octave.
    /// </summary>
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

    /// <summary>
    /// The delay the kernel adds, in samples: its centre, (N − 1) / 2 — a whole
    /// number, since the length is odd.
    /// </summary>
    public int LatencySamples => (TapCount - 1) / 2;

    /// <summary>The same delay in milliseconds at <see cref="SampleRateHz"/>.</summary>
    public double LatencyMs => LatencySamples * 1_000.0 / SampleRateHz;

    /// <summary>
    /// Whether the design names a response a kernel of finite length approximates —
    /// true of the IIR-magnitude method. A brick wall has no finite-slope shape to
    /// compare with, so the windowed sinc answers false.
    /// </summary>
    public bool HasTargetMagnitude => Method == FirCrossoverMethod.IirMagnitude;

    /// <summary>
    /// What is wrong with the design, or null when it can be built. One sentence the
    /// UI can show as it stands.
    /// </summary>
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

    /// <summary>
    /// Designs the kernel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ideal zero-phase response is built first and only then windowed. For the
    /// windowed sinc that response is analytic — the sinc of each corner, the
    /// high-pass as a unit impulse minus the low-pass. For the IIR magnitude it is
    /// the inverse DFT of the magnitude sampled on a grid many times the kernel's
    /// length (<see cref="MagnitudeGridLength"/>), which folds the ideal response's
    /// tail back onto itself only where that tail has long since decayed.
    /// </para>
    /// <para>
    /// No gain renormalization follows: the passband comes out at unity to within
    /// what the length allows, and scaling it would break the complementary sum the
    /// type remarks promise.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The magnitude the IIR-magnitude method samples, at one frequency: the product
    /// of the used edges' IIR magnitudes at <see cref="SampleRateHz"/>. For the
    /// windowed sinc, the ideal brick wall (1 in the passband, 0 outside it).
    /// </summary>
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

    /// <summary>
    /// One edge's digital magnitude at <see cref="SampleRateHz"/>.
    /// </summary>
    /// <remarks>
    /// Butterworth and Linkwitz-Riley in closed form. The crossover's biquads are the
    /// bilinear transform of the analog prototype prewarped at the corner, so the
    /// magnitude is exactly |B|² = 1 / (1 + r^(2n)) with r = tan(πf/fs) / tan(πfc/fs)
    /// (inverted for a high-pass), and a Linkwitz-Riley of order 2n is that Butterworth
    /// squared: |LR| = 1 / (1 + r^(2n)). That is what lets the constructor offer orders
    /// no section list carries. The high-pass is written with the inverted ratio rather
    /// than as r^(2n) / (1 + r^(2n)), which would divide infinity by infinity once a
    /// 96 dB/oct slope's ratio overflows. Bessel has no closed form here and is read off
    /// its sections, which the caller builds once and passes in (or null to build them).
    /// </remarks>
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

    /// <summary>
    /// The worst difference, in dB, between a kernel's magnitude and
    /// <see cref="TargetMagnitude"/> over the audio band, counted only where the
    /// target stands above <paramref name="floorDb"/> — below that the target heads
    /// for minus infinity and any finite kernel "misses" it by an unbounded amount
    /// that says nothing. NaN for a design without a target shape.
    /// </summary>
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

    /// <summary>
    /// The grid the IIR magnitude is sampled on before its inverse DFT: a power of two
    /// at least sixteen kernels long and never under 2^18 points. The ideal response
    /// of a steep, low corner rings for a long time, and whatever of it runs past half
    /// the grid folds back into the kernel; at 2^18 the fold starts 1.36 s out at
    /// 96 kHz, past the ring of a 48 dB/oct corner at 20 Hz.
    /// </summary>
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
                // High-pass alone: the complement of an all-pass, a unit impulse.
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

    // The ideal low-pass at a corner, one sample: 2·fc/fs · sinc(2·fc·n/fs).
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
        // A Bessel edge's sections are built once, not per bin: a quarter of a million
        // bins would rebuild them every time. The other families need none.
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

        // A real, even spectrum inverts to a real, even sequence centred on sample 0;
        // Matlab scaling divides by the grid length, so the inverse is the impulse
        // whose DFT is the magnitude itself.
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

    // The window at offset n from the centre of a kernel whose centre is at c. Every
    // window is stretched over c + 1 rather than c, so its ends stop just short of
    // zero instead of spending the outermost taps on nothing; each is exactly 1 at
    // the centre, which the complementary sum relies on.
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
