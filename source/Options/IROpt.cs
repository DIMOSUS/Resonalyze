using System;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class IROpt : Form
    {
        private readonly WrappingToolTip toolTip = new();

        // Zero until Init, which then offers the full ISO list.
        private int sampleRate;

        public IROpt()
        {
            InitializeComponent();
            ConfigureChoices();
            InitializeToolTips();
            Disposed += (_, _) => toolTip.Dispose();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, ImpulseResponseOptions opt)
        {
            // The settings file clamps wider than the control; an out-of-range value must not throw.
            sampleRate = expSweepMeasurement?.SampleRate ?? 0;
            numericLength.Value = numericLength.ClampValue(opt.Length);
            numericEnvelopeSmoothing.Value =
                numericEnvelopeSmoothing.ClampValue(opt.EnvelopeSmoothingMs);
            comboBandWidth.SelectedItem = NearestBandWidth(opt.BandFilterOctaves);
            SyncBandCentres(opt.BandCenterHz);
            comboAmplitudeScale.SelectedItem = opt.AmplitudeScale;
            comboTimeUnit.SelectedItem = opt.TimeUnit;
            comboTimeOrigin.SelectedItem = opt.TimeOrigin;
            checkInvert.Checked = opt.Invert;
            checkNormalizeStep.Checked = opt.NormalizeStepToImpulsePeak;
            checkBoxShowImpulse.Checked = opt.ShowImpulse;
            checkBoxShowEnvelope.Checked = opt.ShowEnvelope;
            checkBoxShowStep.Checked = opt.ShowStep;
        }

        public void SetOptions(ImpulseResponseOptions opt)
        {
            opt.Length = (int)numericLength.Value;
            opt.EnvelopeSmoothingMs = (double)numericEnvelopeSmoothing.Value;
            opt.BandFilterOctaves =
                comboBandWidth.SelectedItem is double width ? width : 0.0;
            opt.BandCenterHz =
                comboBandCenter.SelectedItem is double centre ? centre : opt.BandCenterHz;
            opt.AmplitudeScale = Selected(
                comboAmplitudeScale, ImpulseAmplitudeScale.Linear);
            opt.TimeUnit = Selected(comboTimeUnit, ImpulseTimeUnit.Milliseconds);
            opt.TimeOrigin = Selected(comboTimeOrigin, ImpulseTimeOrigin.RecordStart);
            opt.Invert = checkInvert.Checked;
            opt.NormalizeStepToImpulsePeak = checkNormalizeStep.Checked;
            opt.ShowImpulse = checkBoxShowImpulse.Checked;
            opt.ShowEnvelope = checkBoxShowEnvelope.Checked;
            opt.ShowStep = checkBoxShowStep.Checked;
        }

        private static T Selected<T>(ThemedComboBox comboBox, T fallback)
            where T : struct, Enum =>
            comboBox.SelectedItem is T value ? value : fallback;

        // Off is width zero, so "no band" and "which band" are one setting.
        private const double OctaveBand = 1.0;
        private const double ThirdOctaveBand = 1.0 / 3.0;

        private static readonly double[] OctaveCentres =
            [31.5, 63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];

        private static readonly double[] ThirdOctaveCentres =
        [
            25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800,
            1_000, 1_250, 1_600, 2_000, 2_500, 3_150, 4_000, 5_000, 6_300, 8_000,
            10_000, 12_500, 16_000, 20_000
        ];

        private void ConfigureChoices()
        {
            FillNumeric(
                comboBandWidth,
                width => width switch
                {
                    OctaveBand => "1 octave",
                    ThirdOctaveBand => "1/3 octave",
                    _ => "Off"
                },
                0.0,
                OctaveBand,
                ThirdOctaveBand);
            FillNumeric(comboBandCenter, FormatFrequency, OctaveCentres);
            comboBandWidth.SelectedIndexChanged += (_, _) =>
                SyncBandCentres(
                    comboBandCenter.SelectedItem is double current ? current : 1_000.0);

            Fill(
                comboAmplitudeScale,
                scale => scale switch
                {
                    ImpulseAmplitudeScale.PercentOfPeak => "% of peak",
                    ImpulseAmplitudeScale.Decibels => "dB re peak",
                    _ => "Linear"
                },
                ImpulseAmplitudeScale.Linear,
                ImpulseAmplitudeScale.PercentOfPeak,
                ImpulseAmplitudeScale.Decibels);
            Fill(
                comboTimeUnit,
                unit => unit == ImpulseTimeUnit.Samples ? "Samples" : "Milliseconds",
                ImpulseTimeUnit.Milliseconds,
                ImpulseTimeUnit.Samples);
            Fill(
                comboTimeOrigin,
                origin => origin switch
                {
                    ImpulseTimeOrigin.FirstArrival => "First arrival",
                    ImpulseTimeOrigin.Peak => "Peak",
                    _ => "Record start"
                },
                ImpulseTimeOrigin.RecordStart,
                ImpulseTimeOrigin.FirstArrival,
                ImpulseTimeOrigin.Peak);
        }

        // Keeps the nearest centre when switching widths.
        private void SyncBandCentres(double preferredCentreHz)
        {
            double octaves = comboBandWidth.SelectedItem is double width ? width : 0.0;
            bool active = octaves > 0.0;
            double[] centres = active && octaves < OctaveBand
                ? ThirdOctaveCentres
                : OctaveCentres;
            // Only bands whose whole octave-symmetric passband fits under Nyquist; same rule as ImpulseResponseOptions.HasBandFilter.
            if (active && sampleRate > 0)
            {
                double[] realizable = centres
                    .Where(centre => new ImpulseResponseOptions
                    {
                        BandFilterOctaves = octaves,
                        BandCenterHz = centre
                    }.HasBandFilter(sampleRate))
                    .ToArray();
                if (realizable.Length > 0)
                {
                    centres = realizable;
                }
            }

            SetItems(comboBandCenter, centres);
            comboBandCenter.SelectedItem = Nearest(centres, preferredCentreHz);
            comboBandCenter.Enabled = active;
            // Not Enabled: a disabled label paints near-black on this dark panel.
            UiStyle.SetTextEnabledLook(labelBandCenter, active);
        }

        private static double NearestBandWidth(double octaves) =>
            octaves <= 0.0
                ? 0.0
                : Nearest([OctaveBand, ThirdOctaveBand], octaves);

        // Nearest in octaves: linear Hz reads 250 as nearer to 500 than to 125.
        private static double Nearest(IReadOnlyList<double> values, double wanted) =>
            values.MinBy(value => Math.Abs(Math.Log2(value / wanted)));

        private static string FormatFrequency(double hertz) =>
            hertz >= 1_000.0
                ? $"{hertz / 1_000.0:0.###} kHz"
                : $"{hertz:0.#} Hz";

        // Format handler attached once; the list is refilled on width change and handlers would stack.
        private static void FillNumeric(
            ThemedComboBox comboBox,
            Func<double, string> label,
            params double[] values)
        {
            comboBox.FormattingEnabled = true;
            comboBox.Format += (_, e) =>
            {
                if (e.ListItem is double item)
                {
                    e.Value = label(item);
                }
            };
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            SetItems(comboBox, values);
        }

        private static void SetItems(ThemedComboBox comboBox, IReadOnlyList<double> values)
        {
            comboBox.Items.Clear();
            foreach (double value in values)
            {
                comboBox.Items.Add(value);
            }
        }

        private static void Fill<T>(
            ThemedComboBox comboBox,
            Func<T, string> label,
            params T[] values)
            where T : struct, Enum
        {
            comboBox.Items.Clear();
            comboBox.FormattingEnabled = true;
            foreach (T value in values)
            {
                comboBox.Items.Add(value);
            }

            comboBox.Format += (_, e) =>
            {
                if (e.ListItem is T item)
                {
                    e.Value = label(item);
                }
            };
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        private void InitializeToolTips()
        {
            numericLength.ApplyToolTip(
                toolTip,
                "Sets how many impulse-response samples are shown after the peak.");
            toolTip.SetToolTip(
                labelBandWidth,
                "Reads every trace through a zero-phase band of this width, which is " +
                "how you see WHEN a band arrives — a full-range impulse buries that in " +
                "one waveform. Zero phase moves nothing in time, at the price of a " +
                "symmetric ring around each arrival.");
            toolTip.SetToolTip(
                labelBandCenter,
                "Centre of the band. The peak marker follows the band; the arrival " +
                "marker stays on the record's own estimate, so the offset between them " +
                "is the band's delay.");
            toolTip.SetToolTip(
                labelAmplitudeScale,
                "Linear shows the raw sample values, which are comparable between " +
                "records; the other two normalise against the peak — percent for the " +
                "shape of the arrival, decibels for the low-level tail.");
            toolTip.SetToolTip(
                labelTimeUnit,
                "The unit of the time axis. The tracker reads both units either way.");
            toolTip.SetToolTip(
                labelTimeOrigin,
                "Where the axis puts zero: the record start (absolute time, comparable " +
                "with Time Alignment and the Virtual DSP gates), the estimated first " +
                "arrival, or the strongest peak. The measurement itself is never " +
                "moved — only the axis. With zero on an arrival the tracker also " +
                "reads the path length that time corresponds to in air.");
            numericEnvelopeSmoothing.ApplyToolTip(
                toolTip,
                "Averages the envelope over this duration, centred so nothing shifts " +
                "in time. Zero leaves it unsmoothed.");
            toolTip.SetToolTip(
                labelInvert,
                "Flips the displayed polarity of the impulse and step traces. The " +
                "record is not modified and the envelope is unaffected.");
            toolTip.SetToolTip(
                labelNormalizeStep,
                "Scales the step response against the impulse peak, so it keeps its " +
                "size relative to the impulse instead of always filling the axis.");
            toolTip.SetToolTip(
                checkBoxShowImpulse,
                "Shows the impulse-response curve.");
            toolTip.SetToolTip(
                checkBoxShowEnvelope,
                "Shows the energy-time curve: the analytic-signal envelope of the " +
                "impulse, which is where reflections read as separate arrivals.");
            toolTip.SetToolTip(
                checkBoxShowStep,
                "Shows the step response — the running integral of the impulse, which " +
                "is what the system would do if the input jumped to a level and " +
                "stayed there.");
        }
    }
}
