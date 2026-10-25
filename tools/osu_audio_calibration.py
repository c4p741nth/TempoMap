"""Create a synthetic osu! audio test; measure tick-vs-music delay in a stereo loopback recording.

Uses the existing numpy/scipy/soundfile environment. No microphone/input-response latency is measured.
"""
import argparse
import json
import math
from pathlib import Path
import zipfile

import numpy as np
from scipy.signal import correlate, find_peaks
import soundfile as sf


def reference_audio(rate=44100):
    """Known beats from 1000 ms at 120 BPM, in the left channel only."""
    audio = np.zeros((24 * rate, 2), dtype=np.float32)
    t = np.arange(round(rate * .020)) / rate
    pulse = .4 * np.cos(2 * np.pi * 750 * t) * np.exp(-t * 220)
    for start in range(rate, 23 * rate, rate // 2):
        audio[start:start + len(pulse), 0] = pulse
    return audio


def create(folder):
    folder.mkdir(parents=True, exist_ok=True)
    audio = folder / "reference.wav"
    sf.write(audio, reference_audio(), 44100, subtype="PCM_16")
    beatmap = ("osu file format v14\r\n\r\n[General]\r\nAudioFilename: reference.wav\r\nMode: 0\r\n\r\n"
               "[Metadata]\r\nTitle:Audio calibration\r\nArtist:Tempomap\r\nCreator:Tempomap\r\nVersion:120 BPM\r\n\r\n"
               "[Difficulty]\r\nHPDrainRate:5\r\nCircleSize:4\r\nOverallDifficulty:5\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n"
               "[Events]\r\n\r\n[TimingPoints]\r\n1000,500,4,1,0,100,1,0\r\n\r\n"
               "[HitObjects]\r\n256,192,23500,1,0,0:0:0:0:\r\n")
    name = "Tempomap - Audio calibration (Tempomap) [120 BPM].osu"
    (folder / name).write_bytes(beatmap.encode("utf-8"))
    package = folder / "Tempomap-audio-calibration.osz"
    with zipfile.ZipFile(package, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.write(audio, audio.name)
        archive.writestr(name, beatmap)
    return package


def onsets(channel, rate):
    # Tick attack: a causal 1 ms RMS envelope with a threshold relative to each tick's peak.
    size = max(1, round(rate / 1000))
    envelope = np.sqrt(np.convolve(channel ** 2, np.ones(size) / size, mode="full")[:len(channel)])
    maximum = float(np.max(envelope))
    if maximum < 1e-5:
        return np.array([])
    peaks, _ = find_peaks(envelope, height=maximum * .2, prominence=maximum * .15, distance=round(rate * .3))
    starts = []
    for peak in peaks:
        start = peak
        floor = max(0, peak - round(rate * .1))
        while start > floor and envelope[start - 1] > envelope[peak] * .15:
            start -= 1
        starts.append(start / rate * 1000)
    return np.array(starts)


def reference_onsets(channel, rate):
    """Locate the known left-only pulse rather than tick residue from imperfect stereo cancellation."""
    pulse = reference_audio(rate)[rate:rate + round(rate * .020), 0]
    if len(channel) < len(pulse):
        return np.array([])
    scores = correlate(channel, pulse, mode="valid", method="fft")
    if float(np.max(scores)) <= 1e-12:
        return np.array([])
    peaks, _ = find_peaks(scores, height=np.max(scores) * .15, distance=round(rate * .3))
    energy = np.concatenate(([0.0], np.cumsum(channel ** 2)))
    pulse_energy = float(np.dot(pulse, pulse))
    matches = []
    for peak in peaks:
        window_energy = energy[peak + len(pulse)] - energy[peak]
        similarity = scores[peak] / np.sqrt(max(window_energy * pulse_energy, 1e-30))
        if similarity >= .85:
            matches.append(peak / rate * 1000)
    return np.array(matches)


def measure(audio, rate):
    if audio.ndim != 2 or audio.shape[1] != 2:
        raise ValueError("Use a stereo loopback recording; mono cannot separate music from metronome.")
    if not np.all(np.isfinite(audio)) or np.max(np.abs(audio)) >= .999:
        raise ValueError("Recording is non-finite or clipped; lower both volumes and record again.")
    left, right = audio[:, 0], audio[:, 1]
    # Song is left-only, and osu!'s centered tick is in both channels. Removing the right recovers the song.
    if np.mean(left ** 2) <= 1.05 * np.mean(right ** 2):
        raise ValueError("Left-only reference music is missing or channels were mixed/swapped. Disable mono/spatial processing.")
    music = reference_onsets(left - right, rate)
    ticks = onsets(right, rate)
    if len(music) < 10 or len(ticks) < 10:
        raise ValueError("Need at least 10 reference beats and metronome ticks; record 10 seconds or longer with both audible.")
    if abs(float(np.median(np.diff(music))) - 500) > 2:
        raise ValueError("Reference pulses are not 500 ms apart. Use the generated map at 100% speed with no extra audio.")
    deltas = []
    # Ignore the first and last pulse: a recording/seek may truncate either one.
    for start in music[1:-1]:
        tick = ticks[np.argmin(np.abs(ticks - start))]
        if abs(tick - start) < 200:
            deltas.append(float(tick - start))
    if len(deltas) < 8 or len(deltas) < .8 * (len(music) - 2):
        raise ValueError("Too few matching ticks. Keep 120 BPM, offset 1000 ms, metronome enabled, and Ctrl released.")
    median = float(np.median(deltas))
    minimum, maximum = min(deltas), max(deltas)
    note = "Residual tick-vs-music timing in this recording, including sample attack; not total device latency."
    if maximum - minimum > 2:
        note += " Tick timing varies; copy_shift_ms targets the median only and cannot align every tick."
    return dict(pairs=len(deltas), tick_late_ms=round(median, 3),
                p10_ms=round(float(np.percentile(deltas, 10)), 3),
                p90_ms=round(float(np.percentile(deltas, 90)), 3),
                min_ms=round(minimum, 3), max_ms=round(maximum, 3), range_ms=round(maximum-minimum, 3),
                copy_shift_ms=-math.floor(median + .5),
                note=note)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    make = commands.add_parser("create", help="Write the synthetic WAV, .osu and importable .osz")
    make.add_argument("folder", type=Path)
    read = commands.add_parser("measure", help="Measure a stereo WAV/FLAC loopback recording of this map in osu!")
    read.add_argument("recording", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "create":
            package = create(args.folder)
            print("Created " + str(package))
            print("Import into osu!, F3, 120 BPM / offset 1000 ms / 100% speed. Record stereo loopback with music and tick.")
        else:
            audio, rate = sf.read(args.recording, dtype="float64", always_2d=True)
            print(json.dumps(measure(audio, rate), indent=2))
    except (ValueError, RuntimeError) as exc:
        parser.exit(1, str(exc) + "\n")


if __name__ == "__main__":
    main()
