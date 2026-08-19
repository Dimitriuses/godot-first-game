"""Drive the whole die pipeline: Blender render, composite, pack, scene, validate.

One clip at a time, so a run can be interrupted and resumed and so the sub-frames
never all exist at once -- a d20's are about 2.2 GB in one go, and roughly a
twentieth of that clip by clip.

    python tools/dice-render/pipeline.py d20                  # dry run: the plan
    python tools/dice-render/pipeline.py d20 --run            # do it
    python tools/dice-render/pipeline.py d20 --run --only 3,idle0
    python tools/dice-render/pipeline.py d20 --run --red 20   # tint one numeral
    python tools/dice-render/pipeline.py d20 --status         # what is on disk

Nothing is written into the repository until `--install`, which copies the finished
sheets into assets/dice/, and `--scene`, which regenerates the die's .tscn.

Clips are named as the game names them -- `3`, `idle0` -- not as render.py's
directories (`face3`).
"""
import argparse, json, os, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dice_config

ROOT = dice_config.ROOT
BLEND = os.path.join(ROOT, "assets", "Dice D20 D12 D8 D10 D8 D6 D4",
                     "Dices blendswap.blend")
BLENDER_CANDIDATES = [
    os.environ.get("BLENDER", ""),
    r"D:\Programs\Blender 4.4.3\blender.exe",
    r"C:\Program Files\Blender Foundation\Blender 4.4\blender.exe",
    "blender",
]


def find_blender(explicit=None):
    for c in [explicit] + BLENDER_CANDIDATES:
        if c and (os.path.exists(c) or shutil.which(c)):
            return c
    sys.exit("could not find Blender; pass --blender <path> or set $BLENDER")


def clips(cfg):
    """(clip name, render.py's directory name, frame count) for every animation."""
    out = [(str(n), "face%d" % n, cfg["roll_frames"])
           for n in range(1, cfg["faces"] + 1)]
    out += [(idle, idle, cfg["idle_frames"]) for idle in cfg["idles"]]
    return out


def status(cfg, work):
    """How far each clip has got: rendered sub-frames, composited frames, a sheet."""
    rows = []
    for name, tag, nframes in clips(cfg):
        sub = os.path.join(work, "faces", tag)
        meta = os.path.join(sub, "meta.json")
        nsub = (sum(f["nsub"] for f in json.load(open(meta))["frames"])
                if os.path.exists(meta) else 0)
        frames = os.path.join(work, "frames", tag)
        nfr = (len([f for f in os.listdir(frames) if f.endswith(".png")])
               if os.path.isdir(frames) else 0)
        sheet = os.path.basename(dice_config.sheet_path(cfg, name))
        rows.append(dict(clip=name, tag=tag, frames=nframes, subframes=nsub,
                         composited=nfr, complete=nfr == nframes, sheet=sheet,
                         packed=os.path.exists(os.path.join(work, "sheets", sheet)),
                         installed=os.path.exists(
                             os.path.join(ROOT, dice_config.sheets_dir(cfg), sheet))))
    return rows


def print_status(cfg, work):
    # "sheet in build" is usually "-" after a real run: --install writes the sheet
    # straight into assets/ rather than leaving a copy behind. "installed" is the
    # column that says whether the game has it.
    print("%-7s %-7s %9s %11s %14s %9s"
          % ("clip", "dir", "subframes", "composited", "sheet in build", "installed"))
    for r in status(cfg, work):
        print("%-7s %-7s %9s %11s %14s %9s"
              % (r["clip"], r["tag"], r["subframes"] or "-",
                 "%d/%d" % (r["composited"], r["frames"]),
                 "yes" if r["packed"] else "-", "yes" if r["installed"] else "-"))


def collider(cfg):
    """Measure the resting sprite and report the collider that fits it.

    The circle is an approximation of a silhouette that is never circular, so the rule
    is the mean of the half-width and half-height plus the pixel and a half of margin
    the d6 has always carried. That rule reproduces the d6's and the d20's shipped
    numbers, which is the only reason to trust it on a new die.
    """
    from PIL import Image
    import numpy as np
    rows = []
    cell, cols, last = cfg["cell"], cfg["cols"], cfg["roll_frames"] - 1
    for v in range(1, cfg["faces"] + 1):
        path = os.path.join(ROOT, dice_config.sheet_path(cfg, str(v)).replace("/", os.sep))
        if not os.path.exists(path):
            return None
        im = np.asarray(Image.open(path).convert("RGBA"), np.uint8)
        r, c = divmod(last, cols)
        a = im[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell, 3] > 24
        ys, xs = np.where(a)
        rows.append((xs.min(), xs.max(), ys.min(), ys.max()))
    w = sum(r[1] - r[0] + 1 for r in rows) / len(rows)
    h = sum(r[3] - r[2] + 1 for r in rows) / len(rows)
    cx = sum((r[0] + r[1]) / 2 for r in rows) / len(rows) - (cell - 1) / 2
    cy = sum((r[2] + r[3]) / 2 for r in rows) / len(rows) - (cell - 1) / 2
    return dict(width=w, height=h, radius=round((w + h) / 4 + 1.5),
                offset=(round(cx), round(cy) - 2))


def run(cmd, env=None):
    e = os.environ.copy()
    e.update(env or {})
    p = subprocess.run(cmd, cwd=ROOT, env=e)
    if p.returncode:
        sys.exit("failed (%d): %s" % (p.returncode, " ".join(map(str, cmd))))


def render_clip(blender, die, tag, red):
    """One Blender invocation for one clip.

    Blender prints two lines per saved image, which is 15,000 lines for a d20 and
    drowns everything useful, so its output is captured and only render.py's own
    progress lines survive. A failure prints the lot.
    """
    cmd = [blender, BLEND, "--background", "--python", os.path.join(HERE, "render.py")]
    env = os.environ.copy()
    env.update(DICE_DIE=die, DICE_ONLY=tag)
    if red is not None:
        env["DICE_RED"] = str(red)
    p = subprocess.run(cmd, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                       stderr=subprocess.STDOUT, text=True, errors="replace")
    if p.returncode:
        print(p.stdout)
        sys.exit("Blender failed rendering %s" % tag)
    for line in p.stdout.splitlines():
        if line.startswith("  ") and "sub-frames so far" in line:
            print(line, flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("die", nargs="?", default="d6")
    ap.add_argument("--run", action="store_true", help="actually render")
    ap.add_argument("--status", action="store_true", help="report and stop")
    ap.add_argument("--collider", action="store_true",
                    help="measure the installed sheets and report the collider that fits")
    ap.add_argument("--only", default="", help="comma separated clip names")
    ap.add_argument("--red", help="face value to tint red, or 'none'")
    ap.add_argument("--keep", action="store_true",
                    help="keep sub-frames instead of pruning as we go")
    ap.add_argument("--resume", action="store_true",
                    help="skip clips already composited")
    ap.add_argument("--install", action="store_true",
                    help="copy the finished sheets into assets/")
    ap.add_argument("--scene", action="store_true",
                    help="regenerate the die's .tscn (implies --install)")
    ap.add_argument("--blender")
    a = ap.parse_args()

    cfg = dice_config.die(a.die)
    work = dice_config.work_dir(a.die)

    if a.collider:
        m = collider(cfg)
        if m is None:
            sys.exit("%s has no installed sheets to measure" % a.die)
        print("%s resting sprite %.0fx%.0f px" % (a.die, m["width"], m["height"]))
        print("  collider_radius=%.1f  (config has %.1f)" % (m["radius"], cfg["collider_radius"]))
        print("  collider_offset=%s  (config has %s)" % (m["offset"], cfg["collider_offset"]))
        return

    if a.status:
        print("%s -> %s" % (a.die, work))
        print_status(cfg, work)
        return

    wanted = [c.strip() for c in a.only.split(",") if c.strip()]
    todo = [c for c in clips(cfg) if not wanted or c[0] in wanted]
    if wanted and len(todo) != len(wanted):
        sys.exit("%s has no clip named %s"
                 % (a.die, ", ".join(sorted(set(wanted) - {c[0] for c in todo}))))
    if a.resume:
        done = {r["clip"] for r in status(cfg, work) if r["complete"]}
        todo = [c for c in todo if c[0] not in done]

    red = (dice_config.red_for(cfg) if a.red is None else
           None if a.red.lower() in ("none", "off") else int(a.red))
    print("die        %s -- %s, %d faces" % (a.die, cfg["label"], cfg["faces"]))
    print("clips      %d: %s" % (len(todo), ", ".join(c[0] for c in todo) or "none"))
    print("frames     %d" % sum(c[2] for c in todo))
    print("red glyph  %s" % (red if red is not None else "none"))
    print("work       %s" % work)
    print("sub-frames %s" % ("kept" if a.keep else "pruned after compositing"))
    print("install    %s" % ("yes -> %s/" % dice_config.sheets_dir(cfg)
                             if a.install or a.scene else "no"))
    print("scene      %s" % (cfg["scene"] if a.scene else "not regenerated"))
    if not a.run:
        print("\ndry run -- add --run to start")
        return

    blender = find_blender(a.blender)
    print("blender    %s\n" % blender)
    t0 = time.time()
    for i, (name, tag, _nframes) in enumerate(todo, 1):
        print("[%d/%d] %s" % (i, len(todo), name), flush=True)
        render_clip(blender, a.die, tag, red)
        cmd = [sys.executable, os.path.join(HERE, "composite.py"), "--die", a.die]
        if not a.keep:
            cmd.append("--prune")
        run(cmd + [tag])
    if todo:
        print("\n%d clips in %.1f min" % (len(todo), (time.time() - t0) / 60))

    # Pack only what was asked for. A partial run has no frames for the other clips,
    # and packing them all would just fail on the first one missing.
    pack = [sys.executable, os.path.join(HERE, "pack.py"), "--die", a.die]
    if a.install or a.scene:
        pack.append("--install")
    run(pack + ([c[1] for c in todo] if wanted else []))

    if a.scene:
        run([sys.executable, os.path.join(HERE, "make_scene.py"), a.die, "--write"])
        run([sys.executable, os.path.join(HERE, "validate.py"), a.die])


if __name__ == "__main__":
    main()
