# DSP processor catalog

`dsp/DspProcessorProfile.cs` holds the known processors (`DspProcessorCatalog`), one device entry
(`DspProcessorPreset`) and the profile a project stores (`DspProcessorProfile`).

## Presets and ids

Adding a processor is one line in `DspProcessorCatalog.PresetList`, ordered as the selector lists them (by
manufacturer, flagships first). The entry is also where to grow device facts (delay step and max, PEQ band
count, crossover families and slopes, gain and Q limits): add a property with a default and fill it only for the
devices that differ.

`DspProcessorPreset.Id` is derived from manufacturer and model (lower case, alphanumerics kept, other runs
collapsed to a dash) and is what files store. Consequences:

- Renaming an entry renames its id; files naming the old id fall back to a Custom profile with the numbers they
  were saved with (same simulation, model name lost). Correcting a rate or convention is safe; renaming is the
  one costly edit.
- Two names differing only in punctuation would collide; building `PresetsById` throws at first use, and a
  catalog test trips it.

`DspProcessorCatalog.Resolve` makes a named model always answer with its current preset, so a data correction
reaches every project naming that model. An unknown id (a file from a newer catalog) behaves as Custom and keeps
its stored numbers.

## Properties

- `SampleRateHz` is the rate the device runs its filters at, independent of the measurement rate (bilinear
  warping; see [dsp-chain-response.md](dsp-chain-response.md#processor-rate-vs-record-rate)).
- `QConvention` does **not** change the simulation: every band is an RBJ biquad; the convention only states how
  the device reads a Q number and applies where numbers leave for it (tuning sheets). It is per model, not per
  maker (JL Audio TwK reads Classic, their VXi does not). Only AMP Panacea's Symmetric convention (Cirrus Logic
  CS47048C) is confirmed by measurement; the rest is the owner's table from makers' published data. Catalog tests
  pin what the file says, not how devices really behave.
- `MaxDelayMs` is the manual's per-channel delay ceiling, or null if not looked up. Null is not unlimited: it reads
  as `AutoAlignmentEngine.DefaultMaxDelayMs`, so unfilled entries keep the old behaviour. A wrong value turns a
  dialable tune into a refusal (or the reverse), so an entry states a number only when the manual does.
- `PhaseControl` (see [dsp-helix-phase-control.md](dsp-helix-phase-control.md)) defaults to false and is claimed
  only where the maker's tool is known to show it. All HELIX units run DSP PC-Tool, which shows the control on
  subwoofer and mid/high channels; the flag only says the family offers it.
- `FirFilters` is off until a maker's tool is known to accept a kernel. Like the phase control it is a proposal the
  project's own setting outranks.
- `SelectableSampleRatesHz` is only what the selector offers; Custom profiles accept any positive rate.
