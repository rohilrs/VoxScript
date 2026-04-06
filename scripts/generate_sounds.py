#!/usr/bin/env python3
"""Generate woodblock/click-track WAV files for VoxScript recording cues."""

import struct
import math
import os

SAMPLE_RATE = 44100
BITS = 16
CHANNELS = 1


def generate_click(freq: float, duration_ms: float = 100, attack_ms: float = 1,
                   decay_ms: float = 60) -> list[int]:
    """Generate a single woodblock click: sharp attack, fast exponential decay."""
    n_samples = int(SAMPLE_RATE * duration_ms / 1000)
    attack_samples = int(SAMPLE_RATE * attack_ms / 1000)
    decay_samples = int(SAMPLE_RATE * decay_ms / 1000)
    samples = []
    for i in range(n_samples):
        # Sine tone at the given frequency
        t = i / SAMPLE_RATE
        tone = math.sin(2 * math.pi * freq * t)

        # Add a second harmonic for woodblock character
        tone += 0.3 * math.sin(2 * math.pi * freq * 2.8 * t)

        # Envelope: linear attack, exponential decay
        if i < attack_samples:
            env = i / max(attack_samples, 1)
        elif i < attack_samples + decay_samples:
            decay_pos = (i - attack_samples) / max(decay_samples, 1)
            env = math.exp(-5 * decay_pos)
        else:
            env = 0.0

        sample = int(tone * env * 28000)
        sample = max(-32768, min(32767, sample))
        samples.append(sample)
    return samples


def write_wav(filepath: str, samples: list[int]) -> None:
    """Write 16-bit mono PCM WAV."""
    data_size = len(samples) * 2
    file_size = 36 + data_size
    with open(filepath, "wb") as f:
        # RIFF header
        f.write(b"RIFF")
        f.write(struct.pack("<I", file_size))
        f.write(b"WAVE")
        # fmt chunk
        f.write(b"fmt ")
        f.write(struct.pack("<I", 16))  # chunk size
        f.write(struct.pack("<H", 1))   # PCM format
        f.write(struct.pack("<H", CHANNELS))
        f.write(struct.pack("<I", SAMPLE_RATE))
        f.write(struct.pack("<I", SAMPLE_RATE * CHANNELS * BITS // 8))
        f.write(struct.pack("<H", CHANNELS * BITS // 8))
        f.write(struct.pack("<H", BITS))
        # data chunk
        f.write(b"data")
        f.write(struct.pack("<I", data_size))
        for s in samples:
            f.write(struct.pack("<h", s))


def main() -> None:
    out_dir = os.path.join(os.path.dirname(__file__), "..", "VoxScript", "Assets", "Sounds")
    os.makedirs(out_dir, exist_ok=True)

    # Start: ~800Hz single click
    start_samples = generate_click(800, duration_ms=120)
    write_wav(os.path.join(out_dir, "start.wav"), start_samples)

    # Toggle: ~650Hz single click
    toggle_samples = generate_click(650, duration_ms=120)
    write_wav(os.path.join(out_dir, "toggle.wav"), toggle_samples)

    # Stop: ~500Hz double click (two taps with 80ms gap)
    tap1 = generate_click(500, duration_ms=100)
    gap = [0] * int(SAMPLE_RATE * 0.08)  # 80ms silence
    tap2 = generate_click(500, duration_ms=100)
    stop_samples = tap1 + gap + tap2
    write_wav(os.path.join(out_dir, "stop.wav"), stop_samples)

    print(f"Generated WAVs in {out_dir}")


if __name__ == "__main__":
    main()
