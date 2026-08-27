using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class ImpulseMeasurementView : IImpulseMeasurement
{
    private readonly Func<double, double> harmonicOffset;

    public ImpulseMeasurementView(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        Func<double, double>? harmonicOffset = null)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "Impulse response cannot be empty.",
                nameof(impulseResponse));
        }
        if ((uint)peakIndex >= (uint)impulseResponse.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(peakIndex));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        ImpulseResponse = impulseResponse;
        PeakIndex = peakIndex;
        SampleRate = sampleRate;
        this.harmonicOffset = harmonicOffset ?? (_ => 0.0);
    }

    public Complex[]? ImpulseResponse { get; }
    public int PeakIndex { get; }
    public int SampleRate { get; }

    /// <summary>
    /// Where this response stops carrying a measurement, for one whose protective
    /// high-pass was divided back out. Zero — the default — for every other view,
    /// including the derived ones: a SUM of two channels plays wherever either of
    /// them does, so one channel's limit is not the sum's.
    /// </summary>
    public double LowestMeasuredFrequencyHz { get; init; }

    public double HarmonicIROffset(double harmonic) => harmonicOffset(harmonic);
}
