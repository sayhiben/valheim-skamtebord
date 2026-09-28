"""Encode a completed 40-second BuildRideDemo capture with its game audio."""
import argparse
from pathlib import Path
import subprocess
import wave

import imageio_ffmpeg


def encode(root: Path, output: Path) -> None:
    frames = root / "demo-frames"
    expected = [frames / f"frame_{index:05d}.png" for index in range(1200)]
    if not all(frame.is_file() for frame in expected):
        raise ValueError("Capture must contain all 1200 frames")
    if "SUCCESS" not in (root / "smoke-result.txt").read_text(encoding="utf-8-sig"):
        raise ValueError("Demo runtime checks did not finish successfully")
    with wave.open(str(root / "demo-audio.wav"), "rb") as audio:
        duration = audio.getnframes() / audio.getframerate()
        if audio.getnchannels() != 2 or abs(duration - 40) > 0.1:
            raise ValueError(f"Expected synchronized stereo audio; got {duration}s")
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([
        imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "warning", "-y",
        "-framerate", "30", "-i", str(frames / "frame_%05d.png"),
        "-i", str(root / "demo-audio.wav"), "-map", "0:v:0", "-map", "1:a:0",
        "-c:v", "libx264", "-preset", "medium", "-crf", "19", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-t", "40", str(output)
    ], check=True)
    print(f"Wrote {output}: 40s, 1280x720, 30fps, stereo game audio")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    encode(args.root, args.output)
