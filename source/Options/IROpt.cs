using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    /// <summary>Binds the Impulse settings to an <see cref="ImpulseViewSettingsSession"/>.</summary>
    public partial class IROpt : ModeSettingsForm
    {
        private readonly ImpulseViewSettingsSession session = new();
        private IReadOnlyList<double>? shownCentres;

        public IROpt()
        {
            InitializeComponent();
            numericLength.ApplyFieldRange(ModeSettingsLimits.ImpulseLength);
            numericEnvelopeSmoothing.ApplyFieldRange(ModeSettingsLimits.EnvelopeSmoothingMs);
            FillChoices();
            WireFields();
            InitializeToolTips();
        }

        /// <param name="sampleRate">The open result's rate, or the configured one when nothing is open.</param>
        public void Init(int sampleRate, ImpulseResponseOptions opt)
        {
            session.Load(opt, sampleRate);
            Present();
        }

        public void SetOptions(ImpulseResponseOptions opt) => session.WriteTo(opt);

        private void FillChoices()
        {
            Fill(comboBandWidth, ImpulseBandCentres.Widths, ImpulseBandCentres.WidthLabel);
            Format<double>(comboBandCenter, ImpulseBandCentres.CentreLabel);
            Fill(
                comboAmplitudeScale,
                [ImpulseAmplitudeScale.Linear, ImpulseAmplitudeScale.PercentOfPeak, ImpulseAmplitudeScale.Decibels],
                scale => scale switch
                {
                    ImpulseAmplitudeScale.PercentOfPeak => "% of peak",
                    ImpulseAmplitudeScale.Decibels => "dB re peak",
                    _ => "Linear"
                });
            Fill(
                comboTimeUnit,
                [ImpulseTimeUnit.Milliseconds, ImpulseTimeUnit.Samples],
                unit => unit == ImpulseTimeUnit.Samples ? "Samples" : "Milliseconds");
            Fill(
                comboTimeOrigin,
                [ImpulseTimeOrigin.RecordStart, ImpulseTimeOrigin.FirstArrival, ImpulseTimeOrigin.Peak],
                origin => origin switch
                {
                    ImpulseTimeOrigin.FirstArrival => "First arrival",
                    ImpulseTimeOrigin.Peak => "Peak",
                    _ => "Record start"
                });
        }

        private void WireFields()
        {
            Bind(numericLength, value => session.Length = (int)value);
            Bind(numericEnvelopeSmoothing, value => session.EnvelopeSmoothingMs = value);
            BindItem<double>(comboBandWidth, session.SetBandOctaves);
            BindIndex(comboBandCenter, index => session.CentreIndex = index);
            BindItem<ImpulseAmplitudeScale>(comboAmplitudeScale, scale => session.AmplitudeScale = scale);
            BindItem<ImpulseTimeUnit>(comboTimeUnit, unit => session.TimeUnit = unit);
            BindItem<ImpulseTimeOrigin>(comboTimeOrigin, origin => session.TimeOrigin = origin);
            Bind(checkInvert, on => session.Invert = on);
            Bind(checkNormalizeStep, on => session.NormalizeStepToImpulsePeak = on);
            Bind(checkBoxShowImpulse, on => session.ShowImpulse = on);
            Bind(checkBoxShowEnvelope, on => session.ShowEnvelope = on);
            Bind(checkBoxShowStep, on => session.ShowStep = on);
        }

        private protected override void PresentControls()
        {
            Show(numericLength, session.Length);
            Show(numericEnvelopeSmoothing, session.EnvelopeSmoothingMs);
            ShowItem(comboBandWidth, session.BandOctaves);
            if (!ReferenceEquals(shownCentres, session.Centres))
            {
                comboBandCenter.Items.Clear();
                foreach (double centre in session.Centres)
                {
                    comboBandCenter.Items.Add(centre);
                }

                shownCentres = session.Centres;
            }

            ShowIndex(comboBandCenter, session.CentreIndex);
            comboBandCenter.Enabled = session.BandActive;
            // Not Enabled: a disabled label paints near-black on this dark panel.
            UiStyle.SetTextEnabledLook(labelBandCenter, session.BandActive);
            ShowItem(comboAmplitudeScale, session.AmplitudeScale);
            ShowItem(comboTimeUnit, session.TimeUnit);
            ShowItem(comboTimeOrigin, session.TimeOrigin);
            checkInvert.Checked = session.Invert;
            checkNormalizeStep.Checked = session.NormalizeStepToImpulsePeak;
            checkBoxShowImpulse.Checked = session.ShowImpulse;
            checkBoxShowEnvelope.Checked = session.ShowEnvelope;
            checkBoxShowStep.Checked = session.ShowStep;
        }

        private static void Fill<T>(ThemedComboBox comboBox, IReadOnlyList<T> values, Func<T, string> label)
        {
            comboBox.Items.Clear();
            foreach (T value in values)
            {
                comboBox.Items.Add(value!);
            }

            Format(comboBox, label);
        }

        private static void Format<T>(ThemedComboBox comboBox, Func<T, string> label)
        {
            comboBox.FormattingEnabled = true;
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
