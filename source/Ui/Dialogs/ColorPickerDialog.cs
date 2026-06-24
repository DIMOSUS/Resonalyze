using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Resonalyze;

/// <summary>
/// Provides a compact color picker tailored to the dark Resonalyze interface.
/// </summary>
internal sealed class ColorPickerDialog : Form
{
    private static readonly Color[] PresetColors =
    [
        Color.FromArgb(255, 107, 107),
        Color.FromArgb(255, 159, 67),
        Color.FromArgb(254, 202, 87),
        Color.FromArgb(29, 209, 161),
        Color.FromArgb(72, 219, 251),
        Color.FromArgb(84, 160, 255),
        Color.FromArgb(95, 39, 205),
        Color.FromArgb(243, 104, 224),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(200, 214, 229),
        Color.FromArgb(131, 149, 167),
        Color.FromArgb(34, 47, 62)
    ];

    private readonly ColorSpectrum spectrum = new();
    private readonly HueSlider hueSlider = new();
    private readonly Panel preview = new();
    private readonly TextBox hexTextBox = new();
    private readonly DarkNumericUpDown redInput = CreateChannelInput();
    private readonly DarkNumericUpDown greenInput = CreateChannelInput();
    private readonly DarkNumericUpDown blueInput = CreateChannelInput();
    private bool updatingControls;

    public ColorPickerDialog(Color initialColor)
    {
        SelectedColor = initialColor;
        InitializeDialog();
        SetSelectedColor(initialColor);
    }

    public Color SelectedColor { get; private set; }

    private void InitializeDialog()
    {
        SuspendLayout();

        UiStyle.ApplyDarkDialog(this, new Size(452, 448), title: "Select overlay color");

        var title = UiStyle.CreateLabel(
            "Overlay color",
            new Point(20, 18),
            UiPalette.TextPrimary,
            new Font(Font, FontStyle.Bold));
        var subtitle = UiStyle.CreateLabel(
            "Choose a preset or create a custom color.",
            new Point(20, 42),
            UiPalette.TextMutedSoft,
            Font);

        var presets = new FlowLayoutPanel
        {
            BackColor = Color.Transparent,
            Location = new Point(20, 70),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = new Size(412, 34),
            WrapContents = false
        };
        foreach (Color color in PresetColors)
        {
            presets.Controls.Add(CreateSwatch(color));
        }

        spectrum.Location = new Point(20, 120);
        spectrum.Size = new Size(320, 190);
        spectrum.ColorChanged += (_, _) => SetSelectedColor(spectrum.SelectedColor, updateSpectrum: false);

        hueSlider.Location = new Point(352, 120);
        hueSlider.Size = new Size(28, 190);
        hueSlider.HueChanged += (_, _) =>
        {
            spectrum.Hue = hueSlider.Hue;
            SetSelectedColor(spectrum.SelectedColor, updateSpectrum: false);
        };

        preview.Location = new Point(392, 120);
        preview.Size = new Size(40, 190);
        preview.Paint += PreviewPaint;

        AddLabel("HEX", 20, 330);
        UiStyle.ApplyTextBox(hexTextBox, new Point(20, 350), new Size(100, 23));
        hexTextBox.CharacterCasing = CharacterCasing.Upper;
        hexTextBox.MaxLength = 7;
        hexTextBox.TextAlign = HorizontalAlignment.Center;
        hexTextBox.Validated += (_, _) => ApplyHexColor();
        hexTextBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                ApplyHexColor();
                args.SuppressKeyPress = true;
            }
        };

        AddChannelControl("R", redInput, 140);
        AddChannelControl("G", greenInput, 230);
        AddChannelControl("B", blueInput, 320);

        var cancelButton = CreateDialogButton("Cancel", DialogResult.Cancel, accent: false);
        cancelButton.Location = new Point(238, 399);

        var okButton = CreateDialogButton("Apply", DialogResult.OK, accent: true);
        okButton.Location = new Point(338, 399);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(
        [
            title,
            subtitle,
            presets,
            spectrum,
            hueSlider,
            preview,
            hexTextBox,
            redInput,
            greenInput,
            blueInput,
            cancelButton,
            okButton
        ]);

        OverlayDialogControls.ApplyRuntimeDpiScale(this);
        ResumeLayout(false);
        PerformLayout();
    }

    private Button CreateSwatch(Color color)
    {
        var button = new Button
        {
            AccessibleName = $"Select {ToHex(color)}",
            BackColor = color,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 6, 0),
            Size = new Size(28, 28),
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        UiStyle.ApplyBorderedSwatch(button, UiPalette.DialogBorderSoft);
        button.Click += (_, _) => SetSelectedColor(color);
        return button;
    }

    private void AddChannelControl(string labelText, DarkNumericUpDown input, int x)
    {
        AddLabel(labelText, x, 330);
        input.Location = new Point(x, 350);
        input.ValueChanged += (_, _) => ApplyRgbColor();
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = UiPalette.TextSecondary,
            Location = new Point(x, y),
            Text = text
        });
    }

    private static DarkNumericUpDown CreateChannelInput()
    {
        var input = new DarkNumericUpDown
        {
            Maximum = 255,
            TextAlign = HorizontalAlignment.Center
        };
        UiStyle.ApplyNumericUpDown(input, Point.Empty, new Size(72, 23));
        return input;
    }

    private static Button CreateDialogButton(string text, DialogResult result, bool accent)
    {
        return UiStyle.CreateDialogButton(text, result, accent);
    }

    private void SetSelectedColor(Color color, bool updateSpectrum = true)
    {
        SelectedColor = Color.FromArgb(color.R, color.G, color.B);
        updatingControls = true;
        try
        {
            redInput.Value = SelectedColor.R;
            greenInput.Value = SelectedColor.G;
            blueInput.Value = SelectedColor.B;
            hexTextBox.Text = ToHex(SelectedColor);

            if (updateSpectrum)
            {
                HsvColor hsv = HsvColor.FromColor(SelectedColor);
                hueSlider.Hue = hsv.Hue;
                spectrum.SetColor(hsv);
            }
        }
        finally
        {
            updatingControls = false;
        }

        preview.Invalidate();
    }

    private void ApplyRgbColor()
    {
        if (updatingControls)
        {
            return;
        }

        SetSelectedColor(Color.FromArgb(
            (int)redInput.Value,
            (int)greenInput.Value,
            (int)blueInput.Value));
    }

    private void ApplyHexColor()
    {
        if (TryParseHex(hexTextBox.Text, out Color color))
        {
            SetSelectedColor(color);
            return;
        }

        System.Media.SystemSounds.Beep.Play();
        hexTextBox.Text = ToHex(SelectedColor);
        hexTextBox.SelectAll();
    }

    private void PreviewPaint(object? sender, PaintEventArgs args)
    {
        using var brush = new SolidBrush(SelectedColor);
        args.Graphics.FillRectangle(brush, preview.ClientRectangle);
        using var pen = new Pen(UiPalette.DialogBorder);
        args.Graphics.DrawRectangle(
            pen,
            0,
            0,
            preview.ClientSize.Width - 1,
            preview.ClientSize.Height - 1);
    }

    private static bool TryParseHex(string value, out Color color)
    {
        string normalized = value.Trim().TrimStart('#');
        if (normalized.Length == 6 &&
            int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            color = Color.FromArgb(
                (rgb >> 16) & 0xff,
                (rgb >> 8) & 0xff,
                rgb & 0xff);
            return true;
        }

        color = default;
        return false;
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

internal sealed class ColorSpectrum : Control
{
    private double hue;
    private double saturation;
    private double value;
    private bool dragging;

    public ColorSpectrum()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public event EventHandler? ColorChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Hue
    {
        get => hue;
        set
        {
            hue = Math.Clamp(value, 0, 360);
            Invalidate();
        }
    }

    public Color SelectedColor => HsvColor.ToColor(hue, saturation, value);

    public void SetColor(HsvColor color)
    {
        hue = color.Hue;
        saturation = color.Saturation;
        value = color.Value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using (var saturationBrush = new LinearGradientBrush(
            ClientRectangle,
            Color.White,
            HsvColor.ToColor(hue, 1, 1),
            LinearGradientMode.Horizontal))
        {
            args.Graphics.FillRectangle(saturationBrush, ClientRectangle);
        }
        using (var valueBrush = new LinearGradientBrush(
            ClientRectangle,
            Color.Transparent,
            Color.Black,
            LinearGradientMode.Vertical))
        {
            args.Graphics.FillRectangle(valueBrush, ClientRectangle);
        }
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        float markerX = (float)(saturation * (ClientSize.Width - 1));
        float markerY = (float)((1.0 - value) * (ClientSize.Height - 1));
        using var outerPen = new Pen(Color.Black, 3);
        using var innerPen = new Pen(Color.White, 1);
        args.Graphics.DrawEllipse(outerPen, markerX - 6, markerY - 6, 12, 12);
        args.Graphics.DrawEllipse(innerPen, markerX - 6, markerY - 6, 12, 12);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button == MouseButtons.Left)
        {
            dragging = true;
            UpdateSelection(args.Location);
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (dragging)
        {
            UpdateSelection(args.Location);
        }
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        dragging = false;
    }

    private void UpdateSelection(Point point)
    {
        saturation = Math.Clamp(point.X / (double)Math.Max(1, ClientSize.Width - 1), 0, 1);
        value = 1.0 - Math.Clamp(point.Y / (double)Math.Max(1, ClientSize.Height - 1), 0, 1);
        Invalidate();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class HueSlider : Control
{
    private double hue;
    private bool dragging;

    public HueSlider()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public event EventHandler? HueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Hue
    {
        get => hue;
        set
        {
            hue = Math.Clamp(value, 0, 360);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        int height = Math.Max(1, ClientSize.Height - 1);
        for (int y = 0; y < ClientSize.Height; y++)
        {
            double rowHue = y / (double)height * 360.0;
            using var pen = new Pen(HsvColor.ToColor(rowHue, 1, 1));
            args.Graphics.DrawLine(pen, 0, y, ClientSize.Width, y);
        }

        float markerY = (float)(hue / 360.0 * height);
        using var markerPen = new Pen(Color.White, 2);
        args.Graphics.DrawRectangle(
            markerPen,
            1,
            Math.Clamp(markerY - 2, 0, ClientSize.Height - 4),
            ClientSize.Width - 3,
            4);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button == MouseButtons.Left)
        {
            dragging = true;
            UpdateHue(args.Y);
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (dragging)
        {
            UpdateHue(args.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        dragging = false;
    }

    private void UpdateHue(int y)
    {
        hue = Math.Clamp(y / (double)Math.Max(1, ClientSize.Height - 1) * 360.0, 0, 360);
        Invalidate();
        HueChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    public static HsvColor FromColor(Color color)
    {
        double red = color.R / 255.0;
        double green = color.G / 255.0;
        double blue = color.B / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == red)
            {
                hue = 60 * (((green - blue) / delta) % 6);
            }
            else if (max == green)
            {
                hue = 60 * ((blue - red) / delta + 2);
            }
            else
            {
                hue = 60 * ((red - green) / delta + 4);
            }
        }
        if (hue < 0)
        {
            hue += 360;
        }

        double saturation = max <= 0 ? 0 : delta / max;
        return new HsvColor(hue, saturation, max);
    }

    public static Color ToColor(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        double chroma = value * saturation;
        double section = hue / 60.0;
        double intermediate = chroma * (1 - Math.Abs(section % 2 - 1));
        (double red, double green, double blue) = section switch
        {
            < 1 => (chroma, intermediate, 0d),
            < 2 => (intermediate, chroma, 0d),
            < 3 => (0d, chroma, intermediate),
            < 4 => (0d, intermediate, chroma),
            < 5 => (intermediate, 0d, chroma),
            _ => (chroma, 0d, intermediate)
        };

        double match = value - chroma;
        return Color.FromArgb(
            (int)Math.Round((red + match) * 255),
            (int)Math.Round((green + match) * 255),
            (int)Math.Round((blue + match) * 255));
    }
}
