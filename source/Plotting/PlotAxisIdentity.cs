using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>An axis's key and hard limits; re-arming them means the axis now shows something else.</summary>
internal readonly record struct PlotAxisIdentity(
    string? Key,
    string? Title,
    Type AxisType,
    double AbsoluteMinimum,
    double AbsoluteMaximum);

/// <summary>Whether a plot still shows what a remembered range, undo entry or zoom box was taken from. See docs/tech/plot-interaction.md#axis-identity.</summary>
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
