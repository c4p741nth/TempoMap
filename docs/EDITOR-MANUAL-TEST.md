# Phase 1 editor: manual acceptance checklist

These checks require a Windows desktop and audio output. They have **not** been performed in the
Codex sandbox. Automated Editor tests cover the data, rendering inputs, scheduling, edits, view model,
proposal mapping and file writes; they do not establish audible synchronization or GUI usability.

## Prepare and launch

- [ ] Use .NET SDK 10.0.402 and the prepared package cache. From the repository root:

  ```powershell
  $env:DOTNET_CLI_HOME = "$PWD/.dotnet-home"
  $env:NUGET_PACKAGES = 'D:\packages\nuget'
  dotnet build -c Release --no-restore
  dotnet test -c Release --no-build --no-restore
  dotnet run --project src/TimingAnalyzer.App -c Release --no-build --no-restore
  ```

- [ ] Confirm that the window opens, resizes, and closes without exceptions. Check the side panel,
  status area, buttons and proposal fields at the minimum window size and normal desktop DPI.
- [ ] Make a disposable copy of a real beatmap folder, including its audio. Save overwrites the loaded
  `.osu` and backs up its previous bytes to `<name>.osu.bak`; each save replaces that backup.
  Perform the following save checks on the disposable copy.

## Load and navigate

- [ ] Open audio directly (MP3, OGG or WAV). After automatic Ogg q6 preparation, the waveform and a default 120 BPM red line appear.
- [ ] Open a `.osu` in the copied folder. Its AudioFilename resolves relative to the map, the waveform
  loads, and the side panel contains its red lines. Try a map with several BPM sections.
- [ ] For fixture smoke checks, use copies of any of your own beatmaps with their audio beside them (the repository's
  fixtures in `tests/TimingAnalyzer.Editor.Tests/Fixtures` are synthetic and have no audio).
  Without that audio, timing should still load and the status should explain the missing file.
- [ ] Confirm the red section markers, purple measures, gray beats, dim quarter beats, orange selected
  red line and yellow playhead. A new red line resets measure counting.
- [ ] Ctrl+wheel zooms around the pointer. Wheel and the bottom slider scroll. Check the beginning,
  middle and end at close and distant zooms. Waveform transients should remain visible.
- [ ] Click away from a red line to seek. Space and Play/Pause toggle playback. Space and editing
  shortcuts must not interfere with typing in a text box.

## Audio and metronome

- [ ] Follow starts checked. Play at 1.0 and 0.75: the yellow head and waveform move smoothly, with
  the head one third from the left except near the song boundaries. Zoom while paused, then Play:
  the anchor uses the new visible width. Check the end stops scrolling and Play restarts at zero.
- [ ] While playing, Ctrl+wheel zoom in and out: the view keeps following, with the head one third from
  the left at each zoom. Right after opening the program, press Play and wheel-scroll **ahead**: the view
  waits, then follows again by itself once the head reaches one third of it. Scroll **back** while playing:
  the view stays; click the waveform (or ←/→) and it follows again at once. The slider behaves like the
  wheel. Pause leaves the view in place; Play resumes follow.
  Toggle Follow off/on to resume immediately. With Follow unchecked, Play must leave it off.
- [ ] With follow active, click to seek forward/backward (including a small seek). The head and view
  should jump immediately and keep following. A seek after manual navigation must leave follow
  suspended until Play or toggling Follow. Compare the head with audible transients at each speed;
  small position corrections should not make it jitter backwards. Close during playback cleanly.

- [ ] With known-good timing, play at **1.0** speed. Listen at the beginning, middle and end: clicks
  should match the song beats, with an accented click on every measure and each red-line start.
- [ ] Repeat at **0.75**, then **0.5**. Pitch should stay stable and clicks should remain synchronized.
- [ ] Pause/resume repeatedly; seek forward and backward while playing and while paused. Repeat after
  changing speed. There should be no stale click tail, doubled tick or growing drift.
- [ ] Compare the yellow playhead against audible clicks and waveform transients at each speed.
  This specifically checks BASS_FX source position and device-latency compensation on real hardware.
- [ ] Toggle Metronome off/on during playback. The song should continue from the audible position.
- [ ] Check Music starts at 100 % and Metronome at 70 %. While playing, move each volume slider:
  only that source changes, 0 % silences it, and the song continues without a gap or position jump.
  Repeat at each speed; seek, edit timing and reopen the same song. Both slider values and audible
  levels should persist. Check slider labels and layout at minimum window width.
- [ ] Play through the end, then press Play again. Position should reach the end and playback restart.
- [ ] If no output device is available, verify a readable status message and that loaded timing and
  waveform remain usable. Reopen the file after reconnecting the device.

## Edit timing

- [ ] Click near a red line to select it. Drag it: offset changes, BPM stays fixed, and the preview follows
  the pointer. Release commits the move. A single Ctrl+Z undoes the entire drag; Ctrl+Y reapplies it.
- [ ] Ctrl+drag a beat or measure line after a red line: the chosen beat follows the pointer, BPM changes,
  and the red-line start stays fixed. Check undo/redo and synchronization after release.
- [ ] Press Escape during a drag, or lose pointer capture. The preview should cancel without an edit.
  Try dragging onto another red line or stretching a beat before its section start: timing must stay valid.
- [ ] Seek, press Insert, and verify the new line inherits the current section's BPM and meter. Delete it,
  then undo/redo each operation. A duplicate at the same time should report an error.
- [ ] Select a row and edit time, BPM and meter, then Apply fields. Try decimal values under th-TH;
  decimal points should remain dots. Invalid BPM or meter must leave timing unchanged.
- [ ] In Shift, choose All and try all six beat fractions and all four ms nudges on a multi-BPM map.
  Each section uses its own beat length for beat shifts; fixed ms nudges use the same delta throughout.
  Each press undoes/redoes in one step. Check playback follows the new timing after each shift.
- [ ] Choose Selected and shift a middle red line: only that line moves and selection stays on it.
  Try crossing/touching either neighbour: a status message should explain the rejection, with no edit
  or extra undo step. Shift the first line before zero and verify the negative time remains editable.
  Inspect the tooltip and saved/clipboard timing: green SV/sample rows stay at their original times.

## Phase 1.2: snap, clock, osu! precision, multi-select

- [ ] The time below the waveform reads like osu!: seek onto a red line at offset 1531 and it shows
  `00:01:531 / mm:ss:ms` (song length after the slash), with no decimal point. It counts while playing.
- [ ] For Beat snap 1/1, 1/2, 1/3, 1/4, 1/6, 1/8, 1/12 and 1/16: the dim ticks split each beat into that
  many parts (whole beats only when zoomed far out). Paused, press → and ← repeatedly: the playhead lands
  on each tick in turn, onto red lines when crossing them, and the view scrolls when it leaves the screen.
  Repeat while playing. Focus the volume slider or the list first: arrows should still step the playhead,
  but not inside a text box.
- [ ] Run Auto-time on MAMBO COMBO: the proposal reads `159.997` BPM and a whole-ms offset; after Apply the
  BPM field shows `159.997`, not `159.9968637393969`. Save and open in osu!: same BPM and offset.
- [ ] Type BPM `159.99964` and time `100.4`, Apply fields: they become `160` and `100`. Drag and stretch a
  line and shift by ¼ beat: the list shows whole ms and at most three BPM decimals.
- [ ] Ctrl+P adds a red line at the playhead (like Insert).
- [ ] Drag across several red lines on empty waveform space: an orange band appears, and on release they
  turn orange and get ● in the list. Ctrl+A selects all; Esc clears. (Since E33 Shift+drag highlights a range.) A short click
  still seeks, and grabbing a red line still drags it.
- [ ] With three lines selected, press +¼ beat: all three move by their own
  beat, as one Ctrl+Z step. Push them into an unselected line: a status message, no edit. Delete removes
  all selected lines as one undo step.

## Phase 2: multi-BPM auto-time (M3)

- [ ] Open MAMBO COMBO audio, leave the BPM box empty, press **Auto-time** (Normal). The status gives the
  number of red lines and their BPM range, and Apply... appears. Nothing in the timeline changes yet.
- [ ] Press **Apply...**, confirm: every red line is replaced and the grid follows the new
  sections. **One Ctrl+Z restores the whole previous timing** (not one line at a time).
- [ ] Repeat with a BPM typed in the box (e.g. `160`): the button refuses with a message naming the BPM box,
  and no proposal is made.
- [ ] Open a single-BPM song (e.g. any Set B map): the proposal is one red line with an editable offset (the original detector's line).
- [ ] Open a beatmap with green lines, apply a multi-BPM proposal, then Copy timing points: the green lines
  are still there.

## Phase 3 (2026-10-18): duplicate red lines, drift mode

- [ ] Open `ReeK - Multigenre Smackdown (Arrival) [Electro Genre Turnstile Sequence]` (set 1627304, ranked taiko):
  it opens (no "Open failed"), retains overlapping rows with orange markers, and says the first controls playback.
- [ ] Open Billie Jean (`E:\Music\Michael Jackson - Michael Songs From The Motion Picture - Billie Jean.ogg`), choose
  **Drift 16 beats**, press **Auto-time**: about 36 red lines between 116.5 and 117.8 BPM are listed; Apply...
  replaces the red lines; with the metronome on, the click stays on the kick and snare from the intro
  to the end (one red line drifts audibly off in the intro). One Ctrl+Z restores the previous timing.
- [ ] Try 4 and 32 beats: more / fewer lines, the click stays on the drums in both.
- [ ] Choose **Drift 1 beat**, press **Auto-time**: about 553 red lines between about 112 and 124 BPM; the
  description gives the BPM range (no long list) and the list below scrolls. Apply, then at 0.5x with the metronome
  the click lands on every kick and snare, intro to end, with no audible flam. Save, open in osu!, and play-test a
  few kick/snare notes: they judge as on time.
- [ ] After that Apply: the metronome accents every fourth beat only, and in osu! (mania) a bar line shows every
  4 beats, not on every red line. If the bars are off, select a red line on a downbeat, **Bar line here**: the bar
  lines move, Ctrl+Z restores them.
- [ ] On a map with hitsound volumes (green lines), apply a proposal: the red lines' volume and sample set match the
  green line before them, not 100 % normal.
- [ ] Copy the visible normalized rows, paste into the osu! editor timing setup (replacing its timing points), play in mania:
  check scroll speed through BPM changes. Reopen the saved map: the green SVs stay the same. Main BPM now uses the
  whole audio, per E46; osu!'s own last-note reference may differ.
- [ ] Billie Jean in Normal mode: Auto-time proposes one red line (117.213, offset 103, editable) and the status
  suggests Drift 1 beat (drums wobble about 4.9 ms); a steady song (any Set B map) gets no such hint.
- [ ] Timing table: All / Timing / Inherited tabs list red and green lines like osu!'s timing window (offset, BPM
  or x SV, meter, sample, volume, kiai). Ctrl+click and Shift+click select several rows; Ctrl+C (or Copy selected)
  then paste into the osu! editor's timing setup adds exactly those rows; Ctrl+A then Ctrl+C copies the whole tab.
- [ ] Shrink the window to its minimum (900×500): the toolbars and the selected-line panel get scroll bars, the
  waveform stays usable; after an auto-time the Apply buttons are in view without scrolling; Apply...
  asks a one-line yes/no.

## E42/E43: held arrows, ❮ BPM ❯, Esc, Auto-time on the edge and on a bar line

- [ ] Hold → for a second or two: the yellow line steps once, and after 0.3 s runs on smoothly, faster the longer you
  hold; release stops it where it is (it does not creep on). ← does the same. While playing, the sound pauses during
  the run and plays on from the head on release. Shift+→ held grows the blue range with it.
- [ ] Shift row, **❮ 0.01 ❯ BPM**: with a red line selected, press ❯ (or Ctrl+↑): its BPM goes 143 → 143.01 and its
  time stays. Hold ❯ and it keeps going; one Ctrl+Z undoes the whole run. The drop-down picks 0.001 / 0.01 / 0.1 / 1
  and is remembered; Ctrl+Shift+↑/↓ takes ten steps. Tune a BPM against the waveform at the far end of a long section.
- [ ] Press Esc in the Keys window (F1), About, and the Replace red lines? window: each closes (Esc in the last one is
  Cancel). Esc in the main window still clears the selection.
- [ ] Auto-time on a sharp-drummed song (recreate.ogg, or any electronic or metal song): the proposed offset sits on the
  start of the drum hit in the waveform, not 15-20 ms before it; Settings → Auto-time on the waveform's edge off gives
  the old offset (the status says how many ms the lines sit after the onset peak).
- [ ] Settings → Auto-time starts on a bar line: Auto-time on a song with an intro puts the first red line on the
  first beat of a bar that has music (the purple bar lines then fall on the downbeats); the status says how many beats it
  moved. Off: the detector's first beat after 35 ms.
- [ ] recreate.ogg (E44): Auto-time proposes 285.993 BPM and the status says the map may be at half of it. Type 143 in the
  BPM box and Auto-time (or ÷2 then Fit offset): one red line near 140 ms on the 143 grid of the long part. Paste
  `437,419.5804195804196,4,1,0,100,1,0`, `49108,333.3333333333333,4,1,0,100,1,0` and
  `55108,419.5804195804196,4,1,0,100,1,0` (Replace all), move each line 90 ms with the Shift row, select it and press
  Fit offset: it comes back to within 4 ms (437, 49104, 55108). Listen to the metronome on the first hit at 437.

## E37: drop to open, ❮ step ❯ shifts, move timing point here

- [ ] Drag a `.osu` from Explorer onto the window: it opens with its audio, as File → Open does. Drag an audio file:
  it opens on its own. Drag a `.txt`: the cursor shows no drop, and dropping it only says what can be dropped.
- [ ] The Shift row reads **Shift ❮ 1/4 ❯ beat ❮ 1 ❯ ms**. With one red line selected pick 1/32 and press ❯: the line
  moves a thirty-second of its own beat (15 ms at 125 BPM); 1/16 twice that. Pick 5 ms: ❮ moves 5 ms earlier. Each
  press is one Ctrl+Z. Ctrl+←/→ still shift 1 ms. Close and reopen: both drop-downs keep their choice.
- [ ] Select a red line, put the playhead (yellow) where it belongs (click, or ←/→), press **⇥** beside +: the red
  line jumps onto the yellow line, its BPM and meter kept. Ctrl+Shift+P does the same; Ctrl+P (+) still adds a new
  line. With several lines selected they move together. Past the next red line it is refused.

## E35: Check sections, tempo ratios, Fill ramp

- [ ] Open Parallel Universe Shifter with its map, **Check sections** (Ctrl+K): one violet band at 04:43:730 (250 BPM),
  selected in the table. Listen with the metronome: is the map's grid or the one 59 ms later on the drums?
- [ ] Select a section, press ÷2 then ×2: the BPM halves and comes back, the red line stays where it was; Ctrl+Z
  undoes each.
- [ ] Delete a ramp's inner red lines, select its first line and the line after it (Ctrl+click), **Fill ramp**
  (Ctrl+L): a red line per beat, the BPM climbing evenly; listen to the clicks over the music. Where the music holds a
  tempo midway, select that line too and fill again.
- [ ] Edit any red line after a check: the violet bands go away.

## E33: Auto-time range, paste, + / −

- [ ] Shift+drag across the waveform (start on a red line too): a blue band appears and stays; the red line does not
  move. Shift+→ / Shift+← step the playhead by the beat snap and grow / shrink a band from where they started. Esc
  clears it.
- [ ] a crowd of rebellion - Ill: Auto-time, Apply; highlight about 131-169 s, **Auto-time range**
  (Ctrl+R): one 142 BPM red line at about 131 678, and the 220 BPM part after 169 s keeps its timing. Listen to the
  metronome over the drums there, and check the start against the map's 131 722.
- [ ] Ctrl+Z after Auto-time range restores the previous red lines in one step.
- [ ] Copy rows from a .osu or the osu! editor's timing setup, Ctrl+V: red and green rows join, including duplicates.
  Orange overlap markers show same-kind collisions. Ctrl+Shift+V asks, then replaces every red line.
- [ ] **+** puts a red line at the playhead in All/Timing (green in Inherited), **−** deletes selected points, Enter or leaving a field saves its edits (E48 replaces the old ✓ button).
  The To playhead button and Ctrl+Shift+P are gone (E37 brings the move back as ⇥ beside +).

## E32: menu bar, toolbars, shortcuts, remembered settings

- [ ] The menu bar has File, Edit, Timing, Settings and Help; Open/Save/Copy/Undo/Redo/About are no longer toolbar
  buttons, and there is no All / Selected shift box.
- [ ] The three toolbars (playback, Auto-time, Shift) line up: drop-downs, boxes and buttons are the same height as
  their labels' line, with a clear gap between groups. At 900 px wide a group wraps whole onto the next line.
- [ ] Click a row in the timing table: it is selected, the playhead (yellow) does not move. Double-click a row: the
  playhead jumps there. (To playhead was removed in E33; E37 brings it back as ⇥ beside +.)
- [ ] Ctrl+←/→ shift the selected lines 1 ms, Ctrl+Shift+←/→ 5 ms; M toggles the metronome, F follow, F1 lists the
  keys; Ctrl+T runs Auto-time and Ctrl+Enter applies; Ctrl+O / Ctrl+Shift+O / Ctrl+S open and save.
- [ ] After Auto-time no list of "1. xxx ms - xxx BPM" lines appears; the status line summarises the proposal.
- [ ] Change both volumes, the speed, beat snap, auto-time mode, the table tab and the window size; close and
  reopen: they are back. **Settings → Restore default settings** puts them back to 100 % / 70 %, 1.0, 1/4,
  Normal, All, 1280×720.

## E22: short tempo steps (Snap to onsets, BPM from spacing)

- [ ] Yooh - RPG, 252 s: delete the auto-time lines between 229 s and 256 s, then at 0.5x speed put a red line
  (Ctrl+P) on the accents near 252463, 253263, 254120, 254620 and 255165 (the 210 BPM section). Box-select the
  five lines, press **Snap to onsets**, then **BPM from spacing**: 150, 140, 120, 110 and 210, each line on the
  end of the step before (about 7 ms before the official lines; Ctrl+Z twice restores the placed lines).
- [ ] Listen to the ramp with the metronome on: each step's clicks stay on the music.
- [ ] With the 16-beat rule: select one ramp line and the next section's first line; the section line keeps
  its BPM and the status says so.
- [ ] At a small window (about 720 px high) both buttons are fully visible side by side under Apply fields.

## E21: Space, red-line list layout and list follow

- [ ] Click any button or checkbox (e.g. Metronome, Copy timing points), then press Space: playback toggles and
  the clicked control is not pressed again.
- [ ] Run Auto-time (Normal) on a long multi-BPM song: the timing table on the right keeps its height and scrolls
  normally. Try a small window (about 720 px high).
- [ ] Play through a multi-BPM map with Follow on: each time the playhead passes a red line, that row is selected
  and scrolled into view. Paused seeks and Follow off leave the selection alone.

## E20: list jump, copy rows, ⅛ shift, song title

- [ ] Double-click rows in the timing table: the playhead jumps onto each line and the view shows it one third
  from the left; while playing, playback continues from the line. (Since E32 a single click only selects.)
- [ ] Copy timing points and paste into the osu! editor's timing setup: only number rows, no `[TimingPoints]`.
- [ ] Shift −⅛ / +⅛ beat moves each red line by an eighth of its own beat, one undo step.
- [ ] Open a .osu: the window title reads "Artist - Title [Difficulty] (Mapper) - TempoMap by C4P741N";
  open plain audio: its file name.

## Phase 1.3: BPM lock, per-section auto-time, follow and volume

- [ ] Auto-time (Normal) on MAMBO COMBO with the BPM box empty (159.997 / 351), then with `160` (Locked
  BPM 160 / 352). Bad input (`abc`, `0`) shows a message and does not analyze.
- [ ] Open the test map `MBCB (C4P741N) [Test]`. Select the 135 BPM line at 104494 and press **Fit
  offset**: the status names the section and the move (about +2 ms); listen to the metronome there.
  Ctrl+Z restores it. Repeat on 150334 and 173033.
- [ ] Select the 140 BPM line at 140049 and press **Fit offset** (moves about −33 ms): judge by ear
  whether the fitted or the original position is right. Auto-time section there reports an approximate
  70 BPM; typing `140` in the BPM box and pressing Auto-time section fits it.
- [ ] Select a ramp line (e.g. 94525, 3 s long): both buttons refuse with the "too short" hint. Place
  it with ←/→ onto a beat of the previous section and Ctrl+P instead.
- [ ] While playing, drag the Music and Metronome sliders: the change is heard almost at once (about
  0.1 s). Listen for crackles or dropouts at 1.0, 0.75 and 0.5 speed.
- [ ] While playing, Ctrl+wheel zoom: follow continues (see Audio and metronome).

## Auto-time, clipboard and osu! save

- [ ] Run Auto-time on a supported song (prefer 44.1 kHz and under ten minutes). The UI remains responsive
  while analysis runs; editing and loading are disabled until it finishes.
- [ ] Read the exact/approximate label, BPM and osu! offset. Cancel the Apply confirmation and verify
  that red lines stay unchanged. Confirm Apply: one red line replaces the old red lines. Undo restores them.
- [ ] If only approximate BPM is found, the offset is explicitly unavailable. Applying without a manually
  entered offset must fail clearly; supply one and verify the confirmation and applied timing.
- [ ] Copy timing rows into a text editor: invariant numbers, red/green times shifted -33 ms, normalized green rows
  included. Red times are integers; beat lengths retain full precision.
- [ ] Save the disposable `.osu`. Confirm the `.osu.bak` contains the exact pre-save file. Check that only
  timing rows include visible normalization; hit objects and other sections retain their original text.
  Repeat Save and confirm the backup now contains the preceding saved version.
- [ ] Open the saved map in the **osu! editor**. Inspect timing points, inherited SV/sample changes,
  measure resets and beat alignment. Play in osu! to confirm offset and BPM, including section changes.
- [ ] Open audio directly and Save .osu to a new file. Confirm the generated map opens in osu! and its
  relative audio path works. Its metadata is a minimal template; set title/artist/difficulty in osu!.
- [ ] Close while loading audio or analyzing, then reopen. No crash or lingering playback should occur.

Record OS/audio device, song, speed, seek time and observed drift for any synchronization failure.
Record remaining GUI or osu! issues in `docs/PHASE1-STATUS.md`; do not mark this checklist passed merely
because the automated build and tests pass.

## Calibrated Copy and residual audio calibration (E45/E46)

Recorded comparison (2026-10-25): `tm_1000.wav` at 1000 ms / 120 BPM has 42 pairs within 0.05 ms; the osu!
captures show delayed ticks and 10 ms timing steps. See `OSU-AUDIO-TIMING.md`. This confirms this synthetic map's
TempoMap playback, not every Auto-time result or the full checklist below. The user selected fixed -33 ms for this setup.

- [ ] Use the synthetic map and recording procedure in `OSU-AUDIO-TIMING.md` to measure stable's tick/music delay
  at 100% speed on the current device. Record universal offset, compatibility mode and the result's spread.
- [ ] Copy the song timing with the fixed -33 ms compensation; replace the timing points in a disposable osu! map.
  Verify red/green changes remain together and listen near the beginning, middle and end. Check gameplay separately.
- [ ] Keep TempoMap timing visible while copying: it stays unchanged. Copy emits -33 ms externally, while
  Save .osu and internal Ctrl+C/V retain the original times. The separate Copy to osu! dialog is removed.

## Unified workflow (E46)

Automated real-window checks cover both 1280x720/900x500, actual copy/paste clicks and keys, orange markers,
1/64 button, drag/drop conversion, save/reopen and Copy with 553 rows. The following are manual acceptance items.

- [ ] Type a locked BPM and proposal offset, then Open audio or drag/drop another song: both old inputs clear.
- [ ] Import MP3/WAV/FLAC/non-q6 Ogg: a q6 Ogg appears beside the source before the waveform; originals stay intact.
  Repeat open: the existing matching sidecar is reused. An unrelated `.ogg` sibling is preserved with a new name used.
- [ ] Import a `.osu` referencing non-q6 audio, then Save: AudioFilename references the prepared Ogg relatively.
- [ ] Multi-BPM: Inherited/All show main/BPM green rows including x1.00 at the main tempo. Verify main BPM against
  duration over the whole audio, including its tail; authored SV effects remain on top.
- [ ] Copy a selected red or green row, then paste within TempoMap: original times and distinct duplicate rows.
  Both overlaps display orange; Ctrl+Z/Redo restore the whole operation. Copy to a text editor: times are -33 ms.
- [ ] Pick 1/64 in Shift and click earlier/later: each selected red uses its own beat, rounded to integer ms.
- [ ] Close during a long conversion: no lingering ffmpeg process or staged Ogg; original audio remains.

## Point editing and navigation (E48)

Automated checks use real headless MainWindow input at 1280x720 and 900x500. These items cover desktop acceptance.

- [ ] Ctrl+P adds red at the yellow playhead and selects BPM text for typing, including when a field already has focus.
  Enter saves. Adding at an existing red time selects the newly added row.
- [ ] Edit Time/BPM/Meter, then Enter, Tab or click another row/waveform: the original point saves before selection changes.
  Moving Time past another point still selects the row clicked; an unchanged blur adds no undo step.
- [ ] Esc in a field cancels its draft. Invalid Enter leaves focus and shows an error; leaving the invalid field restores
  its saved value. Playback crossing a section while typing does not replace the field's target.
- [ ] Point actions are ordered +, minus, move-to-yellow after Meter (E51), reachable by scrolling the inspector. No checkmark button.
  + / Insert adds red in All/Timing and green in Inherited; the new value field has focus.
- [ ] Ctrl+G adds green from every tab (switching Timing to All), with SV selected. Change Time/SV, then move/delete/undo;
  samples/volume/kiai and the red grid remain. Required automatic normalization rows can reappear after deletion.
- [ ] After Auto-time, Offset and Apply are together before the main BPM note, including at the minimum window size.
  Cancel retains the proposal; Ctrl+Enter while typing Offset applies the edited offset after confirmation.
- [ ] Seek bar start/end icons and Home/End move the yellow line and view to song boundaries, paused or playing,
  including Follow off. Home/End inside a field move the typing caret.
- [ ] Listen while adding/editing points and using the new navigation. Confirm undo/redo, Save/reopen and osu! paste
  preserve the intended timing, SV and audible alignment.

## Workspace appearance (E50)

- [ ] Use the header appearance picker to select Dark, Light and System. Window surfaces, fields, popup menus and
  confirmation dialogs should follow immediately. System should follow the OS appearance, including OS changes.
- [ ] Close and reopen with Light selected; Light returns. Restore default settings and verify Dark returns.
  An existing settings file without a Theme entry opens in Dark.
- [ ] In both themes inspect waveform, subdivisions/beats/bars, timing markers, selected points, range highlight and
  amber playhead. Primary button text stays readable on its mint/teal fill, also on hover and keyboard focus.
- [ ] At 900x500 and 1280x720, open a long song title and Auto-time proposal. The theme picker, Copy/Paste, point
  toolbar, Offset/Apply and seek controls stay reachable; text trims and panels scroll without covering buttons.
- [ ] Repeat Ctrl+P/Ctrl+G, field Enter/Esc/blur, delete/move/undo, copying many rows and seek navigation in both
  themes. Changing appearance must keep timing edits, navigation, playback and pending proposals.
- [ ] With no audio, the waveform explains open/drop. Loading a song removes the hint and names it in the waveform
  header. Check desktop DPI scaling and native title bar/menus on the actual display.

## Inline analysis, SV and transport follow-up (E51)

- [ ] Auto-time fills the existing BPM/Offset/Apply group without adding a result panel. Edit both values and Apply;
  multi-BPM still applies all proposed red lines. At 900x500 the group wraps together with its fields on one line.
  Type over a previous BPM result and run Auto-time twice: keep the typed BPM and fit only offset (E52).
  Check both whole and fractional BPM, then Apply; clearing the BPM box restores automatic detection.
- [ ] Metronome/Music percentages follow their sliders; 0.25 speed plays with pitch preserved and synced clicks.
- [ ] Add green at a chorus start and enable Kiai; add another at its end and disable Kiai. Copy/Save/reopen and
  check the effect in osu! without changing the intended SV. The point buttons sit after Meter in both themes.
- [ ] Manual SV is default. Enter 1.75, edit/add red lines, Save/reopen: green values stay fixed. Enable Auto normalize,
  edit BPM, then Undo/Redo through the mode switch: mode and values restore together. Disabling freezes visible values.
- [ ] Enter multiplier 0.5 and press Apply repeatedly: BPM halves each press and Undo reverses one press. Invalid/zero
  factors are rejected. Confirm Kiai and SV are not changed in Manual mode.
- [ ] Go to song end with End/the icon, change speed/timing there, seek back or Play: no BASS error 7, Play restarts.
  Check an Ogg with a fractional duration and verify the actual speaker output at 0.25.
- [ ] The mint logo matches the header/window/taskbar/Explorer; EXE and app title use TempoMap (E53-E54).
  F2 opens the Thai tool guide; close stays visible on
  short windows. File has one Copy all command; selected Copy and the Settings SV mode match the visible rows.
