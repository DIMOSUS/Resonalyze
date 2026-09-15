using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>LIVE settings objects held by reference: panels and the settings file edit them in place. Do not copy values out.</summary>
internal sealed record PlotPresentationOptions(
    FrequencyResponseOptions FrequencyResponse,
    FrequencyResponseOptions PhaseResponse,
    FrequencyResponseOptions GroupDelay,
    CurveVisibilityOptions FrequencyResponseVisibility,
    CurveVisibilityOptions PhaseResponseVisibility,
    CurveVisibilityOptions GroupDelayVisibility,
    ImpulseResponseOptions ImpulseResponse,
    LiveSpectrumOptions LiveSpectrum,
    WaterfallGenerateOptions Waterfall,
    WaterfallGenerateOptions BurstDecay);
