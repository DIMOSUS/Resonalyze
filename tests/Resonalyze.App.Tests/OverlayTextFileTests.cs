namespace Resonalyze.App.Tests;

public sealed class OverlayTextFileTests
{
    [Fact]
    public void ExportThenImport_RoundTripsPoints()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        OverlayPoint[] original =
        [
            new OverlayPoint(20, -10.5),
            new OverlayPoint(1_000, -2.25),
            new OverlayPoint(20_000, -30.0)
        ];

        try
        {
            OverlayTextFile.Export(path, original);
            OverlayPoint[] loaded = OverlayTextFile.Import(path);

            Assert.Equal(original, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_IgnoresCommentsAndBlankLinesAndExtraColumns()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(
            path,
            "# comment\n; another\n\n100   -3.0   ignored\n200\t-4.5\n");

        try
        {
            OverlayPoint[] loaded = OverlayTextFile.Import(path);

            Assert.Equal(
                [new OverlayPoint(100, -3.0), new OverlayPoint(200, -4.5)],
                loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_AcceptsCommaSemicolonAndTabSeparatorsAndSkipsGarbage()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(
            path,
            "freq,gain\n100,-3.0\n200; -4.5\n300\t-6.0\nnot a pair\n400 abc\n");

        try
        {
            OverlayPoint[] loaded = OverlayTextFile.Import(path);

            Assert.Equal(
                [
                    new OverlayPoint(100, -3.0),
                    new OverlayPoint(200, -4.5),
                    new OverlayPoint(300, -6.0)
                ],
                loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_RejectsFileWithFewerThanTwoPoints()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "# only one\n1000 -3.0\n");

        try
        {
            Assert.Throws<InvalidDataException>(() => OverlayTextFile.Import(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
