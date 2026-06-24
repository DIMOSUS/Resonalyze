using System.Drawing;

namespace Resonalyze.App.Tests;

public sealed class OverlayOperationFileTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsCalculatedOverlayConfiguration()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayOperationFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 11,
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
            OverlayOperationFile? loaded = OverlayOperationFile.Load(
                Mode.FrequencyResponse,
                11,
                root);

            Assert.NotNull(loaded);
            Assert.Equal(original.Mode, loaded.Mode);
            Assert.Equal(original.Slot, loaded.Slot);
            Assert.Equal(original.Title, loaded.Title);
            Assert.Equal(original.SourceSlotA, loaded.SourceSlotA);
            Assert.Equal(original.SourceSlotB, loaded.SourceSlotB);
            Assert.Equal(original.Operation, loaded.Operation);
            Assert.Equal(original.BlendFrequencyHz, loaded.BlendFrequencyHz);
            Assert.Equal(original.BlendWidthOctaves, loaded.BlendWidthOctaves);
            Assert.Equal(original.UseAmplitudeSpace, loaded.UseAmplitudeSpace);
            Assert.Equal(original.Offset, loaded.Offset);
            Assert.Equal(original.ColorArgb, loaded.ColorArgb);
            Assert.Equal(original.StrokeThickness, loaded.StrokeThickness);
            Assert.Equal(original.LineStyle, loaded.LineStyle);
            Assert.Equal(original.OpacityPercent, loaded.OpacityPercent);
            Assert.Equal(
                original.SmoothingInverseOctaves,
                loaded.SmoothingInverseOctaves);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsSameSourceSlot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayOperationFile
            {
                Mode = Mode.GroupDelay,
                Slot = 12,
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
    public void Load_IgnoresJsonPropertyOrder()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = OverlayOperationFile.GetPath(
                Mode.FrequencyResponse,
                12,
                root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {
                  "smoothingInverseOctaves": 6,
                  "opacityPercent": 75,
                  "lineStyle": "Dot",
                  "strokeThickness": 2.5,
                  "colorArgb": -1,
                  "offset": 1.25,
                  "useAmplitudeSpace": true,
                  "blendWidthOctaves": 0.5,
                  "blendFrequencyHz": 1200.0,
                  "operation": "Blend",
                  "sourceSlotB": 4,
                  "sourceSlotA": 1,
                  "title": "Shuffled calculated overlay",
                  "slot": 12,
                  "mode": "FrequencyResponse",
                  "savedAtUtc": "2026-06-15T10:20:30+00:00",
                  "version": 4,
                  "format": "resonalyze-overlay-operation"
                }
                """);

            OverlayOperationFile? loaded = OverlayOperationFile.Load(
                Mode.FrequencyResponse,
                12,
                root);

            Assert.NotNull(loaded);
            Assert.Equal("Shuffled calculated overlay", loaded.Title);
            Assert.Equal(OverlayOperation.Blend, loaded.Operation);
            Assert.True(loaded.UseAmplitudeSpace);
            Assert.Equal(1_200.0, loaded.BlendFrequencyHz);
            Assert.Equal(0.5, loaded.BlendWidthOctaves);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsOrdinarySlot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayOperationFile
            {
                Mode = Mode.FrequencyResponse,
                Slot = 10,
                Title = "Wrong slot",
                SourceSlotA = 1,
                SourceSlotB = 2,
                Operation = OverlayOperation.Average,
                ColorArgb = Color.White.ToArgb()
            };

            Assert.Throws<ArgumentOutOfRangeException>(
                () => file.Save(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsInvalidBlendConfiguration()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayOperationFile
            {
                Mode = Mode.FrequencyResponse,
                Slot = 11,
                Title = "Blend",
                SourceSlotA = 1,
                SourceSlotB = 2,
                Operation = OverlayOperation.Blend,
                BlendFrequencyHz = 0,
                BlendWidthOctaves = 0,
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
    public void Save_RejectsAmplitudeSpaceInUnsupportedMode()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayOperationFile
            {
                Mode = Mode.PhaseResponse,
                Slot = 11,
                Title = "Phase",
                SourceSlotA = 1,
                SourceSlotB = 2,
                Operation = OverlayOperation.Sum,
                UseAmplitudeSpace = true,
                ColorArgb = Color.White.ToArgb()
            };

            Assert.Throws<InvalidDataException>(() => file.Save(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
