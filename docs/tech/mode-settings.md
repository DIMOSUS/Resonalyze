# Mode settings panels

The docked settings panels of the analysis modes: Frequency Response (`FROptions`), Phase (`PROpt`), Group Delay
(`GDOpt`), Waterfall (`WaterfallOptions`), Burst Decay (`BDOpt`), Impulse (`IROpt`) and Autocorrelation (`ACOpt`).
Each holds its fields in a UI-free session, reads everything it shows from the session and its readers, and writes
the session back on Apply. Live Spectrum and Record Settings have their own maps
([live-spectrum.md](live-spectrum.md#settings-panel-code-map), [sweep-measurement.md](sweep-measurement.md#record-settings-code-map)).

## Code map

| Concern | Where |
| --- | --- |
| Frequency Response: window mode, the Tukey fades, smoothing, calibration list, curves, dB SPL | `FrequencyResponseSettingsSession` |
| The amber of dB SPL and its three tooltips, off the open measurement | `FrequencyResponseSplChoice` |
| Phase and Group Delay: the gate, window, smoothing, curves; for Phase the unwrap and τ | `GatedAnalysisSettingsSession`, `GateFields`, `PhaseDetrendState` |
| What the reset buttons restore | `GatedAnalysisDefaults` |
| The Auto τ read-out and the τ buttons' estimates, over a snapshot of the gate | `PhaseDetrendEstimate` |
| Waterfall and Burst Decay: window, fades, slices, step, offset, range, smoothing, periods, rate and capture time | `WaterfallSettingsSession` |
| Impulse and Autocorrelation fields | `ImpulseViewSettingsSession` |
| Band widths, the centres a rate realizes, where a stored centre lands, labels | `ImpulseBandCentres` |
| A window and its two fades in samples; the settings file's clamp | `TukeyFades` |
| Fixed/FDW and the cycles; the settings file's cycle fallback | `WindowModeChoice` |
| The smoothing lists and labels (the settings file and Live Spectrum read them too) | `SmoothingPresetOptions` |
| "Reliable from" and the gate tooltips (the Virtual DSP gate dialog reads them too) | `GateReadout` |
| The fields' ranges | `ModeSettingsLimits` |
| What a panel reads of the open measurement, the configured rate when nothing is open | `ModeSettingsMeasurement` |
| Whether a waterfall has the 8 slices it draws from, and the plot's text when not | `WaterfallSliceVerdict` |
| Binding: follow the document, present the session, announce the user's edits | `ModeSettingsForm` |
| The preview drawn from what the session reads (`SampleWindowPreview`, `GatePreview`) | `ImpulsePreviewOptionsForm` |
| The fields the Phase and Group Delay panels share; those Waterfall and Burst Decay share | `GatedAnalysisOptionsForm`, `WaterfallSettingsForm` |

- **Fields behave as their controls.** A session holds each value as its field shows it: loading clamps and rounds
  as the field would, and a step onto zero or a snap onto an equal offset keeps what the field keeps. The panel writes
  one edit into the session and then presents the whole session with its controls' events ignored; a field is written
  only when its value moved, so text being typed survives.
- **The fades are the user's.** `TukeyFades` keeps the fades the user chose and shows them contained in the window,
  the left first. A narrower window clamps them on screen, a wider one gives them back; Apply writes what is shown, and
  a stored pair loads contained in its own window.
- **The document is followed in one place.** `ModeSettingsForm` subscribes to the `AnalyzerDocument`, marshals to the
  UI thread and hands the panel a `ModeSettingsMeasurement`. The session then moves what depends on it: the Auto gate
  onto the transfer IR's start, the rate and capture time of Waterfall and Burst Decay, the band centres of Impulse,
  the amber of dB SPL. Nothing a measurement moves applies: the host applies on `UserChanged`, which only an edit, a
  pick or a reset raises (an arrow key in a closed list moves the list without it). The rate a panel shows with nothing
  open comes from one `Form1` member.
- **τ.** Auto τ is read through a snapshot of the gate and window (`GatedAnalysisSettingsSession.DetrendReading`),
  memoized per measurement and snapshot. While the document is busy nothing is read: the read-out keeps what it
  shows and the buttons refuse, both in `PhaseDetrendEstimate`.
- **Tests.** Rules are tested on sessions and readers (`ModeSettingsSessionTests`); `ModeSettingsWiringTests` drives
  the panels in a real `DockedModeSettingsHost` and counts its applies; `ModeSettingsPanelsBoundaryTests` keeps statics
  and nested types off the panels and their bases.
