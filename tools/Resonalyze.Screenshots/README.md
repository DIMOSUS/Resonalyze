# Resonalyze.Screenshots

Re-takes the documentation's figures from the current build, so a panel that changed
does not leave a screenshot describing a program that no longer exists.

```powershell
dotnet run --project tools/Resonalyze.Screenshots                # every shot
dotnet run --project tools/Resonalyze.Screenshots -- fr gd       # just those two
dotnet run --project tools/Resonalyze.Screenshots -- --list      # what there is
```

The shots land in `assets/images/` (and `assets/images/manual/`), overwriting the
committed figures in place — so the diff after a run is exactly what changed on
screen.

## Setup

The measurements the figures are taken from are one car, measured once: they are
large and personal, so they live outside the repository and their paths are a local
setting. Copy `screenshots.example.json` beside it as `screenshots.json` — that name
is git-ignored — and point it at your own files:

| key | what it must be |
| --- | --- |
| `measurement` | an impulse response (`.json`). Every analysis-mode shot is taken from it. |
| `session` | a finished Virtual DSP project. The Virtual DSP, EQ Wizard and manual shots come from it, so it wants every channel loaded and tuned. |
| `arrayMeasurement` | optional: a measurement recorded with a microphone array. Both array figures come from it — the curves, and the dialog, whose rows are read back out of the same file. |
| `arrayRig` | optional, and needed by the array DIALOG only: the `inputs`, `loopbackInput` (numbered as the dialog prints them — Input 1 is 1) and `backend` (`asio`, `wasapiShared`, `wasapiExclusive`, `wave`) the dialog is to be shown for, plus optional `calibrations` — one name per further microphone, in the measurement's order. It states how many inputs are free and where that count comes from, and a measurement records none of it, so the figure waits for an answer instead of composing one. The names are there because a set recorded by one microphone moved between sittings stores the same calibration on every row, which illustrates an individually calibrated array badly. A rig that contradicts the measurement's own channels stops the shot. |
| `output` | leave empty for the repository's own `assets/images`. |

`--config <path>` overrides the file, which is the way to render into a scratch
folder while working on a shot.

**`measurement-options` is not in a no-argument sweep.** Record Settings draws the
live audio configuration, so without the interface attached it shoots a panel of
defaults and a *loopback is REQUIRED* warning — a worse figure than the committed
one. Ask for it by name, with the rig plugged in:

```powershell
dotnet run --project tools/Resonalyze.Screenshots -- measurement-options
```

**The array figures need material a sweep can be missing.** Without `arrayMeasurement`
(or, for the dialog, `arrayRig`) those scenes print what they want and the sweep goes on
— but asking for one of their shots BY NAME fails instead, because a named shot that was
never written must not exit 0.

**`ai_assistant` is taken for a real reply, in `ai-proposal.json` beside this tool.**
The scene presses *Copy for AI* first, because that is what mints the package id, and
rewrites only that id into the reply — a reply carrying any other one draws the amber
warning about a package the session cannot vouch for, which is a different figure. It
refuses to shoot rather than photograph a refusal: a review that warns, or a row the
session will not offer, stops the scene with the reason. The reply is written for a
session with a C–D junction on both sides; edit the file to suit another one.

**The run needs the screen to itself.** Several shots are screen captures, so the
tool raises the shell above everything else and refuses the shot when another
process's window still covers it — without that check a grab quietly returns whatever
was on top, and a browser window ends up committed as a figure. The screen must also
be at least 1720×1035. Expect a full sweep to take about ten minutes and to own the
display for all of it.

**It never touches your own configuration.** `portable.flag` is copied beside the
executable, which puts the application into portable mode: its settings, history and
overlays go to the tool's output folder rather than `%LocalAppData%\Resonalyze`.
Without it a sweep would flush whatever it touched — a cleared EQ bank, an audio
configuration it fell back to when the interface was absent — over the developer's
working setup, and every measurement it loads would land in their history. Do not
remove that file.

## What it cannot take

- **`noise`** — Live Spectrum needs a live signal through real hardware.
- **`compare`** — a composed before/after crop of two different tunes.
- The manual figures that came from the forum article: the microphone photo, the MMM
  captures, the hybrid pair, and the open PEQ menu — a context menu is a window of
  its own that neither capture path reaches.
- **`manual/record-settings`** — the same panel as `measurement-options`, but composed
  beside the shell's transport column with the six numbered callouts drawn on. The
  panel half has to be shot by hand for the reason `measurement-options` is asked for
  by name: this tool runs in portable mode with its own empty settings, so it would
  photograph a panel of defaults rather than a configured rig. The transport half is
  cropped from the shell shot; the callouts follow the panel's own group geometry.

These are committed artifacts. Re-take them by hand when they go stale.

## How it works, and what will break it

The tool drives the real shell. Four details are not obvious, and each was learned by
getting it wrong first:

- **The shell runs under a real `Application.Run` loop**, with the work driven from
  `Shown`. Only that loop installs the WinForms synchronization context; without it
  an `await` inside the EQ Wizard's Auto Tune resumes on a thread-pool thread and
  builds its band controls there, which WinForms then refuses to parent.
- **Waiting is done by pumping messages**, never by blocking. The panels marshal
  their background work back to the UI thread, so a plain `Wait()` deadlocks.
- **A mode's settings panel is a separate owned window**, so `DrawToBitmap` on the
  shell renders everything except it. Those shots come off the screen instead.
- **That panel docks to whichever side has room**, so the shell is pinned to the
  right edge of the screen to make it dock inside. With space to the right it lands
  outside the captured rectangle, and the shot silently loses the panel.

**Never drive a control that can raise a `MessageBox` or a file dialog.** Those are
window class `#32770`, not Forms, so they never appear in `Application.OpenForms` and
nothing here can close them — the click that opened one blocks for ever. Two are
already avoided: the EQ Wizard's *Reset filters* asks before clearing, so the bank is
emptied directly; and *Export* only shows the Q chooser while the project names no
processor model, so that dialog is constructed rather than clicked. `CaptureModal`
closes a stray native dialog and fails with a clear message, but that is a net, not a
licence.

The tool reaches the shell's private fields by name, because it drives panels the
application never meant to expose. Every accessor throws with the name it could not
find, so a rename stops the run with `No field buttonExport on VirtualCrossoverPanel`
rather than quietly skipping a shot. The project is in the solution so that the parts
which *can* break at compile time do.

The annotation coordinates in `Shots.cs` are read off the rendered figure, so they
belong to a window size and a panel layout. When a panel moves, the boxes move with
it and those numbers have to be re-read.
