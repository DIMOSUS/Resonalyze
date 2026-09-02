using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// The highest point of a PEQ bank's NET response — preamp and every band
/// together, built at the processor's rate like the simulation builds it. A
/// bank whose net response rises above 0 dB anywhere asks the device for more
/// than unity somewhere, which is where a full-scale signal clips; a boost that
/// sits inside a wider cut, or under a negative preamp, asks for nothing. The
/// sign of an individual band says nothing about headroom; this figure does.
/// </summary>
internal static class AgentPeqHeadroom
{
    private const int GridPoints = 512;

    /// <summary>
    /// The net response's maximum (dB) and where it sits (Hz), over 20 Hz to the
    /// processor's Nyquist — the whole range a band may be placed in, not the
    /// audible one: a boost at 25 kHz on a 96 kHz device clips just the same.
    /// </summary>
    public static (double PeakDb, double PeakHz) Peak(
        double preampDb, IReadOnlyList<PeqBand> bands, int processorSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(bands);

        if (bands.Count == 0)
        {
            return (preampDb, 20);
        }

        PreparedDspResponse response = PreparedDspResponse.Create(
            new DspChannelChain(0, 0, false, CrossoverSpec.Off, new EqualizationCurve(bands, preampDb)),
            processorSampleRateHz);
        double highHz = processorSampleRateHz / 2.0 * 0.999;
        double peakDb = double.NegativeInfinity;
        double peakHz = AgentCurveSampling.BroadbandLowHz;
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(
            AgentCurveSampling.BroadbandLowHz, highHz, GridPoints))
        {
            double db = DataHelper.AmplitudeToDecibels(response.Response(frequency).Magnitude);
            if (db > peakDb)
            {
                peakDb = db;
                peakHz = frequency;
            }
        }

        return (peakDb, peakHz);
    }
}
