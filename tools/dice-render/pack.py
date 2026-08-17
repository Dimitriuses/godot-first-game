"""Pack finished frames into the spritesheets scenes/dice.tscn expects:
10 columns, row-major, one 128px cell per frame.

    python tools/dice-render/pack.py            # write into $DICE_WORK/sheets
    python tools/dice-render/pack.py --install  # write straight into assets/dice/
"""
import os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.environ.get("DICE_WORK") or os.path.join(HERE, "build")
FRAMES = os.path.join(WORK, "frames")
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "assets", "dice"))
CELL, COLS = 128, 10

JOBS = [("face%d" % n, 91, "dice_%d_sprites.png" % n) for n in range(1, 7)] + \
       [("idle0", 30, "dice_idle0_sprites.png"), ("idle1", 30, "dice_idle1_sprites.png")]


def pack(tag, nframes, outname, outdir):
    rows = (nframes + COLS - 1) // COLS
    sheet = Image.new("RGBA", (COLS * CELL, rows * CELL), (0, 0, 0, 0))
    for f in range(nframes):
        p = os.path.join(FRAMES, tag, "f%03d.png" % f)
        im = Image.open(p).convert("RGBA")
        if im.size != (CELL, CELL):
            raise SystemExit("%s is %s, expected %dx%d" % (p, im.size, CELL, CELL))
        r, c = divmod(f, COLS)
        sheet.paste(im, (c * CELL, r * CELL))
    os.makedirs(outdir, exist_ok=True)
    dst = os.path.join(outdir, outname)
    sheet.save(dst, optimize=True)
    return dst, sheet.size, os.path.getsize(dst)


if __name__ == "__main__":
    outdir = ASSETS if "--install" in sys.argv else os.path.join(WORK, "sheets")
    total = 0
    for tag, n, name in JOBS:
        dst, size, nbytes = pack(tag, n, name, outdir)
        total += nbytes
        print("%-24s %dx%d  %6.1f KB" % (name, size[0], size[1], nbytes / 1024))
    print("total: %.2f MB -> %s" % (total / 1024 / 1024, outdir))
