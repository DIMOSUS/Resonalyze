using System.Drawing;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayFileTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsCapturedMagnitudeScale()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 2,
                Title = "SPL overlay",
                ColorArgb = Color.Orange.ToArgb(),
                CapturedMagnitudeScale = MagnitudeScale.SoundPressureLevel,
                Points = [new OverlayPoint(20, 80), new OverlayPoint(20_000, 70)]
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 2, root);

            Assert.NotNull(loaded);
            Assert.Equal(MagnitudeScale.SoundPressureLevel, loaded!.CapturedMagnitudeScale);
            // A file written before the field existed defaults to Relative.
            Assert.Equal(MagnitudeScale.Relative, new OverlayFile().CapturedMagnitudeScale);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsCapturedCurveKind()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.PhaseResponse,
                Slot = 4,
                Title = "Phase overlay",
                ColorArgb = Color.Purple.ToArgb(),
                CapturedCurveKind = AnalysisCurveKind.ExcessPhase,
                Points = [new OverlayPoint(20, 10), new OverlayPoint(20_000, -30)]
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.PhaseResponse, 4, root);

            Assert.NotNull(loaded);
            Assert.Equal(AnalysisCurveKind.ExcessPhase, loaded!.CapturedCurveKind);
            // A file written before the field existed leaves the kind unknown.
            Assert.Null(new OverlayFile().CapturedCurveKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheRawSpectrum()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 5,
                Title = "Exact overlay",
                ColorArgb = Color.Green.ToArgb(),
                Points = [new OverlayPoint(20, -40), new OverlayPoint(20_000, -60)],
                RawSpectrum =
                [
                    new OverlayPoint(0, -40),
                    new OverlayPoint(5, -41),
                    new OverlayPoint(10, -42)
                ],
                RawCalibrationCorrectionDb =
                    Enumerable.Range(0, RawCurveRenderer.PointCount)
                        .Select(index => index * 0.01)
                        .ToArray()
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 5, root);

            Assert.NotNull(loaded);
            Assert.Equal(3, loaded!.RawSpectrum.Length);
            Assert.Equal(-42, loaded.RawSpectrum[2].Y, 6);
            Assert.Equal(
                RawCurveRenderer.PointCount,
                loaded.RawCalibrationCorrectionDb.Length);
            Assert.Equal(10.23, loaded.RawCalibrationCorrectionDb[^1], 6);
            // A file written before the field existed carries no raw spectrum.
            Assert.Empty(new OverlayFile().RawSpectrum);
            Assert.Empty(new OverlayFile().RawCalibrationCorrectionDb);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsPsychoacousticSmoothingAsAPlainWidthPlusFlag()
    {
        // Same additive-field pattern as CapturedMagnitudeScale: the file keeps
        // a plain valid width in the legacy field (older builds read 1/6) and
        // the psychoacoustic mode travels in its own flag.
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 3,
                Title = "Psycho overlay",
                ColorArgb = Color.Teal.ToArgb(),
                Points = [new OverlayPoint(20, 0), new OverlayPoint(20_000, -3)]
            };
            original.SetSmoothingCode(SpectrumSmoothing.PsychoacousticCode);
            Assert.Equal(
                SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
                original.SmoothingInverseOctaves);

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 3, root);

            Assert.NotNull(loaded);
            Assert.Equal(
                SpectrumSmoothing.PsychoacousticCode, loaded!.SmoothingCode);
            Assert.Equal(
                SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
                loaded.SmoothingInverseOctaves);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveAndLoad_RoundTripsPhaseUnwrappedFlag(bool unwrapped)
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile original = CreateMinimalOverlay(Mode.PhaseResponse, 5);
            original.PhaseUnwrapped = unwrapped;

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.PhaseResponse, 5, root);

            Assert.NotNull(loaded);
            Assert.Equal(unwrapped, loaded.PhaseUnwrapped);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_DefaultsPhaseUnwrappedToNullForOlderFiles()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile original = CreateMinimalOverlay(Mode.PhaseResponse, 6);
            original.Save(root);

            // An older file simply never wrote the property, so it must load as unknown.
            OverlayFile? loaded = OverlayFile.Load(Mode.PhaseResponse, 6, root);

            Assert.NotNull(loaded);
            Assert.Null(loaded.PhaseUnwrapped);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOperationWithLiveCurveOperands()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.PhaseResponse,
                Slot = 9,
                Kind = OverlayKind.Operation,
                Title = "Main minus Compare",
                SourceCurveKeyA = "PhaseResponse:Primary:Main",
                SourceCurveKeyB = "PhaseResponse:Primary:Compare",
                Operation = OverlayOperation.AMinusB,
                ColorArgb = Color.Aqua.ToArgb()
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.PhaseResponse, 9, root);

            Assert.NotNull(loaded);
            Assert.Equal(OverlayKind.Operation, loaded.Kind);
            Assert.Equal("PhaseResponse:Primary:Main", loaded.SourceCurveKeyA);
            Assert.Equal("PhaseResponse:Primary:Compare", loaded.SourceCurveKeyB);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsIdenticalLiveCurveOperands()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayFile
            {
                Mode = Mode.PhaseResponse,
                Slot = 10,
                Kind = OverlayKind.Operation,
                Title = "Invalid",
                SourceCurveKeyA = "PhaseResponse:Primary:Main",
                SourceCurveKeyB = "PhaseResponse:Primary:Main",
                Operation = OverlayOperation.AMinusB,
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
    public void SaveAndLoad_RoundTripsComplexSumOperationWithoutOperands()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            // Complex sum reads the Main and Compare transfer IRs directly, so a
            // valid file carries no operand slots or curve keys at all.
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 11,
                Kind = OverlayKind.Operation,
                Title = "Main + Compare (complex)",
                Operation = OverlayOperation.ComplexSum,
                CompareDelayMs = -0.35,
                CompareInvertPolarity = true,
                ColorArgb = Color.Orange.ToArgb()
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 11, root);

            Assert.NotNull(loaded);
            Assert.Equal(OverlayKind.Operation, loaded.Kind);
            Assert.Equal(OverlayOperation.ComplexSum, loaded.Operation);
            Assert.Null(loaded.SourceCurveKeyA);
            Assert.Null(loaded.SourceCurveKeyB);
            Assert.Equal(-0.35, loaded.CompareDelayMs);
            Assert.True(loaded.CompareInvertPolarity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsComplexSumLossOperation()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            // The loss variant behaves like the complex sum: no operands, FR only, and it
            // carries the same Compare delay / polarity that shape the underlying sum.
            var original = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 10,
                Kind = OverlayKind.Operation,
                Title = "Sum loss",
                Operation = OverlayOperation.ComplexSumLoss,
                CompareDelayMs = 0.5,
                CompareInvertPolarity = true,
                ColorArgb = Color.Cyan.ToArgb()
            };

            original.Save(root);
            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 10, root);

            Assert.NotNull(loaded);
            Assert.Equal(OverlayOperation.ComplexSumLoss, loaded.Operation);
            Assert.Null(loaded.SourceCurveKeyA);
            Assert.Equal(0.5, loaded.CompareDelayMs);
            Assert.True(loaded.CompareInvertPolarity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsComplexSumOutsideFrequencyResponse()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var file = new OverlayFile
            {
                Mode = Mode.PhaseResponse,
                Slot = 12,
                Kind = OverlayKind.Operation,
                Title = "Invalid",
                Operation = OverlayOperation.ComplexSum,
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

    [Fact]
    public void Load_ThrowsOnCorruptJson()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = OverlayFile.GetPath(Mode.FrequencyResponse, 3, root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not valid json");

            Assert.ThrowsAny<Exception>(() =>
                OverlayFile.Load(Mode.FrequencyResponse, 3, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void QuarantineCorruptFile_MovesSlotFileAsideAndPreservesContent()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = OverlayFile.GetPath(Mode.FrequencyResponse, 3, root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not valid json");

            string? quarantinePath = OverlayFile.QuarantineCorruptFile(
                Mode.FrequencyResponse,
                3,
                root);

            Assert.Equal(path + ".corrupt", quarantinePath);
            Assert.False(File.Exists(path));
            Assert.Equal("{ not valid json", File.ReadAllText(quarantinePath!));
            // The slot now loads as empty instead of failing again.
            Assert.Null(OverlayFile.Load(Mode.FrequencyResponse, 3, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsTheCachedInstanceWhileTheFileIsUnchanged()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            CreateMinimalOverlay(Mode.FrequencyResponse, 6).Save(root);

            OverlayFile? first = OverlayFile.Load(Mode.FrequencyResponse, 6, root);
            OverlayFile? second = OverlayFile.Load(Mode.FrequencyResponse, 6, root);

            Assert.NotNull(first);
            Assert.Same(first, second);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReloadsAfterSaveInvalidatesTheCache()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            CreateMinimalOverlay(Mode.FrequencyResponse, 6).Save(root);
            OverlayFile? original = OverlayFile.Load(Mode.FrequencyResponse, 6, root);

            OverlayFile updated = CreateMinimalOverlay(Mode.FrequencyResponse, 6);
            updated.Title = "Renamed after the first load";
            updated.Save(root);
            OverlayFile? reloaded = OverlayFile.Load(Mode.FrequencyResponse, 6, root);

            Assert.NotNull(original);
            Assert.NotNull(reloaded);
            Assert.NotSame(original, reloaded);
            Assert.Equal("Renamed after the first load", reloaded.Title);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNullAfterDeleteEvenWhenCached()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            CreateMinimalOverlay(Mode.FrequencyResponse, 6).Save(root);
            Assert.NotNull(OverlayFile.Load(Mode.FrequencyResponse, 6, root));

            OverlayFile.Delete(Mode.FrequencyResponse, 6, root);

            Assert.Null(OverlayFile.Load(Mode.FrequencyResponse, 6, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void QuarantineCorruptFile_ReturnsNullWhenSlotFileIsAbsent()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Assert.Null(OverlayFile.QuarantineCorruptFile(
                Mode.FrequencyResponse,
                3,
                root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
