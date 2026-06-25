namespace Resonalyze;

public partial class Form1
{
    private void SetImpulseResponseSourceFile(string? path)
    {
        plotModelFactory.SetImpulseResponseFileName(path);
    }

    private string GetImpulseResponseDialogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(measurementSettings.LastImpulseResponseDirectory) &&
            Directory.Exists(measurementSettings.LastImpulseResponseDirectory))
        {
            return measurementSettings.LastImpulseResponseDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void UpdateLastImpulseResponseDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        measurementSettings.LastImpulseResponseDirectory = directory;
        measurementSettings.Save();
    }

    private async void buttonSave_Click(object sender, EventArgs e)
    {
        if (expSweepMeasurement.HasImpulseResponse && !expSweepMeasurement.InProgress)
        {
            using var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "json",
                Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"Resonalyze-IR-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json",
                InitialDirectory = GetImpulseResponseDialogDirectory(),
                RestoreDirectory = true,
                Title = "Save impulse response"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            commandController.FreezeSaveLoadDraw();
            try
            {
                ImpulseResponseFile file =
                    ImpulseResponseFile.Capture(expSweepMeasurement);
                await file.SaveAsync(dialog.FileName);
                if (currentHistoryEntryId.HasValue)
                {
                    measurementHistoryService.MarkSaved(
                        currentHistoryEntryId.Value,
                        dialog.FileName,
                        file);
                }
                else
                {
                    currentHistoryEntryId = measurementHistoryService.AddOrUpdateLoadedFile(
                        dialog.FileName,
                        file);
                }
                SetImpulseResponseSourceFile(dialog.FileName);
                UpdateLastImpulseResponseDirectory(dialog.FileName);
                RefreshCurrentModePlot();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    $"Failed to save the impulse response.\r\n\r\n{exception.Message}",
                    "Save failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                commandController.SetSaveAvailable(true);
                commandController.SetLoadAvailable(true);
            }
        }
    }

    private async void buttonLoad_Click(object sender, EventArgs e)
    {
        if (!expSweepMeasurement.InProgress)
        {
            using var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = GetImpulseResponseDialogDirectory(),
                Multiselect = false,
                RestoreDirectory = true,
                Title = "Load impulse response"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            commandController.SetSaveAvailable(false);
            commandController.SetLoadAvailable(false);
            try
            {
                ImpulseResponseFile file =
                    await ImpulseResponseFile.LoadAsync(dialog.FileName);
                expSweepMeasurement.RestoreImpulseResponse(
                    file.Octaves,
                    file.SampleRate,
                    file.Bits,
                    file.SweepDurationSeconds,
                    file.PlayChannel,
                    file.GetSweepDeconvolutionImpulseResponse(),
                    file.SweepDeconvolutionPeakIndex,
                    file.MeasurementMode,
                    file.GetTransferImpulseResponse(),
                    file.TransferPeakIndex);
                expSweepMeasurement.RestoreLevelSnapshot(file.GetMeterSnapshot());
                ApplyLoadedImpulseResponseState(dialog.FileName);
                currentHistoryEntryId = measurementHistoryService.AddOrUpdateLoadedFile(
                    dialog.FileName,
                    file);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    $"Failed to load the impulse response.\r\n\r\n{exception.Message}",
                    "Load failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                commandController.SetSaveAvailable(
                    expSweepMeasurement.HasImpulseResponse);
                FinalizeMeasurementCommandState();
            }
        }
    }
}
