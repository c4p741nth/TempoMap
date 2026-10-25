# Audio input, optional conversion and timing clipboard

## Opening and saving

Open/drop uses the original audio for waveform, analysis and playback. It does not run ffmpeg/ffprobe,
create Ogg sidecars or change the audio used by other difficulties in a mapset. The native Windows/BASS
decoder handles MP3, WAV, Ogg and available system codecs (FLAC is exercised by the native smoke checks).
BPM lock and pending analysis values reset when another song is opened.

Save keeps the source AudioFilename. Save As adjusts its relative path for the new directory while still
referencing the same source audio. Encoding an optional Ogg does not automatically switch the current map's audio.

Timing files are written to a same-directory temporary file, flushed, then atomically installed. The first
existing destination is backed up once per editing session. An existing `.osu.bak` is never overwritten:
later sessions use a distinct timestamped backup. Save errors leave the prior target intact.

## Optional Ogg conversion

Use the separate conversion command when you want a new Ogg. Choose its output filename, sample rate
(Original by default) and Vorbis quality (-1 to 10; default **6**). The source file is never overwritten.
An existing output is backed up before atomic replacement, and a failed/cancelled conversion removes its staged file.

Only this optional command needs `ffmpeg`, either beside TempoMap.exe or on PATH. `ffprobe` is optional for
format diagnostics. Missing conversion tools do not prevent opening MP3/WAV/FLAC/Ogg. Other containers depend on available native codecs.
Opus was unavailable on this workstation; its error points to the explicit converter. No automatic sidecar is created.
No external encoder binary is bundled in the release; the release README states that requirement.

## Clipboard

Copy/Ctrl+C exports selected timing rows with compensation **-33 ms by default**, including greens.
Settings → Keyboard shortcuts / copy profiles edits the value or stores a named setup. Ctrl+Shift+C
copies all rows. Internal clipboard data retains the source times so Ctrl+C/V within TempoMap creates duplicates
at the same times. Orange overlap markers identify red/red or green/green collisions; a red and green at the
same time is normal. Undo/redo includes pasted greens. For duplicate reds, the first controls playback and beat snap.

Manual SV is the default: entered green values stay fixed. With Auto normalize SV enabled, Inherited/All show
normalization to the BPM held longest across the whole audio; main-tempo
sections get explicit x1.00 lines. Existing SV/sample/volume/kiai effects are preserved on top. The normalization
updates with red edits and is included in Copy/Save; reopening recognizes already normalized rows. This full-audio
reference was explicitly requested and may differ from osu!'s main BPM measured to the last hit object.

Beat shifts include 1/64: at 120 BPM this is 7.8125 ms, rounded to 8 ms in osu! integer offsets.
