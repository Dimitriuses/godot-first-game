"""Rebuild a die's roll clips with fewer frames, by dropping some.

    python tools/clip-lab/decimate.py d6 --frames 61

Writes new sheets into tools/clip-lab/out/decimated/<die>/ and reports what they really
cost, rather than what a model says they would. Nothing under assets/ is touched.

This is a *resample*, not a re-render: it selects frames that already exist. That is what
makes it worth trying first — it needs no Blender, no source model, and no change to how
the animation is drawn. The last frame is always kept, because that is the resting pose
`Dice.ShowResting` parks on and `PlaceOnFace` addresses.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

CELL = 128
COLS = 10           # dice_config's `cols`, shared by a die's roll and idle sheets
ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
OUT = os.path.join(os.path.dirname(__file__), "out", "decimated")


def sheet_path(die, clip):
    return os.path.normpath(os.path.join(ROOT, "assets", "dice", die,
                                         f"{clip}_sprites.png"))


def read_frames(path, count):
    image = Image.open(path).convert("RGBA")
    cols = image.width // CELL
    return [image.crop(((i % cols) * CELL, (i // cols) * CELL,
                        (i % cols) * CELL + CELL, (i // cols) * CELL + CELL))
            for i in range(count)]


def pick(total, keep, plan="eased", power=1.3):
    """Which frames to keep.

    What a viewer sees each tick is the *step* — how much the picture changed since the
    frame before. Dropping frames makes steps bigger, and where that matters is not
    uniform: during the blur every frame is a smear and a large step is invisible, while
    during the settle the die is sharp and the same step reads as a stutter.

    Measured on the d6, mean step per region, 91 frames against 61:

        region      as rendered   evenly spaced   eased
        blur              8.18          9.35      10.07
        settle            1.05          1.70       1.38

    So `eased` spends its frames towards the end, where they are seen, and takes the
    saving out of the blur, where they are not. It beats even spacing on the settle by
    about a fifth for the same budget, and its worst single step lands at frame 24 —
    deep in the smear.

    An abrupt change of sampling rate is worse than either: a sparse first half joined
    to a dense second half puts the largest step exactly at the boundary, which is where
    the die is starting to become readable. Hence a curve rather than two rates.
    """
    if plan == "uniform":
        return sorted(set(int(round(x)) for x in np.linspace(0, total - 1, keep)))
    t = np.linspace(0.0, 1.0, keep)
    return sorted(set(int(round((total - 1) * (1 - (1 - x) ** power))) for x in t))


def write_sheet(frames, path, cols=COLS):
    """One sheet, laid out in the column count the render pipeline uses.

    It has to be `cols`, not something square: `dice_config` carries a single `cols` for
    every animation of a die, so a roll sheet in a different grid from the idle sheets
    beside it makes `make_scene.py` refuse the whole die. Square sheets were free while
    this only wrote into `out/` as a prototype; they stopped being free the moment the
    output became installable.
    """
    rows = int(np.ceil(len(frames) / cols))
    out = Image.new("RGBA", (cols * CELL, rows * CELL), (0, 0, 0, 0))
    for i, frame in enumerate(frames):
        out.paste(frame, ((i % cols) * CELL, (i // cols) * CELL))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path, optimize=True)
    return os.path.getsize(path)


def faces_of(die):
    folder = os.path.dirname(sheet_path(die, 1))
    found = []
    for name in os.listdir(folder):
        head = name.split("_")[0]
        if name.endswith("_sprites.png") and head.isdigit():
            found.append(int(head))
    return sorted(found)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("die", nargs="?", default="d6")
    ap.add_argument("--frames", type=int, default=61)
    ap.add_argument("--source-frames", type=int, default=91)
    ap.add_argument("--plan", choices=["eased", "uniform"], default="eased")
    ap.add_argument("--cols", type=int, default=COLS,
                    help="cells per sheet row; must match dice_config's cols")
    ap.add_argument("--install", action="store_true",
                    help="write into assets/dice/<die>/ instead of out/decimated/<die>/")
    args = ap.parse_args()

    # Installing replaces the sheets a committed scene addresses, and the scene's atlas
    # regions still describe the old grid: the die is broken until make_scene.py is re-run.
    target = (os.path.join(ROOT, "assets", "dice", args.die) if args.install
              else os.path.join(OUT, args.die))

    keep = pick(args.source_frames, args.frames, args.plan)
    print(f"{args.die}: {args.source_frames} -> {len(keep)} frames per clip "
          f"({args.plan})")
    print(f"keeping {keep[:6]} ... {keep[-3:]}")
    print(f"dropping {args.source_frames - len(keep)} of every {args.source_frames}\n")

    before = after = 0
    print(f"{'clip':>6}{'before KB':>12}{'after KB':>11}{'saved':>8}")
    for face in faces_of(args.die):
        src = sheet_path(args.die, face)
        frames = read_frames(src, args.source_frames)
        # Before the write, not after: --install overwrites the source, so measuring it
        # afterwards compares the new sheet with itself and reports a saving of nothing.
        was = os.path.getsize(src)
        size = write_sheet([frames[i] for i in keep],
                           os.path.join(target, f"{face}_sprites.png"), args.cols)
        before += was
        after += size
        print(f"{face:>6}{was / 1024:>12.0f}{size / 1024:>11.0f}"
              f"{100 * (1 - size / was):>7.0f}%")
    print(f"{'all':>6}{before / 1024:>12.0f}{after / 1024:>11.0f}"
          f"{100 * (1 - after / before):>7.0f}%")

    print(f"\nAt 30 fps a {args.frames}-frame clip lasts "
          f"{args.frames / 30:.2f}s against {args.source_frames / 30:.2f}s.")
    print("Either the throw gets shorter, or the clip plays slower and each step gets")
    print("bigger. The prototype scene shows both.")
    print(f"\nwritten to {os.path.normpath(target)}")
    if args.install:
        print(f"now set roll_frames={len(keep)} in dice_config.py, then run "
              f"`python tools/dice-render/make_scene.py {args.die} --write`")
    return 0


if __name__ == "__main__":
    sys.exit(main())
