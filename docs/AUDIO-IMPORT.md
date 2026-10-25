# Audio import and timing clipboard

Open audio, drag/drop and the audio referenced by an imported `.osu` all use the same preparation step before
waveform decoding, analysis or playback. BPM lock and proposal offset inputs reset on every new open/drop.

Audio that is not recognized as Ogg Vorbis quality 6 is encoded using `libvorbis -q:a 6`. Quality 6 is VBR,
approximately 192 kb/s for stereo; the file's measured average bitrate is not a quality test. See the
[Audacity Ogg options](https://manual.audacityteam.org/man/ogg_vorbis_export_options.html) and
[FFmpeg libvorbis options](https://ffmpeg.org/ffmpeg-codecs.html#libvorbis).

Both `ffmpeg` and `ffprobe` must be beside Tempomap.exe or available on PATH. They are installed on the development
machine. No new NuGet packages or FFmpeg binaries were added to the repository/package. Missing tools give an
actionable open error; the editor does not silently load the unconverted original. Redistribution of external
executables and their licences remains a packaging decision for a future release.

- `song.mp3` normally creates `song.ogg` beside it. A non-q6 `song.ogg` creates `song.q6.ogg`.
- Occupied names are preserved. Further names use `.q6.2.ogg`, `.q6.3.ogg`, etc.; an unrelated sibling is never overwritten.
- Generated files have Vorbis quality/source-SHA256 tags. Opening the same source reuses a matching generated
  sidecar; changing its contents creates another sidecar. Sources remain untouched.
- Existing q6 Oggs pass through. For untagged files, Vorbis's nominal bitrate is the compatibility signal:
  192000 for stereo or 110000 for mono. This does not prove the historical encoder's exact quality setting;
  the setting is not generally recoverable from an arbitrary Vorbis file. An explicit Tempomap q6 tag identifies
  our own outputs, including other rates/channel counts. Other files are converted conservatively.
- Conversion writes a unique staged Ogg, probes it, then moves it into a free final name. Failure/cancel removes
  only that staged file. Closing while converting kills the encoder child.
- The converted path is used for waveform, analysis and playback. Save .osu writes its relative AudioFilename.

The existing Copy button/Ctrl+C exports selected timing rows with fixed **-33 ms**, including greens. Ctrl+Shift+C
copies all rows. Internal clipboard data retains the source times so Ctrl+C/V within Tempomap creates duplicates
at the same times. Orange overlap markers identify red/red or green/green collisions; a red and green at the
same time is normal. Undo/redo includes pasted greens. For duplicate reds, the first controls playback and beat snap.

For multiple BPMs, Inherited/All show normalization to the BPM held longest across the whole audio; main-tempo
sections get explicit x1.00 lines. Existing SV/sample/volume/kiai effects are preserved on top. The normalization
updates with red edits and is included in Copy/Save; reopening recognizes already normalized rows. This full-audio
reference was explicitly requested and may differ from osu!'s main BPM measured to the last hit object.

Beat shifts include 1/64: at 120 BPM this is 7.8125 ms, rounded to 8 ms in osu! integer offsets.
