"""Encode real Unity PNG captures; requires imageio-ffmpeg in a local Python venv."""
from __future__ import annotations

import argparse
import csv
from pathlib import Path
import subprocess

import imageio_ffmpeg


def stamp(seconds: float) -> str:
    centiseconds = round(seconds * 100)
    hours, remainder = divmod(centiseconds, 360000)
    minutes, remainder = divmod(remainder, 6000)
    whole, fraction = divmod(remainder, 100)
    return f"{hours}:{minutes:02}:{whole:02}.{fraction:02}"


def ass_text(text: str) -> str:
    return text.replace("\\", "\\\\").replace("{", "(").replace("}", ")").replace("\n", r"\N")


def render(folder: Path, destination: Path, title: str, subtitle: str, fps: int = 30,
           start: float = 0, end: float | None = None, speed: float = 1) -> None:
    frames = sorted(folder.glob("frame_*.png"))
    if not frames:
        raise ValueError(f"No rendered frames in {folder}")
    first = round(start * fps)
    last = min(len(frames), round(end * fps)) if end is not None else len(frames)
    if speed <= 0 or first < 0 or last <= first:
        raise ValueError("Invalid capture range or playback speed")
    count = last - first
    duration = count / fps / speed
    with (folder / "telemetry.csv").open(newline="", encoding="utf-8-sig") as source:
        rows = list(csv.DictReader(source))[first:last]
    if len(rows) != count:
        raise ValueError("Telemetry and frame counts disagree")
    subtitles = destination.with_suffix(".ass")
    header = """[Script Info]
ScriptType: v4.00+
PlayResX: 960
PlayResY: 540
WrapStyle: 2

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Title,Segoe UI,25,&H003DD4FF,&H000000FF,&H00232323,&H00000000,-1,0,0,0,100,100,0,0,1,0,0,7,22,22,12,1
Style: Info,Segoe UI,16,&H00FFFFFF,&H000000FF,&H00232323,&H00000000,0,0,0,0,100,100,0,0,1,0,0,1,22,22,12,1
Style: Live,Segoe UI,22,&H003DD4FF,&H000000FF,&H00232323,&H00000000,-1,0,0,0,100,100,0,0,1,0,0,1,22,22,42,1
Style: Speed,Segoe UI,22,&H00FFFFFF,&H000000FF,&H00232323,&H00000000,-1,0,0,0,100,100,0,0,1,0,0,3,22,22,42,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    events = [f"Dialogue: 0,0:00:00.00,{stamp(duration)},Title,,0,0,0,,{ass_text(title)}"]
    # The captions are authored labels; the underlying frames remain the actual game renderer.
    events.append(f"Dialogue: 0,0:00:00.00,{stamp(duration)},Info,,0,0,0,,{ass_text(subtitle)}")
    for index in range(0, count, 3):
        row = rows[index]
        begin, finish = stamp(index / fps / speed), stamp(min(index + 3, count) / fps / speed)
        phase = row["phase"]
        if "slope" in phase:
            phase = "12 degree descent | No pushing"
        elif phase == "Banked XP":
            phase = "Rolling away"
        if "tricks" in folder.name:
            if int(row["earned_xp"]) > 0:
                phase = f"Banked +{row['earned_xp']} Skamtebord XP"
            elif int(row["combo_points"]) > 0:
                action = "Landed" if row["grounded"] == "True" else phase
                phase = f"{action}   |   Combo: {row['combo_points']}"
        velocity = float(row["speed_mps"]) * 3.6
        events.append(f"Dialogue: 1,{begin},{finish},Live,,0,0,0,,{ass_text(phase)}")
        events.append(f"Dialogue: 1,{begin},{finish},Speed,,0,0,0,,{velocity:.1f} km/h")
    subtitles.write_text(header + "\n".join(events) + "\n", encoding="utf-8-sig")
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    escaped_ass = str(subtitles.resolve()).replace("\\", "/").replace(":", r"\:")
    video_filter = (
        "drawbox=x=0:y=0:w=iw:h=52:color=0x112423@0.88:t=fill,"
        "drawbox=x=0:y=ih-80:w=iw:h=80:color=0x112423@0.88:t=fill,"
        f"subtitles=filename='{escaped_ass}'"
    )
    subprocess.run([
        ffmpeg, "-hide_banner", "-loglevel", "warning", "-y", "-framerate", str(fps * speed),
        "-start_number", str(first), "-i", str(folder / "frame_%05d.png"),
        "-frames:v", str(count), "-vf", video_filter,
        "-c:v", "libx264", "-preset", "medium", "-crf", "19", "-pix_fmt", "yuv420p",
        "-movflags", "+faststart", "-an", str(destination.with_suffix(".mp4")),
    ], check=True)
    subprocess.run([
        ffmpeg, "-hide_banner", "-loglevel", "warning", "-y", "-i", str(destination.with_suffix(".mp4")),
        "-filter_complex", "fps=12,scale=720:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=3",
        "-loop", "0", str(destination.with_suffix(".gif")),
    ], check=True)
    print(f"{destination.name}: {count} frames, {duration:.2f}s; MP4 + GIF")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folder", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--title", required=True)
    parser.add_argument("--subtitle", required=True)
    parser.add_argument("--fps", default=30, type=int)
    parser.add_argument("--start", default=0, type=float)
    parser.add_argument("--end", type=float)
    parser.add_argument("--speed", default=1, type=float)
    args = parser.parse_args()
    args.destination.parent.mkdir(parents=True, exist_ok=True)
    render(args.folder, args.destination, args.title, args.subtitle, args.fps, args.start, args.end, args.speed)
