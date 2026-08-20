"""Composite the crisp sub-frames from render.py into finished 128px frames.

Per sub-frame: draw the ground shadow, dilate the die's alpha into a black
outline, lay the die on top. Averaging those composites *is* the motion blur, so
the outline and shadow smear with the die instead of being pasted onto an
already-blurred image. Compositing is done premultiplied throughout; only the
final save converts back to straight alpha.

    python tools/dice-render/composite.py                  # every clip of the d6
    python tools/dice-render/composite.py --die d20        # every clip of the d20
    python tools/dice-render/composite.py --die d20 face3  # just one
    python tools/dice-render/composite.py --die d20 --prune

`--prune` deletes each clip's sub-frames once it has been composited. A d20's
sub-frames are about 2.2 GB if they all have to exist at once; rendering and
compositing a clip at a time with --prune keeps the peak at roughly a twentieth of
that, at the cost of not being able to re-composite without re-rendering.
"""
import json, os, shutil, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dice_config

CELL          = 128
OUTLINE_RGB   = np.array([0x12, 0x12, 0x12], np.float32) / 255.0
OUTLINE_PX    = 9.0      # gaussian sigma at 512 -> ~2px of outline at 128
SHADOW_RGB    = np.array([0x1A, 0x1A, 0x22], np.float32) / 255.0
SHADOW_GAIN   = 1.25
SHADOW_SQUASH = 0.515    # sin(31 deg): a ground circle seen from the camera
SHADOW_BLUR   = 7.0


def tags(cfg):
    """Every clip of a die, as the directory names render.py writes."""
    return (["face%d" % n for n in range(1, cfg["faces"] + 1)] + list(cfg["idles"]))


def dilate(alpha, px):
    """Round outward dilation that keeps its antialiasing."""
    img = Image.fromarray((alpha * 255).astype(np.uint8))
    b = np.asarray(img.filter(ImageFilter.GaussianBlur(px)), np.float32) / 255.0
    return np.maximum(alpha, np.clip((b - 0.13) / 0.10, 0.0, 1.0))


def shadow_layer(sp, res):
    img = Image.new("L", (res, res), 0)
    rx = sp["r"] * 0.95 * sp["scale"]
    ry = rx * SHADOW_SQUASH
    ImageDraw.Draw(img).ellipse(
        [sp["x"] - rx, sp["y"] - ry, sp["x"] + rx, sp["y"] + ry], fill=255)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(SHADOW_BLUR)), np.float32) / 255.0
    return np.clip(a * sp["alpha"] * SHADOW_GAIN, 0.0, 1.0)


def over(cf, af, cb, ab):
    """Premultiplied 'source over'."""
    return cf + cb * (1.0 - af[..., None]), af + ab * (1.0 - af)


def process(tag, work, prune=False):
    src = os.path.join(work, "faces", tag)
    meta = json.load(open(os.path.join(src, "meta.json")))
    res = meta["res"]
    dst = os.path.join(work, "frames", tag)
    os.makedirs(dst, exist_ok=True)

    for fr in meta["frames"]:
        f, nsub = fr["f"], fr["nsub"]
        acc_c = np.zeros((res, res, 3), np.float32)
        acc_a = np.zeros((res, res), np.float32)
        for i in range(nsub):
            px = np.asarray(Image.open(os.path.join(src, "f%03d_s%02d.png" % (f, i)))
                            .convert("RGBA"), np.float32) / 255.0
            rgb, a = px[..., :3], px[..., 3]        # Blender writes straight alpha
            die_c = rgb * a[..., None]

            sa = shadow_layer(fr["subs"][i], res)
            oa = dilate(a, OUTLINE_PX)
            c, al = over(OUTLINE_RGB * oa[..., None], oa, SHADOW_RGB * sa[..., None], sa)
            c, al = over(die_c, a, c, al)
            acc_c += c
            acc_a += al
        acc_c /= nsub
        acc_a /= nsub

        k = res // CELL                              # box downsample, premultiplied
        down = lambda x: x.reshape(CELL, k, CELL, k, -1).mean(axis=(1, 3))
        c, al = down(acc_c), down(acc_a[..., None])[..., 0]
        rgb = np.where(al[..., None] > 1e-5, c / np.maximum(al[..., None], 1e-5), 0.0)
        out = np.concatenate([np.clip(rgb, 0, 1), np.clip(al, 0, 1)[..., None]], -1)
        Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGBA").save(
            os.path.join(dst, "f%03d.png" % f))
    if prune:
        shutil.rmtree(src)
    return meta["nframes"]


if __name__ == "__main__":
    argv = sys.argv[1:]
    prune = "--prune" in argv
    argv = [a for a in argv if a != "--prune"]
    name = "d6"
    if "--die" in argv:
        i = argv.index("--die")
        name = argv[i + 1]
        del argv[i:i + 2]
    cfg = dice_config.die(name)
    work = dice_config.work_dir(name)

    for tag in (argv or tags(cfg)):
        print("%s %s: %d frames" % (name, tag, process(tag, work, prune)), flush=True)
    print("-> %s" % os.path.join(work, "frames"))
