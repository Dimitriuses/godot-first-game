"""Pack finished frames into the spritesheets a die's scene expects:
`cols` columns, row-major, one `cell`px cell per frame -- both from dice_config.

    python tools/dice-render/pack.py                        # d6, into $work/sheets
    python tools/dice-render/pack.py --die d20 --install    # d20, into assets/dice/
    python tools/dice-render/pack.py --die d20 face3 idle0  # just those two sheets
"""
import os, sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dice_config

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))


def jobs(cfg):
    """(source clip directory, frame count, sheet filename) for each animation.

    render.py writes the numbered clips as `face3`, but the sheet and the SpriteFrames
    animation are both called `3`, so the two names are not interchangeable.
    """
    out = [("face%d" % n, cfg["roll_frames"],
            os.path.basename(dice_config.sheet_path(cfg, str(n))))
           for n in range(1, cfg["faces"] + 1)]
    out += [(idle, cfg["idle_frames"],
             os.path.basename(dice_config.sheet_path(cfg, idle)))
            for idle in cfg["idles"]]
    return out


def pack(tag, nframes, outname, frames_dir, outdir, cell, cols):
    rows = (nframes + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * cell), (0, 0, 0, 0))
    for f in range(nframes):
        p = os.path.join(frames_dir, tag, "f%03d.png" % f)
        im = Image.open(p).convert("RGBA")
        if im.size != (cell, cell):
            raise SystemExit("%s is %s, expected %dx%d" % (p, im.size, cell, cell))
        r, c = divmod(f, cols)
        sheet.paste(im, (c * cell, r * cell))
    os.makedirs(outdir, exist_ok=True)
    dst = os.path.join(outdir, outname)
    sheet.save(dst, optimize=True)
    return dst, sheet.size, os.path.getsize(dst)


if __name__ == "__main__":
    argv = sys.argv[1:]
    install = "--install" in argv
    argv = [a for a in argv if a != "--install"]
    name = "d6"
    if "--die" in argv:
        i = argv.index("--die")
        name = argv[i + 1]
        del argv[i:i + 2]
    cfg = dice_config.die(name)
    work = dice_config.work_dir(name)
    frames_dir = os.path.join(work, "frames")
    outdir = (os.path.join(ROOT, dice_config.sheets_dir(cfg).replace("/", os.sep))
              if install
              else os.path.join(work, "sheets"))

    total = 0
    for tag, n, sheet_name in jobs(cfg):
        if argv and tag not in argv:
            continue
        dst, size, nbytes = pack(tag, n, sheet_name, frames_dir, outdir,
                                 cfg["cell"], cfg["cols"])
        total += nbytes
        print("%-24s %dx%d  %6.1f KB" % (sheet_name, size[0], size[1], nbytes / 1024),
              flush=True)
    print("total: %.2f MB -> %s" % (total / 1024 / 1024, outdir))
