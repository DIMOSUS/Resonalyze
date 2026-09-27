using System.Runtime.CompilerServices;
using OxyPlot;
using OxyPlot.WindowsForms;

namespace Resonalyze;

/// <summary>What the stock <see cref="PlotView"/>'s OnPaint reads before drawing, for a view that replaces that OnPaint.</summary>
/// <remarks>Private fields of OxyPlot.WindowsForms 2.2.0; <c>PlotViewPaintStateTests</c> fails when an upgrade renames them.</remarks>
internal static class PlotViewPaintState
{
    /// <summary>Applies a pending <see cref="PlotView.InvalidatePlot"/> to the model, as the stock paint does first.</summary>
    public static void ApplyPendingUpdate(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (InvalidateLock(view))
        {
            if (!IsModelInvalidated(view))
            {
                return;
            }

            if (view.Model != null)
            {
                ((IPlotModel)view.Model).Update(UpdateDataFlag(view));
                UpdateDataFlag(view) = false;
            }

            IsModelInvalidated(view) = false;
        }
    }

    /// <summary>The box <see cref="PlotView.ShowZoomRectangle"/> asked for; empty when none is shown.</summary>
    public static Rectangle ZoomRectangle(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return ZoomRectangleField(view);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "invalidateLock")]
    private static extern ref object InvalidateLock(PlotView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "isModelInvalidated")]
    private static extern ref bool IsModelInvalidated(PlotView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "updateDataFlag")]
    private static extern ref bool UpdateDataFlag(PlotView view);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "zoomRectangle")]
    private static extern ref Rectangle ZoomRectangleField(PlotView view);
}
