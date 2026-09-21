using OxyPlot.WindowsForms;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;

namespace Resonalyze;

/// <summary>The overlay slot controls beside the main plot: slot 1 from the designer, the rest cloned from it.</summary>
internal sealed class OverlayPanel
{
    private readonly List<OverlaySlotView> views = [];

    public OverlayPanel(
        Form form,
        Panel container,
        PlotView plotView,
        WrappingToolTip toolTip,
        OverlayPlotSources sources,
        Action plotChanged,
        string? storageRoot = null)
    {
        Form = form;

        toolTip.InitialDelay = 600;
        toolTip.ReshowDelay = 150;
        toolTip.AutoPopDelay = 6_000;
        toolTip.ShowAlways = true;

        RoundedPanel templatePanel = container.Controls
            .OfType<RoundedPanel>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Overlay template panel is missing.");
        Button templateCaptureButton = templatePanel.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "buttonSaveOverlay")
            ?? throw new InvalidOperationException("Overlay template capture button is missing.");
        ThemedNumericUpDown templateOffset = templatePanel.Controls
            .OfType<ThemedNumericUpDown>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Overlay template offset control is missing.");
        CheckBox templateCheckBox = templatePanel.Controls
            .OfType<CheckBox>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Overlay template checkbox is missing.");
        Label templateNameLabel = templatePanel.Controls
            .OfType<Label>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Overlay template name label is missing.");

        Session = new OverlaySession(
            sources,
            new NumericFieldRange(templateOffset.Minimum, templateOffset.Maximum, templateOffset.DecimalPlaces),
            templateOffset.Value,
            model =>
            {
                model.InvalidatePlot(true);
                plotView.Refresh();
                plotChanged();
            },
            plotChanged,
            storageRoot);
        Session.SlotChanged += slot => views[slot.Index - 1].Present();
        Session.StorageFailed += ShowStorageError;

        views.Add(new OverlaySlotView(
            this,
            Session.Slots[0],
            templatePanel,
            templateCaptureButton,
            templateOffset,
            templateCheckBox,
            templateNameLabel,
            toolTip));

        form.SuspendLayout();
        container.SuspendLayout();

        for (int index = 2; index <= OverlayFile.MaximumSlotCount; index++)
        {
            RoundedPanel panel = CreatePanel(templatePanel, index);
            CheckBox checkBox = CreateCheckBox(templateCheckBox, index);
            ThemedNumericUpDown offset = CreateOffset(templateOffset, index);
            Button captureButton = CreateCaptureButton(templateCaptureButton, index);
            Label nameLabel = CreateNameLabel(templateNameLabel, index);

            panel.Controls.Add(checkBox);
            panel.Controls.Add(offset);
            panel.Controls.Add(captureButton);
            panel.Controls.Add(nameLabel);

            views.Add(new OverlaySlotView(
                this,
                Session.Slots[index - 1],
                panel,
                captureButton,
                offset,
                checkBox,
                nameLabel,
                toolTip));

            panel.ResumeLayout(false);
            panel.PerformLayout();
            container.Controls.Add(panel);
        }

        container.ResumeLayout(false);
        form.ResumeLayout(false);
    }

    public OverlaySession Session { get; }

    /// <summary>Owns the overlay dialogs.</summary>
    public Form Form { get; }

    public IReadOnlyList<OverlaySlotView> Views => views;

    // A programmatic Close bypasses the focus-close guard, so opening one menu closes the others first.
    public void CloseCaptureMenus()
    {
        foreach (OverlaySlotView view in views)
        {
            view.CloseCaptureMenu();
        }
    }

    public void ShowStorageError(string message, Exception exception)
    {
        MessageBox.Show(
            Form,
            $"{message}{Environment.NewLine}{exception.Message}",
            "Overlay storage",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static RoundedPanel CreatePanel(RoundedPanel template, int index)
    {
        return new RoundedPanel
        {
            BackColor = OverlayModes.SlotDefaultColor(index),
            BorderColor = template.BorderColor,
            CornerRadius = template.CornerRadius,
            Location = new Point(
                template.Location.X,
                template.Location.Y +
                    (template.Size.Height + template.Margin.Top) * (index - 1)),
            Name = $"overlayPanel{index}",
            Size = template.Size
        };
    }

    private static CheckBox CreateCheckBox(CheckBox template, int index)
    {
        return new ReleaseClickCheckBox
        {
            BackColor = template.BackColor,
            FlatStyle = template.FlatStyle,
            AutoSize = template.AutoSize,
            Location = template.Location,
            Name = $"checkBox{index}",
            Size = template.Size
        };
    }

    private static ThemedNumericUpDown CreateOffset(ThemedNumericUpDown template, int index)
    {
        return new ThemedNumericUpDown
        {
            BackColor = template.BackColor,
            DecimalPlaces = template.DecimalPlaces,
            ForeColor = template.ForeColor,
            Increment = template.Increment,
            Location = template.Location,
            Maximum = template.Maximum,
            Minimum = template.Minimum,
            Name = $"numericUpDown{index}",
            Size = template.Size,
            TextAlign = template.TextAlign,
            ThousandsSeparator = template.ThousandsSeparator,
            Value = template.Value
        };
    }

    // Cloned from the designer template to inherit its font and DPI-scaled coordinates.
    private static Label CreateNameLabel(Label template, int index)
    {
        return new Label
        {
            AutoEllipsis = template.AutoEllipsis,
            AutoSize = template.AutoSize,
            BackColor = template.BackColor,
            Font = template.Font,
            ForeColor = template.ForeColor,
            Location = template.Location,
            Name = $"labelOverlay{index}",
            Size = template.Size,
            TextAlign = template.TextAlign,
            UseCompatibleTextRendering = template.UseCompatibleTextRendering
        };
    }

    private static Button CreateCaptureButton(Button template, int index)
    {
        return new ReleaseClickButton
        {
            FlatStyle = template.FlatStyle,
            BackColor = template.BackColor,
            ForeColor = template.ForeColor,
            Location = template.Location,
            Name = $"button{index}",
            Size = template.Size,
            Text = $"{index}",
            UseVisualStyleBackColor = template.UseVisualStyleBackColor,
            UseCompatibleTextRendering = template.UseCompatibleTextRendering
        };
    }
}
