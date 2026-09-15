using System.Text;

namespace Resonalyze.Dsp;

/// <summary>miniDSP biquad text, export only: coefficients do not map back to unique frequency/Q/gain.</summary>
public sealed class MiniDspFormat : IEqProfileFormat
{
    // Coefficients are rate-specific, so the rate is part of the visible format name.
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

        if (curve.PreampDb != 0)
        {
            double gain = Math.Pow(10.0, curve.PreampDb / 20.0);
            AppendBiquad(builder, index++, new BiquadCoefficients(gain, 0, 0, 0, 0));
        }

        foreach (PeqBand band in curve.Bands)
        {
            AppendBiquad(builder, index++, PeqBiquad.Compute(band, sampleRateHz));
        }

        return builder.ToString();
    }

    public bool TryImport(string text, out EqualizationCurve curve) =>
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
