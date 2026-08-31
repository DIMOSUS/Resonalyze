using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// A label that clips its text with an ellipsis must be at least as tall as one
/// line of its own font.
/// </summary>
/// <remarks>
/// <see cref="Label.AutoEllipsis"/> on a fixed-size label asks GDI+ for
/// <c>StringFormatFlags.LineLimit</c>: draw only the lines that fit ENTIRELY
/// inside the layout rectangle. A box one pixel shorter than the line height
/// therefore does not clip the descenders — it draws NOTHING, silently, and the
/// control still reports its Text, its ForeColor and Visible = true. Nothing
/// short of looking at the pixels tells you.
/// <para>
/// That is how the Virtual DSP block's PEQ read-out and the Audition dialog's two
/// file names went blank: 15-pixel boxes around a Segoe UI 9pt line that measures
/// 16. Height and font scale together with the display, so a box that clears the
/// line at 96 DPI clears it everywhere — which is exactly what this asserts.
/// </para>
/// </remarks>
public sealed class EllipsisLabelHeightTests
{
    // The controls that carry such a label and can be built without a session,
    // an audio device or the whole shell.
    public static TheoryData<string> Hosts() =>
        new()
        {
            nameof(VirtualCrossoverChannelControl),
            nameof(VirtualCrossoverAuditionDialog)
        };

    private static Control Build(string name) => name switch
    {
        nameof(VirtualCrossoverChannelControl) => new VirtualCrossoverChannelControl(),
        nameof(VirtualCrossoverAuditionDialog) =>
            new VirtualCrossoverAuditionDialog(new VirtualCrossoverAuditionContext(
                LeftSum: [System.Numerics.Complex.One],
                RightSum: [System.Numerics.Complex.One],
                SampleRate: 48_000,
                LeftChannelCount: 1,
                RightChannelCount: 1,
                BorrowedSide: null,
                CalibrationResolver: null,
                CalibrationEntries: [],
                InitialCalibrationId: null,
                OwnCalibration: new VirtualCrossoverAuditionOwnCalibration(null, null, null),
                SpatialAverage: null,
                SpatialAverageReason: null)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown host")
    };

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void AnEllipsisLabelHasRoomForItsOwnLine(string host)
    {
        StaTest.Run(() =>
        {
            using Control root = Build(host);
            var checkedAny = false;
            foreach (Control control in Descendants(root))
            {
                if (control is not Label { AutoEllipsis: true, AutoSize: false } label)
                {
                    continue;
                }

                checkedAny = true;
                Assert.True(
                    label.Height >= label.Font.Height,
                    $"{host}.{label.Name} is {label.Height} px tall around a " +
                    $"{label.Font.Height} px line of {label.Font.Name} " +
                    $"{label.Font.SizeInPoints}pt, so it draws nothing at all.");
            }

            Assert.True(checkedAny, $"{host} carries no fixed-size ellipsis label any more.");
        });
    }
}
