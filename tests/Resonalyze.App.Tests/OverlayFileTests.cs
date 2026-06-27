using System.Drawing;

namespace Resonalyze.App.Tests;

public sealed class OverlayFileTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsOverlayData()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 4,
                Title = "Overlay4 Frequency Response",
                Offset = -3.5,
                ColorArgb = Color.CornflowerBlue.ToArgb(),
                StrokeThickness = 3.5,
                LineStyle = OverlayLineStyle.DashDot,
                OpacityPercent = 65,
                SmoothingInverseOctaves = 12,
                Points =
                [
                    new OverlayPoint(20, -10),
                    new OverlayPoint(1_000, -2.5),
                    new OverlayPoint(20_000, -30)
                ]
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(
                Mode.FrequencyResponse,
                4,
                root);

            Assert.NotNull(loaded);
            Assert.Equal(original.Mode, loaded.Mode);
            Assert.Equal(original.Slot, loaded.Slot);
            Assert.Equal(original.Title, loaded.Title);
            Assert.Equal(original.Offset, loaded.Offset);
            Assert.Equal(original.ColorArgb, loaded.ColorArgb);
            Assert.Equal(original.StrokeThickness, loaded.StrokeThickness);
            Assert.Equal(original.LineStyle, loaded.LineStyle);
            Assert.Equal(original.OpacityPercent, loaded.OpacityPercent);
            Assert.Equal(
                original.SmoothingInverseOctaves,
                loaded.SmoothingInverseOctaves);
            Assert.Equal(original.Points, loaded.Points);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_PreservesNaNPointValues()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = CreateMinimalOverlay(Mode.GroupDelay, 3);
            original.Points =
            [
                new OverlayPoint(20, 1),
                new OverlayPoint(100, double.NaN),
                new OverlayPoint(1_000, 2)
            ];

            original.Save(root);
            string json = File.ReadAllText(
                OverlayFile.GetPath(Mode.GroupDelay, 3, root));
            OverlayFile? loaded = OverlayFile.Load(Mode.GroupDelay, 3, root);

            Assert.Contains("\"NaN\"", json);
            Assert.NotNull(loaded);
            Assert.True(double.IsNaN(loaded.Points[1].Y));
            Assert.Equal(original.Points[0], loaded.Points[0]);
            Assert.Equal(original.Points[2], loaded.Points[2]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_IgnoresJsonPropertyOrder()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = OverlayFile.GetPath(Mode.FrequencyResponse, 2, root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {
                  "points": [
                    { "y": -12.5, "x": 20 },
                    { "y": -3.0, "x": 1000 }
                  ],
                  "smoothingInverseOctaves": 12,
                  "opacityPercent": 80,
                  "lineStyle": "Dash",
                  "strokeThickness": 3.0,
                  "colorArgb": -16711681,
                  "offset": -1.5,
                  "title": "Shuffled overlay",
                  "slot": 2,
                  "mode": "FrequencyResponse",
                  "savedAtUtc": "2026-06-15T10:20:30+00:00",
                  "version": 5,
                  "format": "resonalyze-overlay"
                }
                """);

            OverlayFile? loaded = OverlayFile.Load(
                Mode.FrequencyResponse,
                2,
                root);

            Assert.NotNull(loaded);
            Assert.Equal("Shuffled overlay", loaded.Title);
            Assert.Equal(OverlayLineStyle.Dash, loaded.LineStyle);
            Assert.Equal(12, loaded.SmoothingInverseOctaves);
            Assert.Equal([new OverlayPoint(20, -12.5), new OverlayPoint(1_000, -3.0)], loaded.Points);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SlotsWithSameNumber_AreSeparatedByMode()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            SaveMinimalOverlay(root, Mode.FrequencyResponse, 1);
            SaveMinimalOverlay(root, Mode.PhaseResponse, 1);

            string frequencyPath = OverlayFile.GetPath(
                Mode.FrequencyResponse,
                1,
                root);
            string phasePath = OverlayFile.GetPath(
                Mode.PhaseResponse,
                1,
                root);

            Assert.NotEqual(frequencyPath, phasePath);
            Assert.True(File.Exists(frequencyPath));
            Assert.True(File.Exists(phasePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_RemovesOnlyRequestedSlot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            SaveMinimalOverlay(root, Mode.GroupDelay, 1);
            SaveMinimalOverlay(root, Mode.GroupDelay, 2);

            OverlayFile.Delete(Mode.GroupDelay, 1, root);

            Assert.Null(OverlayFile.Load(Mode.GroupDelay, 1, root));
            Assert.NotNull(OverlayFile.Load(Mode.GroupDelay, 2, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsOutOfRangeSlot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile file = CreateMinimalOverlay(
                Mode.FrequencyResponse,
                13);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => file.Save(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOperationKind()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 11,
                Kind = OverlayKind.Operation,
                Title = "Left minus right",
                SourceSlotA = 2,
                SourceSlotB = 7,
                Operation = OverlayOperation.Blend,
                BlendFrequencyHz = 1_250,
                BlendWidthOctaves = 0.75,
                UseAmplitudeSpace = true,
                Offset = 1.5,
                ColorArgb = Color.Aqua.ToArgb(),
                StrokeThickness = 3,
                LineStyle = OverlayLineStyle.Dot,
                OpacityPercent = 70,
                SmoothingInverseOctaves = 6
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 11, root);

            Assert.NotNull(loaded);
            Assert.Equal(OverlayKind.Operation, loaded.Kind);
            Assert.Equal(original.SourceSlotA, loaded.SourceSlotA);
            Assert.Equal(original.SourceSlotB, loaded.SourceSlotB);
            Assert.Equal(original.Operation, loaded.Operation);
            Assert.Equal(original.BlendFrequencyHz, loaded.BlendFrequencyHz);
            Assert.Equal(original.BlendWidthOctaves, loaded.BlendWidthOctaves);
            Assert.True(loaded.UseAmplitudeSpace);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTargetKind()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 12,
                Kind = OverlayKind.Target,
                Title = "Harman target",
                TargetSourceSlot = 0,
                TargetPreset = TargetPreset.HarmanRoom,
                TargetTiltDbPerOctave = -0.8,
                TargetBassShelfGainDb = 4,
                TargetBassShelfFrequencyHz = 105,
                TargetBassShelfWidthOctaves = 1.5,
                TargetToleranceDb = 3,
                ColorArgb = Color.Orange.ToArgb()
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 12, root);

            Assert.NotNull(loaded);
            Assert.Equal(OverlayKind.Target, loaded.Kind);
            Assert.Equal(0, loaded.TargetSourceSlot);
            Assert.Equal(TargetPreset.HarmanRoom, loaded.TargetPreset);
            Assert.Equal(-0.8, loaded.TargetTiltDbPerOctave);
            Assert.Equal(4, loaded.TargetBassShelfGainDb);
            Assert.Equal(105, loaded.TargetBassShelfFrequencyHz);
            Assert.Equal(1.5, loaded.TargetBassShelfWidthOctaves);
            Assert.Equal(3, loaded.TargetToleranceDb);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsTargetInTimeMode()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayFile
            {
                Mode = Mode.ImpulseResponse,
                Slot = 11,
                Kind = OverlayKind.Target,
                Title = "Bad target",
                ColorArgb = Color.White.ToArgb()
            };

            Assert.Throws<InvalidDataException>(() => file.Save(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsSameOperationSourceSlot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayFile
            {
                Mode = Mode.GroupDelay,
                Slot = 12,
                Kind = OverlayKind.Operation,
                Title = "Invalid",
                SourceSlotA = 3,
                SourceSlotB = 3,
                Operation = OverlayOperation.Sum,
                ColorArgb = Color.White.ToArgb()
            };

            Assert.Throws<InvalidDataException>(() => file.Save(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsSmoothingForNonFrequencyMode()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile file = CreateMinimalOverlay(
                Mode.ImpulseResponse,
                1);
            file.SmoothingInverseOctaves = 12;

            Assert.Throws<InvalidDataException>(() => file.Save(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SaveMinimalOverlay(string root, Mode mode, int slot)
    {
        CreateMinimalOverlay(mode, slot).Save(root);
    }

    private static OverlayFile CreateMinimalOverlay(Mode mode, int slot)
    {
        return new OverlayFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Mode = mode,
            Slot = slot,
            Title = $"Overlay{slot} Test",
            ColorArgb = Color.OrangeRed.ToArgb(),
            Points =
            [
                new OverlayPoint(1, 2),
                new OverlayPoint(3, 4)
            ]
        };
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"Resonalyze.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
