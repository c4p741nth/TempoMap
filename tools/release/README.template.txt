TempoMap {version} - timing editor for osu! by C4P741N

For Windows (x64). MIT license. Run TempoMap.exe; no .NET install is needed.

Opening MP3/WAV/FLAC/Ogg does not need ffmpeg and never automatically converts your music.
The optional Ogg converter needs ffmpeg.exe beside the app or on PATH (quality 6 by default);
ffprobe.exe is optional for diagnostics. Save preserves the original audio reference.

- Open audio or an .osu (File menu, or drop it on the window), press Auto-time (Normal suits almost every song), review, then Apply...
- Auto-time puts its red lines on the leading edge of the hit in the waveform (about 18 ms after the onset peak the original
  detector reports) and the first red line on the first beat of a bar. Settings has a switch for each.
- Normal also runs a small beat model trained on ranked osu! maps (Models\beatnet.onnx, CPU, about 2 s per song)
  and uses it by itself where it is clearly better; the status line says when it did.
- Beat model mode (beside Normal) uses the model for the whole song: for hard multi-BPM songs. Listen to the result.
- Drift modes follow a drifting tempo; Drift 1 beat puts a red line on every drum hit (drums played live).
- Highlight a range (Shift+drag or Shift+Left/Right) and press Ctrl+R to re-time just that part.
- Check sections (Ctrl+K) marks the parts worth a listen; x2 / /2 fix a wrong octave; Fill ramp (Ctrl+L) writes a
  red line per beat between two selected red lines.
- Hold Left/Right and the playhead runs on, faster the longer you hold. Workspace (F3) shift/nudge tools (or Ctrl+Up/Down)
  tune the BPM of the selected red lines against the waveform.
- File > Save project keeps a .tmap session; recovery and an unsaved-work prompt protect edits.
- F3 opens range loop/pre-roll, tap/anchors, display layers and proposal A/B review. File also exports .osz with audio.
- Settings > Keyboard shortcuts / copy profiles changes keys and clipboard compensation (default -33 ms).
- Native Opus decoding is unavailable in this build; use the explicit converter with ffmpeg, then open the result.
- Help > Keys and colours (F1) lists every shortcut. Settings are kept between runs. Esc closes a dialog.

Credits: audio through BASS by un4seen Developments (free for non-commercial use, https://www.un4seen.com) via
ManagedBass; UI by Avalonia; ONNX Runtime by Microsoft (MIT); CommunityToolkit.Mvvm; the .NET runtime.
