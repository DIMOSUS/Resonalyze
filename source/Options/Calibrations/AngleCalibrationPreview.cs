using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal readonly record struct AngleCorrectionPoint(double FrequencyHz, double CenterDb, double LowerDb, double UpperDb);

/// <summary>What the estimate dialog previews: the correction and its reference spread on a log grid, and the summary.</summary>
internal sealed record AngleCalibrationPreview(IReadOnlyList<AngleCorrectionPoint> Points, string Summary)
{
    public const double MinimumHz = 20.0;
    public const double MaximumHz = 20_000.0;
    private const int PointCount = 240;

    public static AngleCalibrationPreview Build(MicrophoneAngleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(request);
        var points = new List<AngleCorrectionPoint>(PointCount);
        double widest = 0;
        double widestHz = MaximumHz;
        for (int index = 0; index < PointCount; index++)
        {
            double frequency = MinimumHz * Math.Pow(MaximumHz / MinimumHz, index / (double)(PointCount - 1));
            MicrophoneAngleBounds bounds = estimate.Deltas(frequency);
            points.Add(new AngleCorrectionPoint(frequency, bounds.CenterDb, bounds.LowerDb, bounds.UpperDb));
            double spread = bounds.UpperDb - bounds.LowerDb;
            if (spread > widest)
            {
                widest = spread;
                widestHz = frequency;
            }
        }

        return new AngleCalibrationPreview(points, Describe(estimate, points[^1].CenterDb, widest, widestHz));
    }

    private static string Describe(
        MicrophoneAngleEstimate estimate,
        double topOfBandDb,
        double widestSpreadDb,
        double widestSpreadHz)
    {
        string references = estimate.References.Count == 0
            ? "no reference"
            : string.Join(" · ", estimate.References);
        // References hold their last value past their range; say where the first one stops rather than implying a measured flat top.
        string held = estimate.HighestSupportedFrequencyHz < MaximumHz
            ? $" Modelled to {FrequencyText.Format(estimate.HighestSupportedFrequencyHz)}; " +
              "references hold above that."
            : string.Empty;
        // A single comparable reference has nothing to disagree with; 0.00 dB would overstate confidence.
        string spread = widestSpreadDb >= 0.005
            ? $", references disagreeing by up to {widestSpreadDb:0.00} dB " +
              $"around {FrequencyText.Format(widestSpreadHz)}"
            : ", from a single reference, so no spread is shown";
        return
            $"Estimated, not measured: {topOfBandDb:+0.00;-0.00;0.00} dB at 20 kHz" +
            $"{spread}.\r\nBuilt from: {references}.{held}";
    }
}
