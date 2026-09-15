# Audio layer: non-obvious driver behaviour

`audio/` (`Resonalyze.Audio`) is the only project that references NAudio and Media
Foundation. The application opens sessions through `IAudioSessionFactory`
(`AudioSessionFactory` → `AudioBackendRegistry` → `WasapiBackend`, `AsioBackend`,
`MmeBackend`) and sees only neutral contracts (`AudioSessionRequest`,
`AudioCaptureResult`, `AudioFormat`, ...). Only `Resonalyze.Audio.Tests` gets
`InternalsVisibleTo`; a compile error in the app means a backend detail leaked.

This document records driver and codec behaviour that the code depends on but does
not make obvious.

## Averaged runs keep the device open

An averaged sweep replays one open session (`IAudioDuplexSession`) across runs instead
of reopening devices per run:

- WASAPI cannot be restarted once stopped, and render devices reject a different source
  after initialisation. `PcmDuplexSession` therefore builds its playback stream once from
  the bound signal and replays it; the signal is fixed for the session's lifetime.
- Re-initialising an ASIO driver costs seconds on slow drivers. `AsioDuplexSession`
  keeps the driver running (it plays silence after the stream ends); each run calls
  `AsioFullDuplexSession.ResetCapture` for a fresh accumulator and rewinds the
  excitation. The required sample count uses `AcceptedSamples` (including blocks queued
  around the rewind); processed `ReadSamples` could finish one ASIO packet early.
- Between runs the capture is paused (`PcmCaptureSession.Pause`): the device keeps
  running so the input meter stays live, but samples are dropped, otherwise the time
  spent deconvolving and judging a run would grow the capture buffer without bound.

## Device stops and waiters

A capture caller awaits "N samples recorded" through `SampleWaiterRegistry`. A device
stop event (unplug, driver error) completes only the first-buffer and stopped signals,
so a waiter on a sample count that will never arrive would hang until a manual Abort.

- On stop, all pending waiters are faulted (`FaultAll`).
- The failure is remembered (`terminalException`), because some waiters are created
  after the stop — the sweep waiter exists only once playback ends. A waiter registered
  against a stopped device faults immediately. The memory is cleared only by a real
  `StartAsync`, never by the reset between averaged runs.
- `SweepRunAudioOrchestrator` also observes the stop while playback is still running,
  before any sample waiter exists, so a dead device fails the run instead of hanging.
- Live consumers must await `AsioFullDuplexSession.StoppedAsync` alongside their own
  cancellation; otherwise an unplugged device leaves them frozen with no error.
- A driver that fails validation or playback startup must be detached immediately, or
  its callbacks keep running until owner teardown disposes the session.
- Stopping an already stopped device throws `InvalidOperationException`, which
  `AudioCaptureStop` treats as stopped.

## Callback discipline

NAudio fills ASIO playback only after the input callback returns, so the callback does
bounded copies into preallocated slots of `CapturePump` and nothing else; conversion,
metering and publication run on a worker thread. The ASIO pool is allocated in
`AsioCapturePump.Prepare`, because the buffer size is only known once the driver opens;
PCM packet sizes are known before start. Exhausting the pool means processing fell
behind the device and arms a terminal overflow failure. ASIO reports no packet
discontinuities, and does not duplicate a mono provider onto stereo outputs, so
`FloatArrayWaveStream` encodes the output routing explicitly.

## ASIO driver opening

Opening an ASIO driver is a synchronous COM call, and some drivers refuse it while a
previous instance is still being released. `AsioDeviceCatalog.GetDriverInfo` therefore
reads channels, buffer/latency figures and every supported standard rate during one
open.

## MMDevice wrappers are not disposed

`WindowsAudioEndpointService` deliberately does not dispose `MMDevice` wrappers during
enumeration. Core Audio can return the same COM identity from a later `GetDevice` call;
forcing `ReleaseComObject` through `MMDevice.Dispose` can disconnect the cached RCW, and
`WasapiOut` then fails to query `IMMDevice` with `E_NOINTERFACE`. The CLR releases the
short-lived wrappers.

## WASAPI render tail

The last source read can be shorter than the WASAPI buffer. `WasapiPlaybackDevice`
copies the zero-initialised tail as well, so stale device-buffer contents are not
rendered after the final frame.

## Wave format extensible

`AudioFileCodec` opens WAV files with `WaveFileReader` rather than `AudioFileReader`.
Most recorders and DAWs write 24-bit and multichannel WAV as `WAVE_FORMAT_EXTENSIBLE`
(0xFFFE). `AudioFileReader` treats any tag other than literal PCM or IEEE float as
compressed and hands it to ACM, which has no driver for it and fails with
"NoDriver calling acmFormatSuggest" — an ordinary 24-bit recording, refused.

`StandardizeExtensible` reads the subformat GUID (`KSDATAFORMAT_SUBTYPE_PCM` /
`_IEEE_FLOAT`) and turns the header into a plain `WaveFormat`. NAudio exposes the
extension as raw bytes on `WaveFormatExtraData`, not as `WaveFormatExtensible` (a type
test misses it), laid out as validBitsPerSample (2 bytes), channel mask (4), then the
GUID. Genuinely compressed payloads in a .wav (ADPCM, µ-law, MP3-in-WAV) still go to the
ACM-based reader.

Decoding deinterleaves while reading, keeping a running channel cursor across reads:
decoders are not bound to return whole frames, and restarting channel assignment per
block would swap channels from the first partial frame on. `maximumStoredBytes` caps
peak memory (payload + payload / keptChannels during assembly) and is checked per block,
so a file whose header lies about its duration stops at the budget.

Writing is WAV only: re-encoding a render to a lossy format would put codec artefacts
into the audition, and Windows does not guarantee an MP3 encoder. `WriteWav` writes
24-bit with clipping; `WriteWavFloat32` writes unclipped float for data such as FIR taps
that exceed ±1.
