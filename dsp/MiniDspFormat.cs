using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// miniDSP advanced-biquad text (export only). Each PEQ band is converted to a
/// normalised biquad at a fixed sample rate; the preamp is emitted as a leading
/// gain biquad. Import is not supported because biquad coefficients do not map back
/// to a unique frequency/Q/gain.
/// </summary>
public sealed class MiniDspFormat : IEqProfileFormat
{
    // Biquad coefficients are only meaningful for the sample rate they were
    // computed at: the same file applied on a device processing at a different
    // rate lands every band on a different frequency and Q. The rate is a
    // constructor parameter and part of the visible format name, so the user
    // picks the file knowing which device family it fits instead of silently
    // getting 48 kHz coefficients.
    private readonly double sampleRateHz;
    private const string CoefficientFormat = "0.00000000";

    public MiniDspFormat(double sampleRateHz = 48_000)
    {
        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        this.sampleRateHz = sampleRateHz;
    }

    public string Name => $"miniDSP biquads ({sampleRateHz / 1000:0.#} kHz devices)";
    public string Extension => "txt";
    public bool CanImport => false;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var builder = new StringBuilder();
        int index = 1;

        // The preamp becomes a flat gain biquad (b0 = 10^(preamp/20), rest passthrough).
        if (curve.PreampDb != 0)
        {
            double gain = Math.Pow(10.0, curve.PreampDb / 20.0);
            AppendBiquad(builder, index++, new BiquadCoefficients(gain, 0, 0, 0, 0));
        }

        foreach (PeqBand band in curve.Bands)
        {
            AppendBiquad(builder, index++, PeakingBiquad.Compute(band, sampleRateHz));
        }

        return builder.ToString();
    }

    public EqualizationCurve Import(string text) =>
        throw new NotSupportedException("miniDSP biquad files cannot be imported.");

    private static void AppendBiquad(StringBuilder builder, int index, BiquadCoefficients c)
    {
        builder.Append("biquad").Append(index).AppendLine(",");
        builder.Append("b0=").Append(EqTextNumbers.Format(c.B0, CoefficientFormat)).AppendLine(",");
        builder.Append("b1=").Append(EqTextNumbers.Format(c.B1, CoefficientFormat)).AppendLine(",");
        builder.Append("b2=").Append(EqTextNumbers.Format(c.B2, CoefficientFormat)).AppendLine(",");
        builder.Append("a1=").Append(EqTextNumbers.Format(c.A1, CoefficientFormat)).AppendLine(",");
        builder.Append("a2=").Append(EqTextNumbers.Format(c.A2, CoefficientFormat)).AppendLine(",");
    }
}
