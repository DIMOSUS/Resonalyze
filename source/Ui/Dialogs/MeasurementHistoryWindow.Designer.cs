namespace Resonalyze.Ui.Dialogs;

partial class MeasurementHistoryWindow
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        historyDataGridView = new DataGridView();
        Column1 = new DataGridViewTextBoxColumn();
        Column2 = new DataGridViewTextBoxColumn();
        Column3 = new DataGridViewButtonColumn();
        Column4 = new DataGridViewButtonColumn();
        FRPlotView = new OxyPlot.WindowsForms.PlotView();
        ((System.ComponentModel.ISupportInitialize)historyDataGridView).BeginInit();
        SuspendLayout();
        // 
        // historyDataGridView
        // 
        historyDataGridView.AllowUserToAddRows = false;
        historyDataGridView.AllowUserToDeleteRows = false;
        historyDataGridView.AllowUserToResizeColumns = false;
        historyDataGridView.AllowUserToResizeRows = false;
        historyDataGridView.BackgroundColor = Color.FromArgb(40, 42, 48);
        historyDataGridView.BorderStyle = BorderStyle.None;
        historyDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        historyDataGridView.Columns.AddRange(new DataGridViewColumn[] { Column1, Column2, Column3, Column4 });
        historyDataGridView.Location = new Point(0, 0);
        historyDataGridView.Margin = new Padding(0);
        historyDataGridView.MultiSelect = false;
        historyDataGridView.Name = "historyDataGridView";
        historyDataGridView.RowHeadersVisible = false;
        historyDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        historyDataGridView.Size = new Size(350, 500);
        historyDataGridView.TabIndex = 0;
        // 
        // Column1
        // 
        Column1.HeaderText = "Kind";
        Column1.Name = "Column1";
        Column1.ReadOnly = true;
        Column1.Width = 45;
        // 
        // Column2
        // 
        Column2.HeaderText = "Entry";
        Column2.Name = "Column2";
        Column2.ReadOnly = true;
        Column2.Width = 204;
        // 
        // Column3
        // 
        Column3.HeaderText = "Save";
        Column3.Name = "Column3";
        Column3.Width = 50;
        // 
        // Column4
        // 
        Column4.HeaderText = "Delete";
        Column4.Name = "Column4";
        Column4.Width = 50;
        // 
        // FRPlotView
        // 
        FRPlotView.BackColor = Color.FromArgb(32, 36, 46);
        FRPlotView.Location = new Point(0, 500);
        FRPlotView.Margin = new Padding(0);
        FRPlotView.Name = "FRPlotView";
        FRPlotView.PanCursor = Cursors.Hand;
        FRPlotView.Size = new Size(350, 200);
        FRPlotView.TabIndex = 1;
        FRPlotView.Text = "plotView1";
        FRPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
        FRPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
        FRPlotView.ZoomVerticalCursor = Cursors.SizeNS;
        // 
        // MeasurementHistoryWindow
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(40, 42, 48);
        ClientSize = new Size(350, 700);
        Controls.Add(FRPlotView);
        Controls.Add(historyDataGridView);
        ForeColor = Color.FromArgb(235, 237, 240);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(350, 700);
        Name = "MeasurementHistoryWindow";
        ShowIcon = false;
        ShowInTaskbar = false;
        Text = "Measurement History";
        ((System.ComponentModel.ISupportInitialize)historyDataGridView).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private DataGridView historyDataGridView;
    private OxyPlot.WindowsForms.PlotView FRPlotView;
    private DataGridViewTextBoxColumn Column1;
    private DataGridViewTextBoxColumn Column2;
    private DataGridViewButtonColumn Column3;
    private DataGridViewButtonColumn Column4;
}
