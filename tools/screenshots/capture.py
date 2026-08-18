"""Regenerate the README images.

Runs the Godot capture scene, assembles its PNG sequence into docs/roll.gif, and
copies docs/screenshot.png into place.

    python tools/screenshots/capture.py                    # find Godot automatically
    python tools/screenshots/capture.py --godot <path>     # or point at it
    python tools/screenshots/capture.py --keep             # leave build/ for inspection
    python tools/screenshots/capture.py --no-run           # re-assemble existing frames

The capture needs a real window: --headless has no renderer, so a Godot window
opens for a few seconds and closes itself.
"""
import argparse, glob, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BUILD = os.path.join(HERE, "build")
DOCS = os.path.join(ROOT, "docs")
SCENE = "res://tools/screenshots/capture.tscn"

# The GIF is cropped to this box out of the 1152x648 capture, so the rolling die
# fills a useful part of the frame instead of being 60px in a wide shot. Written
# out at native resolution: no resampling, so the pixel art stays crisp.
CROP = (279, 80, 919, 440)          # left, top, right, bottom -> 640x360.
# Top edge threads between the Respawn button (ends y=78) and the banner (starts y~84).
FRAME_MS = 33                       # source clip is 30fps
STRIDE = 2                          # every Nth frame, to keep the GIF small
END_HOLD_MS = 900                   # linger on the settled face before looping

GODOT_CANDIDATES = [
    r"D:\Documents\Projects\Godot\Godot_v4.4.1-stable_mono_win64\Godot_v4.4.1-stable_mono_win64_console.exe",
    r"C:\Program Files\Godot\Godot_mono.exe",
    "godot",
]


def find_godot(explicit):
    if explicit:
        return explicit
    for c in GODOT_CANDIDATES:
        if os.path.exists(c) or shutil.which(c):
            return c
    sys.exit("could not find a Godot .NET build; pass --godot <path>")


def run_capture(godot):
    if os.path.isdir(BUILD):
        shutil.rmtree(BUILD)
    print("building the C# assembly...")
    subprocess.run(["dotnet", "build", "FirstGame.csproj", "-v", "q", "--nologo"],
                   cwd=ROOT, check=True)
    print("running the capture scene (a window will open briefly)...")
    r = subprocess.run([godot, "--path", ".", SCENE, "--",
                        "--out=res://tools/screenshots/build"], cwd=ROOT)
    if r.returncode != 0:
        sys.exit("capture failed with exit code %d" % r.returncode)


def build_gif():
    from PIL import Image

    frames = sorted(glob.glob(os.path.join(BUILD, "roll", "f*.png")))
    if not frames:
        sys.exit("no roll frames in %s; run without --no-run first" % BUILD)

    picked = frames[::STRIDE]
    if frames[-1] not in picked:
        picked.append(frames[-1])           # always end on the settled face

    images = [Image.open(p).convert("RGB").crop(CROP) for p in picked]
    # One shared adaptive palette for every frame: without it each frame gets its
    # own and the GIF both dithers inconsistently and compresses badly.
    palette = images[0].quantize(colors=256, method=Image.MEDIANCUT)
    quantized = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in images]

    durations = [FRAME_MS * STRIDE] * len(quantized)
    durations[-1] = END_HOLD_MS

    out = os.path.join(DOCS, "roll.gif")
    quantized[0].save(out, save_all=True, append_images=quantized[1:],
                      duration=durations, loop=0, optimize=True, disposal=1)
    return out, quantized[0].size, len(quantized), os.path.getsize(out)


def copy_still():
    src = os.path.join(BUILD, "screenshot.png")
    if not os.path.exists(src):
        sys.exit("no screenshot.png in %s" % BUILD)
    dst = os.path.join(DOCS, "screenshot.png")
    shutil.copyfile(src, dst)
    from PIL import Image
    return dst, Image.open(dst).size, os.path.getsize(dst)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--godot", help="path to a Godot .NET build")
    ap.add_argument("--no-run", action="store_true", help="reuse the frames already in build/")
    ap.add_argument("--keep", action="store_true", help="do not delete build/ afterwards")
    a = ap.parse_args()

    if not a.no_run:
        run_capture(find_godot(a.godot))

    dst, size, nbytes = copy_still()
    print("%-18s %dx%d  %6.1f KB" % (os.path.relpath(dst, ROOT), size[0], size[1], nbytes / 1024))
    dst, size, n, nbytes = build_gif()
    print("%-18s %dx%d  %6.1f KB  (%d frames)"
          % (os.path.relpath(dst, ROOT), size[0], size[1], nbytes / 1024, n))

    if not a.keep:
        shutil.rmtree(BUILD, ignore_errors=True)
    else:
        print("kept intermediates in", os.path.relpath(BUILD, ROOT))


if __name__ == "__main__":
    main()
