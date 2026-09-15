using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// <see cref="Label.AutoEllipsis"/> uses LineLimit: a box shorter than one line draws nothing at all
/// (15 px boxes around a 16 px Segoe UI 9pt line went blank).
/// </summary>
public sealed class EllipsisLabelHeightTests
{
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
