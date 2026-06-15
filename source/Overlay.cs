using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MathNet.Numerics;
using NAudio.Gui;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using CheckBox = System.Windows.Forms.CheckBox;
using ToolTip = System.Windows.Forms.ToolTip;
using Button = System.Windows.Forms.Button;

namespace Resonalyze
{
    public class OverlayCollection
    {
        List<Overlay> AllOverlays = new List<Overlay>();
        public OxyPlot.WindowsForms.PlotView plotView { get; private set; }
        public Form1 form { get; private set; }

        public OverlayCollection(Form1 form, Panel overlays, OxyPlot.WindowsForms.PlotView plotView, ToolTip toolTip)
        {
            this.plotView = plotView;
            this.form = form;
            Panel overlayPanel1 = overlays.Controls
                .OfType<Panel>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Overlay template panel is missing.");
            Button button1 = overlayPanel1.Controls
                .OfType<Button>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Overlay template button is missing.");
            NumericUpDown numericUpDown1 = overlayPanel1.Controls
                .OfType<NumericUpDown>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Overlay template offset control is missing.");
            CheckBox checkBox1 = overlayPanel1.Controls
                .OfType<CheckBox>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Overlay template checkbox is missing.");

            AllOverlays.Add(new Overlay(overlayPanel1, button1, numericUpDown1, checkBox1, 1, toolTip, this));

            form.SuspendLayout();
            overlays.SuspendLayout();

            Random r = new Random(3);

            for (int i = 2; i <= 12; i++)
            {
                Button button = new Button();
                CheckBox checkBox = new CheckBox();
                NumericUpDown numericUpDown = new NumericUpDown();
                Panel overlayPanel = new Panel();

                overlayPanel.SuspendLayout();

                //
                overlayPanel.BackColor = Color.FromArgb(r.Next(255), r.Next(255), r.Next(255));

                overlayPanel.Controls.Add(numericUpDown);
                overlayPanel.Controls.Add(checkBox);
                overlayPanel.Controls.Add(button);
                overlayPanel.Size = overlayPanel1.Size;
                overlayPanel.Location = new Point(overlayPanel1.Location.X, overlayPanel1.Location.Y + (overlayPanel.Size.Height + overlayPanel1.Margin.Top) * (i - 1));
                overlayPanel.Name = $"overlayPanel{i}";

                //
                numericUpDown.BorderStyle = System.Windows.Forms.BorderStyle.None;
                numericUpDown.Location = numericUpDown1.Location;
                numericUpDown.Maximum = numericUpDown1.Maximum;
                numericUpDown.Minimum = numericUpDown1.Minimum;
                numericUpDown.Name = $"numericUpDown{i}";
                numericUpDown.Size = numericUpDown1.Size;
                numericUpDown.TextAlign = numericUpDown1.TextAlign;
                numericUpDown.Value = numericUpDown1.Value;

                //
                checkBox.AutoSize = checkBox1.AutoSize;
                checkBox.Location = checkBox1.Location;
                checkBox.Name = $"checkBox{i}";
                checkBox.Size = checkBox1.Size;

                //
                button.FlatStyle = button1.FlatStyle;
                button.Location = button1.Location;
                button.Name = $"button{i}";
                button.Size = button1.Size;
                button.Text = $"{i}";

                overlayPanel.ResumeLayout(false);
                overlayPanel.PerformLayout();

                overlays.Controls.Add(overlayPanel);
                AllOverlays.Add(new Overlay(overlayPanel, button, numericUpDown, checkBox, i, toolTip, this));
            }
            overlays.ResumeLayout(false);
            form.ResumeLayout(false);
        }

        public void Prepare(Mode mode)
        {

        }

        public void Show(Mode mode)
        {
            foreach (Overlay overlay in AllOverlays)
            {
                if (overlay.Checked)
                {
                    if (overlay.SeriesMode == mode)
                    {
                        overlay.Show();
                    }
                    else
                    {
                        overlay.Hide();
                    }
                }
            }
        }
    }

    public class Overlay
    {
        public int Index
        {
            get; private set;
        }

        public string Title
        {
            get; private set;
        }
        public Mode SeriesMode
        {
            get; private set;
        }

        public bool Checked => checkBox.Checked;

        OverlayCollection collection;
        Panel panel;
        NumericUpDown numericUpDown;
        Button button;
        CheckBox checkBox;

        DataPoint[]? sourcePoints;
        DataPoint[]? drawPoints;

        public Overlay(Panel panel, Button button, NumericUpDown numericUpDown, CheckBox checkBox, int index, ToolTip toolTip, OverlayCollection collection)
        {
            toolTip.SetToolTip(panel, "Select color");
            toolTip.SetToolTip(numericUpDown, "Overlay offset");
            toolTip.SetToolTip(checkBox, "Show/Hide overlay");
            toolTip.SetToolTip(button, "Create a static copy of the curve and display it as an overlay for comparison");

            this.panel = panel;
            this.numericUpDown = numericUpDown;
            this.button = button;
            this.checkBox = checkBox;

            panel.Click += new EventHandler(panelClick);
            checkBox.CheckedChanged += new EventHandler(checkBoxUpdate);
            button.Click += new EventHandler(buttonClick);
            numericUpDown.ValueChanged += new EventHandler(numericValueChanged);
            Index = index;
            this.collection = collection;
            Title = "";
            SeriesMode = Mode.None;
        }

        private void UpdateDrawPoints()
        {
            DataPoint[] points = sourcePoints
                ?? throw new InvalidOperationException("Overlay source points are not initialized.");
            drawPoints = new DataPoint[points.Length];
            double offset = (double)numericUpDown.Value;
            for (int i = 0; i < points.Length; i++)
            {
                drawPoints[i] = new DataPoint(points[i].X, points[i].Y + offset);
            }
        }

        private void panelClick(object? sender, EventArgs e)
        {
            ColorDialog MyDialog = new ColorDialog();
            // Keeps the user from selecting a custom color.
            MyDialog.AllowFullOpen = false;

            // Sets the initial color select to the current text color.
            MyDialog.Color = panel.BackColor;

            if (MyDialog.ShowDialog() == DialogResult.OK)
                panel.BackColor = MyDialog.Color;
        }

        private void CheckerSet(bool state)
        {
            checkBox.CheckedChanged -= new EventHandler(checkBoxUpdate);

            checkBox.Checked = state;

            checkBox.CheckedChanged += new EventHandler(checkBoxUpdate);
        }

        private void checkBoxUpdate(object? sender, EventArgs e)
        {
            if (checkBox.Checked)
            {
                if (SeriesMode == collection.form.CurrentMode && Title != "" && sourcePoints != null && drawPoints != null)
                {
                    Show();
                }
                else
                {
                    CheckerSet(false);
                }
            }
            else
            {
                Hide();
            }
        }

        private void buttonClick(object? sender, EventArgs e)
        {
            if (collection.plotView.Model != null && collection.plotView.Model.Series != null && collection.plotView.Model.Series.Count > 0)
            {
                int selection = -1;
                if (collection.plotView.Model.Series.Count == 1)
                {
                    selection = 0;
                }
                else
                {
                    List<string> seriesTitles = new List<string>();
                    foreach (var series in collection.plotView.Model.Series)
                    {
                        seriesTitles.Add(series.Title ?? string.Empty);
                    }

                    SelectSeries selectSeries = new SelectSeries();
                    foreach (string title in seriesTitles)
                    {
                        selectSeries.AddOption(title);
                    }
                    selectSeries.SetSelection(0);

                    if (selectSeries.ShowDialog() == DialogResult.OK)
                    {
                        selection = selectSeries.GetSelect();
                    }
                }

                if (selection > -1)
                {
                    var series = collection.plotView.Model.Series[selection];
                    string seriesTitle = series.Title ?? string.Empty;
                    if (!seriesTitle.StartsWith("Overlay", StringComparison.Ordinal) &&
                        series is LineSeries)
                    {
                        LineSeries lineSeries = (LineSeries)series;
                        if (lineSeries.Points.Count > 1)
                        {
                            Clear();

                            sourcePoints = new DataPoint[lineSeries.Points.Count];
                            lineSeries.Points.CopyTo(sourcePoints);
                            UpdateDrawPoints();

                            SeriesMode = collection.form.CurrentMode;
                            Title = $"Overlay{Index} {seriesTitle}";
                            Show();
                        }
                    }
                }
            }
        }

        private void numericValueChanged(object? sender, EventArgs e)
        {
            if (sourcePoints != null)
            {
                UpdateDrawPoints();
            }
            if (checkBox.Checked)
            {
                if (SeriesMode == collection.form.CurrentMode && Title != "" && sourcePoints != null && drawPoints != null)
                {
                    Hide();
                    Show();
                }
                else
                {
                    CheckerSet(false);
                }
            }
        }

        private void SaveToFile()
        {

        }

        private void LoadFromFile()
        {

        }

        public void Clear()
        {
            Hide();

            sourcePoints = null;
            drawPoints = null;
            Title = "";
        }

        public void Hide()
        {
            if (Title != "")
            {
                List<OxyPlot.Series.Series> copySeries = new List<OxyPlot.Series.Series>();

                foreach (var series in collection.plotView.Model.Series)
                {
                    if (series.Title.IndexOf(Title) != 0)
                    {
                        copySeries.Add(series);
                    }
                }

                collection.plotView.Model.Series.Clear();

                foreach (var series in copySeries)
                {
                    collection.plotView.Model.Series.Add(series);
                }

                collection.plotView.Model.InvalidatePlot(true);
                collection.plotView.Refresh();

                CheckerSet(false);
            }
        }

        public void Show()
        {
            if (Title != "" && sourcePoints != null && drawPoints != null)
            {
                CheckerSet(true);

                var color = panel.BackColor;

                var lineSeries = new LineSeries();
                lineSeries.Points.AddRange(drawPoints);
                lineSeries.Color = OxyColor.FromRgb(color.R, color.G, color.B);
                lineSeries.Title = Title;
                collection.plotView.Model.Series.Add(lineSeries);

                collection.plotView.Model.InvalidatePlot(true);
                collection.plotView.Refresh();
            }
        }
    }
}
