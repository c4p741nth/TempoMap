# TempoMap and osu! stable audio timing

Investigation and calibrated clipboard implementation, 2026-10-25. The reported symptom is a tick that sounds aligned in
TempoMap but late in the stable editor until the copied red-line offsets are reduced.

## Current choice for the requested local osu! listening workflow

**The existing Copy button and Ctrl+C apply -33 ms for the tested osu! setup. No additional recording is needed to choose a usable setting now.** Offset 1000 becomes 967. This is a local playback compensation, not a universal source-audio correction.

| Tested copy shift | Recordings | Matched beat pairs | Mean absolute error (ms) | Worst absolute error (ms) |
|---|---:|---:|---:|---:|
| -33 | 3 | 126 | 3.084 | 3.417 |
| -36 | 2 | 84 | 3.668 | 7.583 |

The -33 ms captures retain a small late residual and are steadier than the tested -36 ms captures. The latter did not improve the median consistently and introduced early ticks in the supplied recordings. The recordings are different acquisition runs, so this is an empirical operating choice from the available data, not proof of a universal deterministic effect of the offset value.

The earlier shift-0 recommendation preserved the original source-audio timing; that is a different goal from the user's clarified request to copy and listen in this osu! installation. **For that clarified goal, the recommendation is -33 ms, not 0 or the provisional -36 ms.** A constant shift cannot remove the observed variable tick timing. The subsequent explicit request fixes this converter to -33 ms, including when old settings contain another value.

## What is established

TempoMap renders metronome samples at each red line's beat positions, in the decoded song's sample timeline.
`PlaybackEngine` mixes the song and this click stream **before** BASS_FX tempo processing. Output buffering,
tempo lookahead and the device therefore delay the combined signal. They do not create a relative tick/music
delay that should be subtracted from an exported offset. The device-latency subtraction in `PositionMs` affects
the playhead; it is not used by the clipboard or the click renderer.

`ClipboardText`, `TimingClipboard.Write` and `TimingPoint.ToOsuLine` export the current timing. Red times are rounded
to whole milliseconds; beat lengths retain their precision and green times retain fractions. There was no extra
export delay to remove.

There is a separate **analysis convention**: `CoreTiming.AlignToEdge` defaults on and adds the song's measured
`EdgeAlign` lag to proposed times, including the model-based paths. The fallback is 18 ms. This is unrelated to
device latency. The original detector's 22.5 + 6.2 = 28.7 ms convention is also an onset calibration, not an
osu! driver measurement. On an existing/manual line, toggling Align to edge changes nothing until analysis is run
again. Comparing the *same red-line values* in both programs is necessary to distinguish this from playback delay.
The model is trained on map times; its output plus the edge adjustment can also differ from the map convention.
No change to that algorithm or its defaults was justified by this report alone.

The actual DLLs from the installed stable client and the App output were loaded in separate 32/64-bit processes.
Both decoded synthetic WAV/MP3/OGG pulses and two local real MP3 tracks with float + prescan + decode flags.
The comparison uses the first ten seconds; the real tracks were decoded for thirty seconds. Tiny PCM differences
are not a time shift. These are comparisons with the DLL defaults, not a trace of stable's stream creation flags.

Installed stable BASS: **2.4.15.2 (x86)**; TempoMap BASS: **2.4.17.48 (x64)**.

| Input | Best shift (samples) | Best shift (ms) | First signal agrees |
|---|---:|---:|---|
| Synthetic WAV | 0 | 0.000 | yes |
| Synthetic MP3 | 0 | 0.000 | yes |
| Synthetic OGG | 0 | 0.000 | yes |
| Real MP3 A | 0 | 0.000 | yes |
| Real MP3 B | 0 | 0.000 | yes |

5 inputs compared; 0 with a nonzero best shift. Maximum absolute zero-lag difference in the mono average: 1.31e-06.

A silent default-output probe (`BASS_Init(-1, 44100, BASS_DEVICE_LATENCY)`, no channels played) reported **25 ms**
latency, **10 ms** minimum buffer, and **48000 Hz** output with both DLLs on this machine. Those are BASS's reported
values under the probe's initialization, not measured residual metronome delay inside the osu! process.
Un4seen defines [`BASS_INFO.latency`](https://www.un4seen.com/doc/bass/BASS_INFO.html) as an average playback-start
delay. It does not tell us to subtract 25 ms from every red line.

## What can differ in osu!

Using BASS in two programs does not imply identical scheduling: a tick triggered when an application observes
a beat still has to start playing; a tick already mixed into the song has no separate start. The residual can
include clock compensation, the sample's attack, application update timing and audio configuration.
This is a plausible explanation of the report, not a measured root cause for this user's stable session.

osu!'s [Timing tab documentation](https://osu.ppy.sh/wiki/en/Client/Beatmap_editor/Timing) defines red offsets as
milliseconds and says each red line restarts the metronome. Its [Universal offset documentation](https://osu.ppy.sh/wiki/en/Offset/Universal_offset)
describes a system-specific audio adjustment. The [official calibration guide](https://osu.ppy.sh/wiki/en/Guides/How_to_use_the_Offset_Wizard)
says to **increase universal offset when hitsounds are late**, the opposite sign to moving a map's red lines
earlier. The inspected local configuration has `Offset = 0` and `AudioCompatibility = 0`; the file cannot measure
the effective residual delay.

For background, [ppy/osu #23393](https://github.com/ppy/osu/issues/23393) records an editor tick/music mismatch and
peppy's explanation of per-system playback differences. That report is about **lazer on Linux**, so its measured
20-30 ms is not a measurement of Windows stable. Likewise,
[osu-framework #6650](https://github.com/ppy/osu-framework/issues/6650) describes separately started sample timing
variance; it is evidence of a mechanism, not proof that stable uses that implementation.
The public stable-reference source URL returned 404 during this investigation. At the initial investigation the
installed stable editor's metronome output had not been captured. A subsequent user recording is measured below;
the editor's internal compensation has not been established from source.
There is no supported basis here for a universal fixed -18/-20/-25 ms preset.

## Current Copy behavior (E46)

The separate Copy to osu! button/dialog was removed at the user's request. **Copy, Ctrl+C, Copy all and Copy normalized**
subtract 33 ms from every red and green row in the public clipboard text. Offset 1000 becomes 967. The clipboard also
contains a private TempoMap format with the original times: Ctrl+V within TempoMap duplicates those rows at their
original times. External text is pasted exactly as supplied. The waveform, playback and saved `.osu` keep source times.

Multi-BPM timing now shows normalized green rows directly in Inherited/All. Main BPM is the tempo held longest over
the **whole audio**, per the user's request (osu!'s own last-note calculation can differ). Each effective red time gets a
green row, including x1.00 at the main tempo, with authored SV effects retained on top. Save includes these visible
green rows; repeat save/reopen does not multiply normalization. Same-kind overlaps have an orange marker and paste
retains every row. Timing point preparation and automatic Ogg import are documented in [AUDIO-IMPORT.md](AUDIO-IMPORT.md).

The converter has no shift argument; a single constant controls the conversion. The settings/view-model shift
properties are read-only too, so old saved values such as 0, -36 or 15 cannot override -33. Repeated copies start
from the current timing and cannot accumulate the correction. In osu!, open F6 and paste the converted rows
after removing the timing points you intend to replace. No additional recording is required to use this setting.

## Measure the residual directly

The generated test uses original synthetic pulses, with no third-party beatmap or audio:

```powershell
.venv\Scripts\python.exe tools/osu_audio_calibration.py create artifacts/audio-calibration
```

1. Import `artifacts/audio-calibration/TempoMap-audio-calibration.osz` into stable and open its editor's F3 tab.
   Keep **120 BPM**, **offset 1000 ms**, **100% speed**, the metronome enabled and Ctrl released.
2. Record at least ten seconds of **stereo desktop/loopback audio** as WAV or FLAC, with music and effects audible.
   Do not use a microphone, mono downmix or spatial processing. Keep volumes low enough to avoid clipping.
   Music is left-only; the centered metronome is also heard on the right. The tool uses the right channel for
   ticks and left minus right for music, so a centered tick sample and unaltered stereo channels are required.
3. Measure the recording:

   ```powershell
   .venv\Scripts\python.exe tools/osu_audio_calibration.py measure path\to\recording.wav
   ```

`tick_late_ms` is the median tick attack minus reference attack; positive means late. `copy_shift_ms` has the
opposite sign. The 10th/90th percentiles show the central spread; min/max/range also expose rare timing steps. This measures residual alignment at the recording point,
including the tick's attack, not absolute sound-card/headphone latency. A shared downstream delay cancels.
The script identifies music pulses by matching their known waveform, rejecting tick residue left by imperfect
left/right cancellation. It rejects missing ticks, mono/mixed/swapped channels, clipping and a reference played at the wrong rate.
Synthetic tests inject known early/zero/late ticks at 44.1/48 kHz and add a common recording delay.

Compare a real song after calibration, and check editor playback separately from gameplay/input timing.
Repeat-start captures and the real-song listening check remain to be done. No automatic correction value has been inferred
from DLL version, BASS's reported latency, the detector constants, or somebody else's recording.

## User recording, 2026-10-25

The user provided a real stereo PCM recording: 24.370 s at 48000 Hz. The initial envelope-only result included tick residue outside the reference music. A matched filter independently found the actual reference pulses. The verified result is:

- Matched beat pairs used: 42
- Tick attack after the reference: **33.417 ms** (median)
- 10th/90th percentiles: 33.417 / 33.417 ms
- Nearest whole-ms copy shift: **-33 ms**

The waveform-matching regression was red before the fix (extra tick-only events counted as music) and green afterward. The original and corrected measurement methods choose the same whole-ms shift. The narrow spread describes this recording, not sub-millisecond accuracy guaranteed across sessions.

The user confirmed this capture used **offset 1000 ms**, without the earlier estimated -22 ms correction. A separate synthetic control package at **offset 967 ms** was prepared at `artifacts/audio-calibration/TempoMap-audio-calibration-minus33.osz`. Its audio was verified identical to the original. The predicted residual under a constant-delay model was about 0.417 ms; the actual control result follows.

## Corrected-offset recording, same settings

The user confirmed the second recording used offset 967 ms with all sound settings unchanged.

| Recording | Map offset (ms) | Matched pairs | Tick late (ms) | Equivalent delay at original offset (ms) |
|---|---:|---:|---:|---:|
| baseline | 1000 | 42 | 33.417 | 33.417 |
| control | 967 | 42 | 2.417 | 35.417 |

Requested timing change: 33 ms earlier; observed reduction in the residual: 31.000 ms. The two runs differ by 2.000 ms after accounting for their different map offsets.

For the second recording alone, the next integer offset would be 965 ms, or -35 ms total relative to the original timing. That is provisional: the first and second runs do not support exactly one invariant latency at sub-millisecond precision. The narrow within-file percentiles do not measure between-run variation.

**Keep offset 967 unchanged and record again after stopping and starting playback**, retaining the same device and audio options. Collect additional independent runs before choosing the final copy shift. No new default, osu! setting, or imported map was changed, and the source of the between-run difference has not been established.

## Repeat recordings at offset 967

| Run (same offset) | Matched pairs | Tick late (ms) |
|---|---:|---:|
| a | 42 | 2.417 |
| b | 42 | 3.417 |
| c | 42 | 3.417 |

Within the 967 ms group, the median residual is 3.417 ms and the between-run span is 1.000 ms. The nearest whole-ms additional correction is -3 ms: **offset 964 ms / total Copy to osu! shift -36 ms**.

The baseline plus all supplied runs, normalized to the original offset before taking the median, gives 35.917 ms and the same integer correction -36 ms. Across those conditions the equivalent delays span 3.000 ms.

Files b and c have identical SHA-256 hashes. The user explicitly confirmed they were newly recorded separately, so the table retains both reported acquisition trials and records the identical-data limitation.

The synthetic verification package is `artifacts/audio-calibration/TempoMap-audio-calibration-minus36.osz`, with offset 964 ms and byte-identical reference audio. Its offset and audio hash were checked. This is a measured trial setting for the current setup, not a universal preset. This was a provisional prediction; the actual 964 recordings are analyzed below.

## Actual offset 964 controls: median does not show every tick

| Recording | Pairs | Median (ms) | Minimum (ms) | Maximum (ms) | Early / late pairs |
|---|---:|---:|---:|---:|---|
| untitled_964a.wav | 42 | 3.417 | -6.604 | 3.417 | 15 / 27 |
| untitled_964b.wav | 42 | 2.417 | -7.583 | 2.417 | 3 / 39 |

These are distinct recordings, confirmed by SHA-256. Every early tick in each file has a same-shaped late tick in that file: the minimum best normalized waveform correlation is 1.000000 / 1.000000. The recorded relative timing switches between clusters about 10 ms apart. This is not explained by simply mistaking a different tick sound for its onset.

The second recording illustrates the percentile limitation: its early ticks fall below the 10th-percentile coverage, so p10/p90 alone appear constant. The measurement tool now also prints `min_ms`, `max_ms`, `range_ms`, and a median-only warning when the observed span exceeds 2 ms. A synthetic regression with three early ticks among 42 pairs failed before this reporting change and passes afterward; the full Python suite now passes 10 tests. No decoder, analysis or playback code changed.

**Do not keep subtracting the median from the map offset:** some ticks are already early. The earlier -36 ms trial is not validated as a complete correction. Playback scheduling in blocks is a possible mechanism, but the stable implementation, capture path and focus effects have not been isolated; no exact internal root cause is claimed.

Next control: pause osu! audio and open the original synthetic `.osu` from `artifacts/audio-calibration/` in TempoMap. Keep its red line at 1000 ms, playback speed 1.0 and metronome enabled; do not run Auto-time. Record stereo loopback with TempoMap foreground and the same recording setup. Measure that capture to compare the combined-PCM playback path with the osu! captures. The control was subsequently provided; see the comparison below.

## TempoMap player control at original offset 1000 / 120 BPM

The user supplied `tm_1000.wav` for the original synthetic timing in TempoMap. The same measurement method gives:

| Player / recording | Offset (ms) | Pairs | Median delay (ms) | Minimum (ms) | Maximum (ms) |
|---|---:|---:|---:|---:|---:|
| TempoMap | 1000 | 42 | -0.021 | -0.042 | -0.021 |
| osu stable baseline | 1000 | 42 | 33.417 | 33.417 | 33.417 |
| osu stable 964a | 964 | 42 | 3.417 | -6.604 | 3.417 |
| osu stable 964b | 964 | 42 | 2.417 | -7.583 | 2.417 |

TempoMap has a 0.021 ms within-recording span and nearest whole-ms correction 0. The recorded tick and reference are aligned to the measurement resolution; no 10 ms steps appear in this control. This agrees with the source graph: music and clicks are mixed on one sample timeline before output.

**Conclusion for this comparison:** the capture/measurement setup can resolve a locked music/tick pair, and TempoMap playback at the manually set reference timing is aligned. The delay/steps in the supplied osu! captures belong to that playback/sample path under the tested conditions. The exact stable clock, tick sample attack and scheduling implementation have not been isolated, so neither a specific DLL bug nor a universal stable compensation value is claimed. This control does not validate Auto-time on unrelated songs.

**Preserving source timing:** Save .osu and internal clipboard data keep the original times. The user's requested public clipboard text applies -33 ms for local osu! listening; it does not edit the source grid. For osu!'s per-device calibration, its [Offset Wizard](https://osu.ppy.sh/wiki/en/Guides/How_to_use_the_Offset_Wizard) remains separate. A constant shift cannot remove the observed variable timing steps. No osu! setting or user map was changed by this investigation.

The current Copy workflow fixes export compensation to -33 ms, as subsequently requested. The provisional -36 ms trial was rejected after its actual recordings. No further synthetic capture is needed for this comparison; real-song paste/listening and any deliberate osu! settings change remain separate acceptance work.
