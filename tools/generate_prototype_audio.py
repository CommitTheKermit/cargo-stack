#!/usr/bin/env python3
"""Generate the small, original prototype sound set used by Cargo Stack."""

import math
import random
import struct
import wave
from pathlib import Path


SAMPLE_RATE = 44_100
OUTPUT_DIR = Path(__file__).resolve().parents[1] / "Assets" / "Audio" / "Prototype"


def write_wave(name: str, samples: list[float]) -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    peak = max(abs(sample) for sample in samples) or 1.0
    scale = 0.92 / max(1.0, peak)
    pcm = b"".join(
        struct.pack("<h", int(max(-1.0, min(1.0, sample * scale)) * 32767))
        for sample in samples
    )
    with wave.open(str(OUTPUT_DIR / name), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(pcm)


def engine_loop() -> list[float]:
    duration = 2.0
    count = int(SAMPLE_RATE * duration)
    result = []
    for index in range(count):
        time = index / SAMPLE_RATE
        # Every oscillator completes an integer number of cycles in two seconds,
        # so the clip can loop without a click at its seam.
        pulse = (
            0.68 * math.sin(math.tau * 42 * time)
            + 0.26 * math.sin(math.tau * 84 * time + 0.4)
            + 0.13 * math.sin(math.tau * 126 * time + 1.1)
        )
        combustion = 0.74 + 0.26 * math.sin(math.tau * 6 * time)
        result.append(math.tanh(pulse * combustion * 1.25) * 0.72)
    return result


def cargo_thump(seed: int, base_frequency: float, duration: float) -> list[float]:
    rng = random.Random(seed)
    count = int(SAMPLE_RATE * duration)
    result = []
    smoothed_noise = 0.0
    for index in range(count):
        time = index / SAMPLE_RATE
        attack = min(1.0, time / 0.003)
        body_decay = math.exp(-time * 19.0)
        noise_decay = math.exp(-time * 34.0)
        smoothed_noise += (rng.uniform(-1.0, 1.0) - smoothed_noise) * 0.16
        body = (
            math.sin(math.tau * base_frequency * time)
            + 0.34 * math.sin(math.tau * base_frequency * 1.71 * time + 0.6)
        )
        result.append(attack * (0.72 * body * body_decay + 0.34 * smoothed_noise * noise_decay))
    return result


def main() -> None:
    write_wave("engine_idle_loop.wav", engine_loop())
    write_wave("cargo_thump_01.wav", cargo_thump(1103, 86.0, 0.28))
    write_wave("cargo_thump_02.wav", cargo_thump(2207, 73.0, 0.31))
    write_wave("cargo_thump_03.wav", cargo_thump(3301, 98.0, 0.24))
    print(f"Generated prototype audio in {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
