#!/usr/bin/env python3
"""Synthesises AVAEliminator's sound effects, the base's hum and the tunes into ../src/AVAEliminator/Sounds as
16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle, saw, sine and noise
voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAEliminator", "Sounds")
RNG = random.Random(1981)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def hum():
    """The base's hum: exactly one second, every voice a whole number of cycles in it, so it loops without a click."""
    out = []
    for i in range(RATE):
        t = i / RATE
        throb = 0.6 + 0.4 * math.sin(2 * math.pi * 2 * t) ** 2
        s = 0.55 * math.sin(2 * math.pi * 55 * t) + 0.3 * math.sin(2 * math.pi * 110 * t + 0.4) + 0.12 * math.sin(2 * math.pi * 165 * t)
        s += 0.06 * math.sin(2 * math.pi * 330 * t) * math.sin(2 * math.pi * 4 * t)
        out.append(s * throb)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ships: a shot's zap, the engine's rumble, a shot glancing off the base, and a ship bumping into it.
    write("Fire.wav", tone(0.11, 1700, 550, "square", release=0.6), 0.2)
    write("Thrust.wav", tone(0.24, 150, 110, "noise", attack=0.03, release=0.4), 0.22)
    write("Ricochet.wav", mix(tone(0.12, 2600, 3600, "tri", release=1.2), tone(0.05, 5000, 2000, "noise", release=0.8, gain=0.3)), 0.2)
    write("Clank.wav", mix(tone(0.18, 190, 150, "tri", release=0.7), tone(0.18, 283, 240, "square", release=1.2, gain=0.25), tone(0.06, 1200, 400, "noise", release=0.8, gain=0.5)), 0.3)
    # The base: an eliminator spat out (a rising, warbling howl), one shot down, a hit on the core, and the base going up.
    write("Bolt.wav", mix(tone(0.45, 80, 700, "saw", attack=0.02, release=0.5, vibrato=0.25, vib_rate=22), tone(0.45, 120, 900, "square", attack=0.02, release=0.6, gain=0.3)), 0.34)
    write("BoltDown.wav", mix(tone(0.3, 3000, 300, "noise", release=0.5), tone(0.25, 1600, 200, "tri", release=0.6, gain=0.5)), 0.32)
    write("CoreHit.wav", mix(tone(0.7, 220, 200, "sine", release=1.4), tone(0.7, 331, 300, "sine", release=1.8, gain=0.5), tone(0.5, 662, 600, "tri", release=2.0, gain=0.25), tone(0.15, 1500, 300, "noise", release=0.6, gain=0.5)), 0.5)
    blast = mix(tone(2.6, 1400, 30, "noise", release=0.6), tone(2.4, 120, 25, "saw", release=0.5, gain=0.8), tone(1.6, 60, 20, "sine", release=0.4, gain=0.9))
    write("BaseBlast.wav", cat(blast, *[tone(0.09, hz(n), kind="pulse", release=0.4, gain=0.6) for n in ["D5", "F5", "A5", "D6", "F6", "A6", "D7"]]), 0.6)
    # The drones: their shot, and going down.
    write("DroneFire.wav", tone(0.14, 1300, 650, "pulse", release=0.6), 0.2)
    write("DroneDown.wav", mix(tone(0.45, 1000, 90, "noise", release=0.5), tone(0.4, 600, 70, "square", release=0.5, gain=0.45)), 0.38)
    write("ShipDown.wav", cat(mix(tone(0.9, 1200, 40, "noise", release=0.3), tone(0.8, 420, 30, "saw", release=0.3, gain=0.7)), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["F5", "D5", "A4"]]), 0.5)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["D5", "A5", "D6", "F6"]]), 0.3)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["A5", "D6", "A5", "D6", "A6"]]), 0.38)
    write("Hum.wav", hum(), 0.16)

    # The title: a dark, marching theme in D minor (an original composition).
    lead = [("D5", 0.5), ("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("E5", 1), ("C5", 1), ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 1), ("E5", 1),
            ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5), ("C6", 1), ("A5", 1), ("Bb5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("D5", 2)]
    bass = [("D2", 0.5), ("D3", 0.5)] * 4 + [("F2", 0.5), ("F3", 0.5)] * 4 + [("Bb1", 0.5), ("Bb2", 0.5)] * 4 + [("A1", 0.5), ("A2", 0.5)] * 2 + [("D2", 2)]
    write("TitleTune.wav", cat(tune(132, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(156, [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5), ("C6", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D2", 2), ("Bb1", 2), ("A1", 2), ("D2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
