"""Build the palette's icon sheet and the pack manifest.

    python tools/dice-render/make_icons.py --write

Writes two files:

    assets/dice/icons.png   one 64px cell per die, cropped to the die and scaled to fill
    assets/dice/pack.json   the pack in order: scene path, display name, icon cell

Why this exists: the palette used to build its buttons by instantiating every die scene
and taking a frame off each. Loading a die's `PackedScene` loads its whole sheet set —
measured at 180 MB of texture memory for the d20 alone, and 727 MB for the eight of them
— so the palette alone was pulling the entire pack into memory before anything was
thrown. With an icon sheet it needs no die scene at all, and a die's frames load only
when one is actually put on the board.

`pack.json` is the manifest for the whole pack, so adding a die no longer means editing
`game.tscn`: add it to `dice_config.py`, render it, and re-run this.
"""

import argparse
import json
import os
import re
import sys

import numpy as np
from PIL import Image

CELL = 128
ICON = 64
ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
DICE_DIR = os.path.normpath(os.path.join(ROOT, "assets", "dice"))
SCENES = os.path.normpath(os.path.join(ROOT, "scenes"))

# The order the palette shows them in. The d6 keeps the original scene name.
PACK = [
    ("d4", "d4.tscn"),
    ("d6", "dice.tscn"),
    ("d6n", "d6n.tscn"),
    ("d8", "d8.tscn"),
    ("d10", "d10.tscn"),
    ("d10p", "d10p.tscn"),
    ("d12", "d12.tscn"),
    ("d20", "d20.tscn"),
]


def faces_of(die):
    folder = os.path.join(DICE_DIR, die)
    return sorted(int(n.split("_")[0]) for n in os.listdir(folder)
                  if n.endswith("_sprites.png") and n.split("_")[0].isdigit())


def label_of(die, scene_file):
    """The name the die calls itself.

    Read out of the scene rather than duplicated here: `DieLabel` is what `Dice`
    exports, and only the two that share a shape with another die set it. Everything
    else is "d" and its face count, which is the same rule `Dice.DisplayName` uses.
    """
    text = open(os.path.join(SCENES, scene_file), encoding="utf-8").read()
    found = re.search(r'DieLabel = "([^"]*)"', text)
    if found and found.group(1):
        return found.group(1)
    return f"d{len(faces_of(die))}"


def collider_offset(scene_file):
    """The die's CollisionShape2D offset, straight out of its scene.

    The art is drawn centred on the die's origin, but the die is *placed* by subtracting
    this — it is what lines the collider up with the drawing. So where the art ends up
    is the icon offset minus this, and a placeholder that ignores it stands about twelve
    pixels low.
    """
    text = open(os.path.join(SCENES, scene_file), encoding="utf-8").read()
    block = text.split('[node name="CollisionShape2D"', 1)
    if len(block) < 2:
        return 0.0, 0.0
    found = re.search(r"position = Vector2\(([-\d.]+), ([-\d.]+)\)", block[1])
    return (float(found.group(1)), float(found.group(2))) if found else (0.0, 0.0)


def resting_frame(die):
    """The last frame of clip 1 — the die sitting still, which is what an icon wants.

    Frame zero would be the die in mid-air, blurred past recognition.
    """
    path = os.path.join(DICE_DIR, die, "1_sprites.png")
    sheet = Image.open(path).convert("RGBA")
    cols = sheet.width // CELL
    frames = cols * (sheet.height // CELL)
    for i in range(frames - 1, -1, -1):
        cell = sheet.crop(((i % cols) * CELL, (i // cols) * CELL,
                           (i % cols) * CELL + CELL, (i // cols) * CELL + CELL))
        if np.asarray(cell)[:, :, 3].max() > 0:
            return cell
    raise SystemExit(f"{die}: no drawn frame found")


def crop_box(cell):
    """The die's bounding box within its 128px frame, or None."""
    alpha = np.asarray(cell)[:, :, 3]
    rows = np.flatnonzero(alpha.max(axis=1) > 25)
    cols = np.flatnonzero(alpha.max(axis=0) > 25)
    if not len(rows) or not len(cols):
        return None
    return int(cols[0]), int(rows[0]), int(cols[-1]) + 1, int(rows[-1]) + 1


def crop_to_die(cell):
    """The die's own corner of its frame.

    Measured off the alpha rather than fixed, because the dice are not all the same size
    in frame — the d4 is 70px across and the numbered d6 48 — and a fixed window leaves
    the small ones swimming. This is the same rule `DicePalette.CropToDie` applies at
    runtime; doing it here means it does not have to be done there.
    """
    alpha = np.asarray(cell)[:, :, 3]
    rows = np.flatnonzero(alpha.max(axis=1) > 25)
    cols = np.flatnonzero(alpha.max(axis=0) > 25)
    if not len(rows) or not len(cols):
        return cell
    return cell.crop((int(cols[0]), int(rows[0]), int(cols[-1]) + 1, int(rows[-1]) + 1))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    sheet = Image.new("RGBA", (ICON * len(PACK), ICON), (0, 0, 0, 0))
    manifest = []
    print(f"{'die':6}{'faces':>7}{'name':>10}{'cropped':>12}{'offset':>12}")
    for i, (die, scene_file) in enumerate(PACK):
        frame = resting_frame(die)
        box = crop_box(frame)
        # Where the drawn die sits relative to its own origin. The sprite centres the
        # whole 128px cell on the origin, so the art's centre is this far off it. The
        # loading placeholder is positioned by this, and without it the real die would
        # visibly jump when it replaces the icon.
        art = [((box[0] + box[2]) / 2 - CELL / 2),
               ((box[1] + box[3]) / 2 - CELL / 2)] if box else [0.0, 0.0]
        collider = collider_offset(scene_file)
        # Where the art lands relative to the point the player dropped the die on.
        offset = [round(art[0] - collider[0], 1), round(art[1] - collider[1], 1)]
        cropped = crop_to_die(frame)
        # Scale the long side to the cell and centre it, so every die fills its button
        # equally whatever its shape.
        scale = ICON / max(cropped.width, cropped.height)
        size = (max(1, round(cropped.width * scale)),
                max(1, round(cropped.height * scale)))
        cropped = cropped.resize(size, Image.LANCZOS)
        at = (i * ICON + (ICON - size[0]) // 2, (ICON - size[1]) // 2)
        sheet.paste(cropped, at)
        manifest.append({
            "scene": f"res://scenes/{scene_file}",
            "name": label_of(die, scene_file),
            "icon": [i * ICON, 0, ICON, ICON],
            "offset": offset,
            # How much the art was resized to fit its 64px cell. It is *not* 1.0 — the
            # dice are not the same size in frame, so this runs from 0.91 for the d4
            # (70px across) to 1.08 for the numbered d6 (59px). The loading placeholder
            # draws the icon at 1/scale to get back to the die's true size; skip that
            # and every die jumps by up to 9% when the real one replaces it.
            "scale": round(scale, 4),
        })
        print(f"{die:6}{len(faces_of(die)):>7}{manifest[-1]['name']:>10}"
              f"{f'{cropped.width}x{cropped.height}':>12}{str(offset):>12}")

    if not args.write:
        print("\n(dry run — pass --write to save)")
        return 0

    sheet.save(os.path.join(DICE_DIR, "icons.png"), optimize=True)
    with open(os.path.join(DICE_DIR, "pack.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")
    size = os.path.getsize(os.path.join(DICE_DIR, "icons.png"))
    print(f"\nicons.png  {sheet.width}x{sheet.height}  {size / 1024:.1f} KB")
    print(f"pack.json  {len(manifest)} dice")
    return 0


if __name__ == "__main__":
    sys.exit(main())
