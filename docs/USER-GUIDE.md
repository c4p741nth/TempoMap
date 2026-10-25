# TempoMap user guide

[Documentation index](INDEX.md) · [คู่มือเครื่องมือภาษาไทย](TOOLS-TH.md) · [Audio import and clipboard](AUDIO-IMPORT.md)

## Open, analyze and apply

Open a song or `.osu` file from File, or drop it into the window. Audio opens in its original format;
opening and playback do not require ffmpeg. Optional Ogg conversion is a separate command with sample-rate
and quality controls (default quality 6), and keeps the source file.

**Auto-time** analyzes the song and displays a proposal. For one BPM, the existing BPM/Offset/Apply group holds
the result. Edit either value and press **Apply**. Multiple-tempo proposals apply several red lines.
Applying a proposal asks before replacing red lines; green points are retained.

Typing a BPM locks it, including when you type over a previous result and run Auto-time again.
Only offset is fitted. Clear the BPM box to detect BPM automatically again.

Use **Normal** for steady or multi-BPM songs. **Drift** modes suit recordings whose tempo moves gradually:
they fit a continuous grid with red lines at the chosen beat interval. Review the waveform and listen before applying.
The detector can choose half/double tempo or miss short/quiet transitions; hand editing and section tools help.

## Navigate and listen

- **Space** plays/pauses. Music and metronome volume show percentages; playback speed goes down to **0.25**.
- Use the waveform and bottom seek bar to move through the song. The endpoint buttons jump to the start/end.
- **Home/End** also jump to the song endpoints. Follow mode keeps playback visible.
- Hold **Shift** and drag on the waveform, or use Shift+Left/Right, to select a time range; **Esc** clears it.
- Theme selection at the upper right offers **Dark**, **Light** and **System**, remembered between runs.

The waveform's yellow line is the current playhead. Red timing lines define BPM/grid; green inherited lines carry
SV and effects. The timing table has **All**, **Timing** and **Inherited** tabs. Click a row to select it,
double-click to jump there; Ctrl+click/Shift+click select multiple rows.

## Add and edit points

**Ctrl+P** adds a red point at the yellow playhead and focuses its BPM. **Ctrl+G** adds a green point from any tab.
The **+** button follows the tab: All/Timing adds red; Inherited adds green. The nearby delete and move buttons
remove selected points or move the selected point to the yellow line.

Edit Time, BPM/SV and Meter in the point fields. **Enter**, Tab or clicking outside commits the edit;
**Esc** cancels the field edit. **Ctrl+Z/Ctrl+Y** undo/redo. Shift and nudge controls support fine grid adjustments.

For Kiai, add a green point at the start of a section and tick **Kiai**. Add another at its end and clear Kiai.
Timing/effect points define the value in effect until another point changes it; verify the result in osu!.

## SV and normalization

**Manual SV is the default.** Entered/imported SV values stay fixed when red timing changes, so SV can be edited
before or after timing. In osu!mania, the visual scroll speed still depends on BPM as well as SV.

Enable **Auto normalize SV** to compensate for BPM changes relative to the main BPM over the whole audio.
Turning it on preserves the visible values as a baseline and adds missing compensation points.
Later red-line edits update compensation. Turning it off freezes the current visible rows and numbers.
Undo/redo restores both the mode and its corresponding green points. Copy and Save use the visible rows.

## Refine a part of the song

| Tool | Use it when |
| --- | --- |
| Auto-time range | A highlighted passage needs new timing while the rest of the song is correct. |
| Auto-time section | One red line's section needs its BPM and offset re-detected. |
| Fit offset | BPM is correct; the selected section's grid needs to move onto the beat. |
| Check sections | Find sections whose grid fits the audio poorly before editing them. |
| Multiply BPM | Correct half/double tempo, or repeatedly apply a custom factor such as `0.5`. |
| Snap to onsets | Selected lines are close to clear attacks and need small position corrections. |
| BPM from spacing | Short ramp steps already have boundaries; infer BPM from their spacing. |
| Fill ramp | Selected anchors define a rising/falling tempo; insert intermediate grid steps. |
| Bar line here | A selected red line should mark beat one of a measure; BPM/time stay unchanged. |

Each change is undoable. Fit/section tools need enough audible material; do not snap quiet passages blindly.
For worked examples and the difference between each tool, open **F2** or [the Thai guide](TOOLS-TH.md).
Algorithm measurements and failure cases belong in [Phase 3 results](PHASE3-STATUS.md).

## Copy and save

**Ctrl+C** copies selected timing rows. **Ctrl+Shift+C** copies all visible red/green rows.
Paste with **Ctrl+V**; rows at the same time are kept and marked as overlaps. Copy normalized is unnecessary:
normalization is an explicit editing mode and its output is already visible.

Copy defaults to **-33 ms** outside TempoMap. Settings → Keyboard shortcuts / copy profiles changes that value or selects a named setup.
Internal paste and Save retain source timing. Read [the clipboard details](AUDIO-IMPORT.md) and
[the calibration measurements](OSU-AUDIO-TIMING.md) before assuming another setup needs the same shift.

Save writes timing into an `.osu` file. Check it in osu! with audio and metronome; UI tests cannot verify that listening step.
The [manual checklist](EDITOR-MANUAL-TEST.md) tracks remaining desktop/audio acceptance.

**F1** opens key/color help; **F2** opens the Thai tool guide. Help → About shows the app version and author.


## Workspace and projects

**F3** opens Listening, Display/bookmarks and Proposal/review tools. Listening includes range loop/pre-roll,
alternate speed, tap tempo, explicit A/B beat counts, multipliers and shift/BPM steps. On narrow windows,
music/metronome sliders are here; the main playback row stays visible.

Display offers filtered waveforms, onset strength, spectrogram, SV/Kiai lanes and four rows of measures.
Middle-drag pans, the top ruler shows time, and the overview seeks across the whole song. The table splitter resizes its panel.
Review shows proposed lines before Apply, current/proposed metronome audition, approximate BPM alternatives,
and a next-issue queue after Check sections. Alternative scores are relative signal strength, not confidence probabilities.

File → Save project stores a `.tmap` session with pending proposals, typed BPM, markers and view/listening state.
The app asks before discarding edited timing and offers recovery after an interrupted session. Save .osu remains
the game export; Save As, Revert and recent files are in File. Export .osz includes the current difficulty,
original audio and local media without saving over the source map.

File → Convert audio to Ogg explicitly creates another file with sample-rate and Vorbis quality controls (default 6).
Audio diagnostics explains native/tool availability. This build's native Opus decoder is unavailable: use the explicit converter
with ffmpeg, then open the converted file. [คู่มือภาษาไทยและตัวอย่าง](TOOLS-TH.md) covers each workflow.
