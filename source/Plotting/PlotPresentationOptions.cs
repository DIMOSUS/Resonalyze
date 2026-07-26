using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// The per-mode presentation settings <see cref="PlotModelFactory"/> draws with,
/// bundled so adding a mode does not widen a constructor that had grown to
/// thirteen positional arguments.
///
/// These are the LIVE settings objects, held by reference on purpose: the
/// settings file and the option panels write into the same instances, and the
/// factory is expected to see those edits on the next plot build. Do not copy
/// values out of them.
/// </summary>
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
