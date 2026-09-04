using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>
/// What an axis MEANS, as far as a stored range is concerned: what it is called and
/// the hard limits it is armed with. Re-arming those is how the app says "this axis
/// now shows something else".
/// </summary>
internal readonly record struct PlotAxisIdentity(
    string? Key,
    string? Title,
    Type AxisType,
    double AbsoluteMinimum,
    double AbsoluteMaximum);

/// <summary>
/// Whether a plot is still showing what a remembered range was taken from.
///
/// The model reference alone does not answer it. The Virtual DSP acoustic view
/// re-arms ONE axis object between dB, degrees and a unitless impulse scale without
/// replacing the model, and swaps the bottom axis between frequency and time in
/// place; the EQ wizard re-arms its dB axis for a new source. A range, an undo entry
/// or a zoom box held across such a change would be numbers in the units of a scale
/// that is no longer on screen — dB read as a normalized impulse — so anything that
/// remembers a range remembers this alongside it and drops what it holds when the
/// two stop matching.
/// </summary>
internal static class PlotAxisIdentities
{
    public static IReadOnlyList<PlotAxisIdentity> Describe(PlotModel? model) =>
        model == null
            ? Array.Empty<PlotAxisIdentity>()
            : model.Axes
                .Select(axis => new PlotAxisIdentity(
                    axis.Key,
                    axis.Title,
                    axis.GetType(),
                    axis.AbsoluteMinimum,
                    axis.AbsoluteMaximum))
                .ToList();

    /// <summary>
    /// True when <paramref name="model"/> is the same plot, showing the same
    /// quantities, that <paramref name="identities"/> was taken from.
    /// </summary>
    public static bool Match(
        PlotModel? model,
        PlotModel? rememberedModel,
        IReadOnlyList<PlotAxisIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        return model != null &&
            ReferenceEquals(model, rememberedModel) &&
            Describe(model).SequenceEqual(identities);
    }
}
