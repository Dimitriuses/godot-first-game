"""Check a die's .tscn against the spritesheets actually on disk.

Every atlas region must fit inside its texture and sit at the grid cell its frame
index implies, each animation must draw from exactly one sheet, and the frame
counts must match what dice_config says. Exits non-zero on any problem.

    python tools/dice-render/validate.py            # the d6
    python tools/dice-render/validate.py d20
"""
import re, os, sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dice_config

ROOT  = dice_config.ROOT
DIE   = sys.argv[1] if len(sys.argv) > 1 else "d6"
CFG   = dice_config.die(DIE)
SCENE = os.path.join(ROOT, CFG["scene"].replace("/", os.sep))
EXPECTED = {name: n for name, n, _loop in dice_config.animations(CFG)}
COLS = CFG["cols"]

s = open(SCENE, encoding="utf-8").read()
ext = {i: p for p, i in re.findall(
    r'\[ext_resource type="Texture2D".*?path="res://([^"]+)" id="([^"]+)"\]', s)}

atlas = {}
for m in re.finditer(r'\[sub_resource type="AtlasTexture" id="([^"]+)"\]\s*\n'
                     r'atlas = ExtResource\("([^"]+)"\)\s*\n'
                     r'region = Rect2\((\d+), (\d+), (\d+), (\d+)\)', s):
    atlas[m.group(1)] = (m.group(2),) + tuple(int(v) for v in m.groups()[2:])

sizes = {}
for rid, path in ext.items():
    p = os.path.join(ROOT, path.replace("/", os.sep))
    sizes[rid] = Image.open(p).size if os.path.exists(p) else None

fail = 0
anims = re.findall(r'"frames": \[(.*?)\],\s*"loop": (\w+),\s*"name": &"([^"]+)",'
                   r'\s*"speed": ([\d.]+)', s, re.S)
print("%-6s %-6s %-6s %-5s %-26s %s" % ("anim", "frames", "loop", "fps", "sheet", "layout"))
for body, loop, name, speed in anims:
    ids = re.findall(r'SubResource\("([^"]+)"\)', body)
    rids = {atlas[i][0] for i in ids}
    if len(rids) != 1:
        print("  !! %s spans %d textures" % (name, len(rids))); fail += 1; continue
    rid = rids.pop()
    if sizes[rid] is None:
        print("  !! %s: missing texture %s" % (name, ext[rid])); fail += 1; continue
    W, H = sizes[rid]
    for k, i in enumerate(ids):
        _, x, y, w, h = atlas[i]
        if x + w > W or y + h > H:
            print("  !! %s frame %d region (%d,%d,%d,%d) outside %dx%d"
                  % (name, k, x, y, w, h, W, H)); fail += 1
        if (x // w, y // h) != (k % COLS, k // COLS):
            print("  !! %s frame %d at grid (%d,%d), expected (%d,%d)"
                  % (name, k, x // w, y // h, k % COLS, k // COLS)); fail += 1
    cell = atlas[ids[0]][3]
    print("%-6s %-6d %-6s %-5s %-26s %dpx cells in %dx%d"
          % (name, len(ids), loop, speed, os.path.basename(ext[rid]), cell, W, H))

got = {a[2]: len(re.findall(r'SubResource', a[0])) for a in anims}
if got != EXPECTED:
    print("  !! frame counts changed: %s" % got); fail += 1

node = re.search(r'\[node name="AnimatedSprite2D".*?\n(.*?)\n\n', s, re.S).group(1)
if "scale" in node:
    print("  !! AnimatedSprite2D still has a scale override:\n    %s"
          % node.replace("\n", "\n    ")); fail += 1

print("\n%s" % ("FAILED: %d problem(s)" % fail if fail else
                "OK: %s is consistent with the sheets on disk"
                % os.path.basename(SCENE)))
sys.exit(1 if fail else 0)
