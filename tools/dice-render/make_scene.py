"""Generate a die's `.tscn` from its spritesheets.

`dice.tscn` carries one `AtlasTexture` sub-resource per animation frame — 606 for the
d6, about 2,000 for a d20. That is not a file anyone edits by hand, so it is generated
from `dice_config.py` plus whatever sheets are on disk.

    python tools/dice-render/make_scene.py d6            # compare against the committed scene
    python tools/dice-render/make_scene.py d6 --write    # write it
    python tools/dice-render/make_scene.py d20 --write

The default action is a comparison, not a write: the generator's whole claim to being
correct is that it reproduces the hand-maintained d6 scene, so that check is the thing
you want to run most often.
"""
import argparse, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dice_config as cfgmod
from dice_config import ROOT


def sheet_grid(cfg, animation, frames):
    """Cell grid for one sheet, verified against the PNG actually on disk."""
    from PIL import Image
    rel = cfgmod.sheet_path(cfg, animation)
    full = os.path.join(ROOT, rel.replace("/", os.sep))
    if not os.path.exists(full):
        raise SystemExit("missing sheet: %s" % rel)
    w, h = Image.open(full).size
    cell, cols = cfg["cell"], cfg["cols"]
    rows = (frames + cols - 1) // cols
    if (w, h) != (cols * cell, rows * cell):
        raise SystemExit("%s is %dx%d; %d frames of %dpx in %d columns needs %dx%d"
                         % (rel, w, h, frames, cell, cols, cols * cell, rows * cell))
    return rel


def generate(cfg):
    """The whole scene as text."""
    anims = cfgmod.animations(cfg)
    cell, cols = cfg["cell"], cfg["cols"]

    ext, sub, body = [], [], []
    script_uid = cfgmod.resource_uid(cfg["script"])
    ext.append(('[ext_resource type="Script"%s path="%s" id="script_0"]'
                % (' uid="%s"' % script_uid if script_uid else "", cfg["script"])))

    atlas_ids = {}
    for animation, frames, _loop in anims:
        rel = sheet_grid(cfg, animation, frames)
        tex_id = "tex_%s" % animation
        uid = cfgmod.resource_uid(rel)
        ext.append('[ext_resource type="Texture2D"%s path="res://%s" id="%s"]'
                   % (' uid="%s"' % uid if uid else "", rel, tex_id))
        ids = []
        for f in range(frames):
            r, c = divmod(f, cols)
            sid = "AtlasTexture_%s_%03d" % (animation, f)
            sub.append('[sub_resource type="AtlasTexture" id="%s"]\n'
                       'atlas = ExtResource("%s")\n'
                       'region = Rect2(%d, %d, %d, %d)'
                       % (sid, tex_id, c * cell, r * cell, cell, cell))
            ids.append(sid)
        atlas_ids[animation] = ids

    sub.insert(0, '[sub_resource type="PhysicsMaterial" id="PhysicsMaterial_0"]\n'
                  'bounce = %s\nabsorbent = %s'
                  % (_num(cfg["bounce"]), "true" if cfg["absorbent"] else "false"))

    blocks = []
    for animation, frames, loop in anims:
        frame_entries = ",\n".join(
            '{\n"duration": 1.0,\n"texture": SubResource("%s")\n}' % sid
            for sid in atlas_ids[animation])
        blocks.append('{\n"frames": [%s],\n"loop": %s,\n"name": &"%s",\n"speed": %s\n}'
                      % (frame_entries, "true" if loop else "false", animation,
                         _num(cfg["fps"])))
    sub.append('[sub_resource type="SpriteFrames" id="SpriteFrames_0"]\n'
               'animations = [%s]' % ", ".join(blocks))
    sub.append('[sub_resource type="CircleShape2D" id="CircleShape2D_0"]\n'
               'radius = %s' % _num(cfg["collider_radius"]))

    ox, oy = cfg["collider_offset"]
    body.append(
        '[node name="Dice" type="RigidBody2D" '
        'node_paths=PackedStringArray("AnimatedSprite", "CollisionShape")]\n'
        + ("input_pickable = true\n" if cfg["input_pickable"] else "")
        + 'physics_material_override = SubResource("PhysicsMaterial_0")\n'
          'gravity_scale = %s\n'
          'center_of_mass_mode = %d\n'
          'freeze_mode = %d\n'
          'continuous_cd = %d\n'
          'linear_damp = %s\n'
          'script = ExtResource("script_0")\n'
          'AnimatedSprite = NodePath("AnimatedSprite2D")\n'
          'CollisionShape = NodePath("CollisionShape2D")'
        % (_num(cfg["gravity_scale"]), cfg["center_of_mass_mode"], cfg["freeze_mode"],
           cfg["continuous_cd"], _num(cfg["linear_damp"]))
        # Written only when it differs from what the palette works out on its own, so
        # adding it changes no scene that did not need it. Two dice of the same face
        # count would otherwise both be offered as "D6".
        + ('\nDieLabel = "%s"' % cfg["label"]
           if cfg["label"] != "d%d" % cfg["faces"] else ""))
    body.append('[node name="AnimatedSprite2D" type="AnimatedSprite2D" parent="."]\n'
                'sprite_frames = SubResource("SpriteFrames_0")\n'
                'animation = &"1"\n'
                'frame = %d\n'
                'frame_progress = 1.0' % (cfg["roll_frames"] - 1))
    body.append('[node name="CollisionShape2D" type="CollisionShape2D" parent="."]\n'
                'visible = false\n'
                'position = Vector2(%s, %s)\n'
                'shape = SubResource("CircleShape2D_0")\n'
                'one_way_collision_margin = 0.0' % (_num(ox), _num(oy)))

    header = '[gd_scene load_steps=%d format=3%s]' % (
        len(ext) + len(sub) + 1,
        ' uid="%s"' % cfg["scene_uid"] if cfg.get("scene_uid") else "")
    return "\n".join([header, "", "\n".join(ext), "", "\n\n".join(sub), "",
                      "\n\n".join(body), ""])


def _num(v):
    """Godot writes 0.0 and 32.0 rather than 0 and 32."""
    if isinstance(v, float) or isinstance(v, int):
        f = float(v)
        return str(int(f)) if f == int(f) and isinstance(v, int) else repr(f)
    return str(v)


# ---------------------------------------------------------------- comparison

def describe(text):
    """Reduce a scene to what it actually means, so two spellings compare equal."""
    ext = {i: p for p, i in re.findall(
        r'\[ext_resource type="\w+"(?: uid="[^"]*")? path="([^"]+)" id="([^"]+)"\]', text)}
    atlas = {}
    for m in re.finditer(r'\[sub_resource type="AtlasTexture" id="([^"]+)"\]\s*\n'
                         r'atlas = ExtResource\("([^"]+)"\)\s*\n'
                         r'region = Rect2\((\d+), (\d+), (\d+), (\d+)\)', text):
        atlas[m.group(1)] = (ext[m.group(2)],) + tuple(int(v) for v in m.groups()[2:])
    anims = {}
    for body, loop, name, speed in re.findall(
            r'"frames": \[(.*?)\],\s*"loop": (\w+),\s*"name": &"([^"]+)",\s*"speed": ([\d.]+)',
            text, re.S):
        anims[name] = dict(
            loop=loop, speed=float(speed),
            frames=[atlas[s] for s in re.findall(r'SubResource\("([^"]+)"\)', body)])
    # Everything else declared as a sub-resource: PhysicsMaterial, CircleShape2D, and
    # anything added later. Without this a wrong collider radius compares equal.
    others = {}
    for m in re.finditer(r'\[sub_resource type="(\w+)" id="[^"]+"\]\n'
                         r'((?:[^\[\n][^\n]*\n?)*)', text):
        if m.group(1) in ("AtlasTexture", "SpriteFrames"):
            continue        # compared field by field above
        others[m.group(1)] = sorted(
            l.strip() for l in m.group(2).strip().splitlines() if " = " in l)

    nodes = {}
    for m in re.finditer(r'\[node name="([^"]+)"([^\]]*)\]\n((?:[^\[\n][^\n]*\n?)*)', text):
        props = {"__header__": m.group(2).strip()}
        for line in m.group(3).strip().splitlines():
            if " = " in line:
                k, v = line.split(" = ", 1)
                props[k.strip()] = re.sub(r'(SubResource|ExtResource)\("[^"]+"\)', r'\1(*)',
                                          v.strip())
        nodes[m.group(1)] = props
    return dict(textures=set(ext.values()), anims=anims, nodes=nodes, others=others)


def compare(generated, existing):
    a, b = describe(generated), describe(existing)
    problems = []
    if a["textures"] != b["textures"]:
        problems.append("textures differ: only generated %s / only existing %s"
                        % (sorted(a["textures"] - b["textures"]),
                           sorted(b["textures"] - a["textures"])))
    if set(a["anims"]) != set(b["anims"]):
        problems.append("animation names differ: %s vs %s"
                        % (sorted(a["anims"]), sorted(b["anims"])))
    for name in sorted(set(a["anims"]) & set(b["anims"])):
        x, y = a["anims"][name], b["anims"][name]
        if x["loop"] != y["loop"] or x["speed"] != y["speed"]:
            problems.append("%s: loop/speed %s,%s vs %s,%s"
                            % (name, x["loop"], x["speed"], y["loop"], y["speed"]))
        if len(x["frames"]) != len(y["frames"]):
            problems.append("%s: %d frames vs %d"
                            % (name, len(x["frames"]), len(y["frames"])))
        elif x["frames"] != y["frames"]:
            bad = [i for i, (p, q) in enumerate(zip(x["frames"], y["frames"])) if p != q]
            problems.append("%s: %d frames point somewhere else, first at %d (%s vs %s)"
                            % (name, len(bad), bad[0], x["frames"][bad[0]],
                               y["frames"][bad[0]]))
    for kind in sorted(set(a["others"]) | set(b["others"])):
        if a["others"].get(kind) != b["others"].get(kind):
            problems.append("sub-resource %s: %s vs %s"
                            % (kind, a["others"].get(kind), b["others"].get(kind)))
    for node in sorted(set(a["nodes"]) | set(b["nodes"])):
        x, y = a["nodes"].get(node), b["nodes"].get(node)
        if x is None or y is None:
            problems.append("node %s only in %s" % (node, "generated" if y is None else "existing"))
            continue
        for key in sorted(set(x) | set(y)):
            if x.get(key) != y.get(key):
                problems.append("node %s: %s = %s vs %s" % (node, key, x.get(key), y.get(key)))
    return problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("die", help="key in dice_config.DICE, e.g. d6")
    ap.add_argument("--write", action="store_true", help="write the scene instead of comparing")
    a = ap.parse_args()

    cfg = cfgmod.die(a.die)
    text = generate(cfg)
    dest = os.path.join(ROOT, cfg["scene"].replace("/", os.sep))
    anims = cfgmod.animations(cfg)
    total = sum(f for _, f, _ in anims)
    print("%s: %d animations, %d frames, %d atlas regions"
          % (cfg["label"], len(anims), total, total))

    if a.write:
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        open(dest, "w", encoding="utf-8", newline="\n").write(text)
        print("written: %s (%d lines)" % (cfg["scene"], text.count("\n") + 1))
        return 0

    if not os.path.exists(dest):
        print("no scene at %s yet; run with --write" % cfg["scene"])
        return 0
    problems = compare(text, open(dest, encoding="utf-8").read())
    if problems:
        print("DIFFERS from %s:" % cfg["scene"])
        for p in problems[:25]:
            print("  " + p)
        if len(problems) > 25:
            print("  ... and %d more" % (len(problems) - 25))
        return 1
    print("matches %s exactly (same textures, animations, regions and node properties)"
          % cfg["scene"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
