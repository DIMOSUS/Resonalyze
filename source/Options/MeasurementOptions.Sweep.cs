using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Resonalyze.Dsp;

using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class MeasurementOptions
    {
        private void PresentSweepBand()
        {
            SweepBandView view = SweepBandPreview.Read(session);
            labelActualRangeCaption.Text = view.Text;
            labelActualRangeCaption.ForeColor = view.Color;
            deviceToolTip.SetToolTip(labelActualRangeCaption, view.ToolTip);
        }

        // For playback from another device while this records; at the selected (not applied) rate, like the band line.
        private void buttonSaveSweepFile_Click(object? sender, EventArgs e)
        {
            SweepFileExport export = SweepBandPreview.Export(session);
            using var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "wav",
                FileName = export.SuggestedFileName,
                Filter = "WAV audio (*.wav)|*.wav",
                OverwritePrompt = true,
                Title = "Save the sweep signal"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            UseWaitCursor = true;
            try
            {
                export.Write(dialog.FileName);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Save sweep",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }
    }
}
