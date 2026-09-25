using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The bank's handles on the EQ axis, numbered as the strips. It only reports what the pointer did (press, where a drag
/// went, wheel notches over the selected handle); the panel turns that into bank edits and redraws. The handles being
/// worked (selected, hovered, held) and the readout draw over the curves; the rest go under them, on
/// <see cref="FaintLayer"/>.
/// </summary>
internal sealed class EqBandHandlesAnnotation : Annotation, IPlotDragHandles
{
    public const double Radius = 8;

    // A band not being worked is a dot; the pointer still grabs the whole disc.
    private const double DotRadius = 3.5;
    private const double HitSlack = 2;
    private const int WheelNotch = 120;
    private const double NumberFontSize = 10;
    // Two digits at the full size touch the ring; a bank can run to 32 filters.
    private const double TwoDigitFontSize = 8.5;
    // Measured off a render: the renderer centres the advance box, whose side bearings and descender space the digits
    // do not fill, so their ink lands beside the disc's centre without these.
    private const double NumberNudgeXPx = 0.45;
    private const double NumberNudgeYPx = 0.5;
    private const double TwoDigitNudgeYPx = 0.1;
    private const double LabelGap = 6;
    private const double LabelPaddingX = 6;
    private const double LabelPaddingY = 3;

    private static readonly OxyColor Fill = UiPalette.PlotOverlayFillHovered.ToOxy();
    private static readonly OxyColor Ink = UiPalette.CurveEqBank.ToOxy();
    private static readonly OxyColor DotFill = OxyColor.FromAColor(210, Ink);
    private static readonly OxyColor SelectedFill = UiPalette.CurveBandOverlay.ToOxy();
    private static readonly OxyColor SelectedInk = UiPalette.GraphSurface.ToOxy();
    private static readonly OxyColor LabelFill = UiPalette.PlotZoomLabelFill.ToOxy();
    private static readonly OxyColor LabelStroke = UiPalette.PlotZoomLabelStroke.ToOxy();

    private IReadOnlyList<PeqBand> bands = Array.Empty<PeqBand>();
    private int? selected;
    private int? hovered;
    private int? dragged;
    private ScreenVector grabOffset;
    private int wheelRemainder;

    public EqBandHandlesAnnotation()
    {
        Layer = AnnotationLayer.AboveSeries;
        FaintLayer = new FaintHandles(this);
    }

    /// <summary>Goes into the model with this annotation: draws the handles not being worked, under the curves.</summary>
    public Annotation FaintLayer { get; }

    /// <summary>A band's handle was pressed (index into the bank).</summary>
    public event Action<int>? Pressed;

    /// <summary>Where the dragged handle is now: band index, frequency (Hz) and level on the EQ axis (dB).</summary>
    public event Action<int, double, double>? Dragged;

    public event Action? Released;

    /// <summary>Wheel notches over the selected handle; positive narrows.</summary>
    public event Action<int, int>? QStepped;

    public IReadOnlyList<PeqBand> Bands => bands;

    public int? Selected => selected;

    /// <summary>A handle is held: the bank's numbering must not move under it until it is let go.</summary>
    public bool Dragging => dragged != null;

    /// <summary>An empty list hides every handle.</summary>
    public void Show(IReadOnlyList<PeqBand> shown, int? selectedIndex)
    {
        bands = shown;
        int? wanted = selectedIndex < shown.Count ? selectedIndex : null;
        if (wanted != selected)
        {
            // Part of a notch belongs to the band it was turned over, not to the next one selected.
            wheelRemainder = 0;
        }

        selected = wanted;
        if (hovered >= shown.Count)
        {
            hovered = null;
        }

        if (dragged >= shown.Count)
        {
            dragged = null;
        }
    }

    public ScreenPoint Center(int index) =>
        Transform(new DataPoint(bands[index].FrequencyHz, EqBandHandles.LevelDb(bands[index])));

    public int? HitTest(ScreenPoint point)
    {
        if (XAxis == null || YAxis == null || PlotModel == null)
        {
            return null;
        }

        const double reach = Radius + HitSlack;
        foreach (int index in DrawOrder().Reverse())
        {
            ScreenPoint center = Center(index);
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            if (Shown(center) && (dx * dx) + (dy * dy) <= reach * reach)
            {
                return index;
            }
        }

        return null;
    }

    public bool Hover(int? handle)
    {
        if (dragged != null || hovered == handle)
        {
            return false;
        }

        hovered = handle;
        return true;
    }

    public void Press(int handle, ScreenPoint point)
    {
        if (handle >= bands.Count)
        {
            return;
        }

        dragged = handle;
        hovered = handle;
        grabOffset = Center(handle) - point;
        Pressed?.Invoke(handle);
    }

    // The grab offset keeps the handle where it was taken, so a press off its centre does not jump the band.
    public void Drag(ScreenPoint point)
    {
        if (dragged is not int index || index >= bands.Count)
        {
            return;
        }

        DataPoint target = InverseTransform(point + grabOffset);
        Dragged?.Invoke(index, target.X, target.Y);
    }

    public void Release()
    {
        if (dragged == null)
        {
            return;
        }

        dragged = null;
        Released?.Invoke();
    }

    public bool Wheel(int handle, int delta)
    {
        if (handle != selected || handle >= bands.Count || !EqBandHandles.HasQ(bands[handle].Type))
        {
            return false;
        }

        // Accumulated so a touchpad's small deltas add up to notches instead of each stepping a whole one.
        wheelRemainder += delta;
        int notches = wheelRemainder / WheelNotch;
        if (notches != 0)
        {
            wheelRemainder -= notches * WheelNotch;
            QStepped?.Invoke(handle, notches);
        }

        return true;
    }

    public bool Raised(int index) => index == selected || index == hovered || index == dragged;

    public override void Render(IRenderContext rc)
    {
        base.Render(rc);
        RenderHandles(rc, raised: true);
        if ((dragged ?? hovered) is int described && described < bands.Count)
        {
            RenderReadout(rc, described);
        }
    }

    private void RenderHandles(IRenderContext rc, bool raised)
    {
        foreach (int index in DrawOrder())
        {
            ScreenPoint center = Center(index);
            if (Raised(index) != raised || !Shown(center))
            {
                continue;
            }

            if (!raised)
            {
                rc.DrawCircle(center, DotRadius, DotFill, OxyColors.Undefined, 0, EdgeRenderingMode.PreferGeometricAccuracy);
                continue;
            }

            bool isSelected = index == selected;
            rc.DrawCircle(
                center,
                Radius,
                isSelected ? SelectedFill : Fill,
                isSelected ? SelectedFill : Ink,
                2,
                EdgeRenderingMode.PreferGeometricAccuracy);
            DrawNumber(rc, center, index + 1, isSelected ? SelectedInk : Ink);
        }
    }

    /// <remarks>Digit by digit, each centred in a cell as wide as a digit: centring the whole string puts "1" and
    /// "12" in different places, because the side bearings differ per glyph.</remarks>
    private void DrawNumber(IRenderContext rc, ScreenPoint center, int number, OxyColor ink)
    {
        string text = number.ToString(CultureInfo.InvariantCulture);
        bool wide = text.Length > 1;
        double size = wide ? TwoDigitFontSize : NumberFontSize;
        double cell = rc.MeasureText("0", ActualFont, size, FontWeights.Bold).Width;
        double baseline = center.Y + (wide ? TwoDigitNudgeYPx : NumberNudgeYPx);
        double left = center.X + NumberNudgeXPx - (cell * (text.Length - 1) / 2);
        for (int digit = 0; digit < text.Length; digit++)
        {
            rc.DrawText(
                new ScreenPoint(left + (cell * digit), baseline),
                text[digit].ToString(),
                ink,
                ActualFont,
                size,
                FontWeights.Bold,
                0,
                OxyPlot.HorizontalAlignment.Center,
                OxyPlot.VerticalAlignment.Middle);
        }
    }

    // Above and right of the handle, flipped to stay on the graph.
    private void RenderReadout(IRenderContext rc, int index)
    {
        ScreenPoint center = Center(index);
        if (!Shown(center))
        {
            return;
        }

        string text = EqBandHandles.Readout(index + 1, bands[index]);
        OxySize size = rc.MeasureText(text, ActualFont, ActualFontSize, ActualFontWeight);
        double width = size.Width + (2 * LabelPaddingX);
        double height = size.Height + (2 * LabelPaddingY);
        OxyRect area = PlotModel.PlotArea;
        double left = center.X + Radius + LabelGap;
        if (left + width > area.Right)
        {
            left = center.X - Radius - LabelGap - width;
        }

        double top = center.Y - Radius - LabelGap - height;
        if (top < area.Top)
        {
            top = center.Y + Radius + LabelGap;
        }

        var label = new OxyRect(
            Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height)),
            width,
            height);
        rc.DrawRectangle(label, LabelFill, LabelStroke, 1, EdgeRenderingMode.PreferGeometricAccuracy);
        rc.DrawText(
            new ScreenPoint(label.Left + LabelPaddingX, label.Top + LabelPaddingY),
            text,
            ActualTextColor,
            ActualFont,
            ActualFontSize,
            ActualFontWeight);
    }

    // The selected handle over the rest, and the one in hand over everything.
    private IEnumerable<int> DrawOrder() =>
        Enumerable.Range(0, bands.Count)
            .OrderBy(index => index == dragged ? 2 : index == selected ? 1 : 0);

    private bool Shown(ScreenPoint center) =>
        PlotModel != null && PlotModel.PlotArea.Contains(center.X, center.Y);

    private sealed class FaintHandles : Annotation
    {
        private readonly EqBandHandlesAnnotation owner;

        public FaintHandles(EqBandHandlesAnnotation owner)
        {
            this.owner = owner;
            Layer = AnnotationLayer.BelowSeries;
        }

        public override void Render(IRenderContext rc)
        {
            base.Render(rc);
            owner.RenderHandles(rc, raised: false);
        }
    }
}
