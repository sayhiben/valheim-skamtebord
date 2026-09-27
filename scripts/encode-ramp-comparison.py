"""Make a labelled, half-speed comparison from actual RampSmoke GPU frames."""
import argparse
import csv
from pathlib import Path
import subprocess

import imageio_ffmpeg


def encode(root: Path, output: Path) -> None:
    with (root / "ramps.csv").open(newline="", encoding="utf-8-sig") as source:
        results = {row["case"]: row for row in csv.DictReader(source)}
    names = ("roll-normal", "jump-lip")
    folders = [root / "ramp-video" / name for name in names]
    counts = [len(list(folder.glob("frame_*.png"))) for folder in folders]
    if min(counts) < 10:
        raise ValueError("Both ramp takes need rendered frames")
    # Frames were recorded at 30fps: playback at 15fps is explicitly half-speed.
    duration = (max(counts) - 5) / 15 + .4
    output.parent.mkdir(parents=True, exist_ok=True)
    subtitles = output.with_suffix(".ass")
    header = """[Script Info]
ScriptType: v4.00+
PlayResX: 1280
PlayResY: 400

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Label,Segoe UI,22,&H00FFFFFF,&H000000FF,&H001A2222,&H00000000,-1,0,0,0,100,100,0,0,1,1,0,7,20,20,12,1
Style: Note,Segoe UI,17,&H00CFDDDD,&H000000FF,&H001A2222,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,20,20,10,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    left, right = (float(results[name]["apex_above_floor"]) for name in names)
    events = [
        f"Dialogue: 0,0:00:00.00,0:01:00.00,Label,,0,0,0,,{{\\pos(20,12)}}Roll off | Peak {left:.2f} m",
        f"Dialogue: 0,0:00:00.00,0:01:00.00,Label,,0,0,0,,{{\\pos(660,12)}}Jump at the lip | Peak {right:.2f} m",
        "Dialogue: 0,0:00:00.00,0:01:00.00,Note,,0,0,0,,Same 12 m/s approach | Half-speed replay | Height above the flat floor | Actual Valheim physics",
    ]
    subtitles.write_text(header + "\n".join(events) + "\n", encoding="utf-8-sig")
    ass_path = str(subtitles.resolve()).replace("\\", "/").replace(":", r"\:")
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    command = [ffmpeg, "-hide_banner", "-loglevel", "warning", "-y"]
    for folder in folders:
        command += ["-framerate", "15", "-start_number", "5", "-i", str(folder / "frame_%05d.png")]
    filters = (
        "[0:v]scale=640:360,tpad=stop_mode=clone:stop_duration=4[l];"
        "[1:v]scale=640:360,tpad=stop_mode=clone:stop_duration=4[r];"
        "[l][r]hstack=shortest=1,pad=1280:400:0:0:color=0x112423,"
        "drawbox=x=0:y=0:w=iw:h=44:color=0x112423@0.9:t=fill,"
        f"subtitles=filename='{ass_path}'"
    )
    subprocess.run(command + ["-filter_complex", filters, "-t", str(duration), "-an", "-c:v", "libx264", "-crf", "19",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(output.with_suffix(".mp4"))], check=True)
    subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "warning", "-y", "-i", str(output.with_suffix(".mp4")),
        "-filter_complex", "fps=15,scale=1120:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse=dither=bayer",
        "-loop", "0", str(output.with_suffix(".gif"))], check=True)
    print(f"Wrote ramp comparison: {duration:.2f}s; MP4 and GIF")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    encode(args.root, args.output)
