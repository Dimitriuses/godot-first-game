"""Render every face of a die flat-on, indexed, so a human can read the numerals once.

Pip counts give a d6 its face-to-value mapping for free. Every other die in the pack is
alphanumeric and you cannot count a glyph, so the mapping has to be read by eye exactly
once and written into `dice_config.py` as `face_values` (ROADMAP 8b).

    DICE_DIE=d20 blender "<the blend>" --background --python tools/dice-render/face_sheet.py
    python tools/dice-render/face_sheet.py --assemble d20

The first command renders one image per face into <work>/facesheet/; the second
tiles them into a single labelled sheet to look at. Face indices are the order
`face_planes()` returns, which is sorted by normal and therefore stable between runs.

Adding `--rest` renders the other sheet you need: each *value* as the game will show
it, through the game camera, with `face_values` and `face_twists` applied. That is the
one that catches a numeral lying on its side, which the flat-on sheet cannot -- it
shows every face the same way up by construction.

    DICE_DIE=d20 blender "<the blend>" --background --python face_sheet.py -- --rest
    python tools/dice-render/face_sheet.py --assemble-rest d20
"""
import os
import sys

CELL = 256          # per-face render, pixels


def render():
    import bpy, math, runpy
    from mathutils import Vector

    here = os.path.dirname(os.path.abspath(__file__))
    G = runpy.run_path(os.path.join(here, "render.py"))
    cfg, work = G["CFG"], G["WORK"]
    out = os.path.join(work, "facesheet")
    os.makedirs(out, exist_ok=True)

    src = bpy.data.objects[cfg["source_object"]]
    nm = G["normalised_mesh"](src)
    G["recentre_on_faces"](nm, cfg["faces"])
    faces = G["face_planes"](nm, cfg["faces"])
    recessed = G["recessed_by_face"](nm, faces)

    # Inked one face at a time, below. Marking every glyph at once puts a readable
    # numeral on each neighbouring face too, and then it is anyone's guess which one
    # the image is actually of.
    for attr in ("DotMask", "RedPip"):
        if attr in nm.color_attributes:
            nm.color_attributes.remove(nm.color_attributes[attr])
    dm = nm.color_attributes.new(name="DotMask", type='FLOAT_COLOR', domain='POINT')
    rp = nm.color_attributes.new(name="RedPip", type='FLOAT_COLOR', domain='POINT')
    for i in range(len(nm.vertices)):
        dm.data[i].color = (0.0, 0.0, 0.0, 1.0)
        rp.data[i].color = (0.0, 0.0, 0.0, 1.0)

    mat, _ramp = G["toon_material"]()
    nm.materials.append(mat)

    scene_name = "FaceSheet"
    if scene_name in bpy.data.scenes:
        bpy.data.scenes.remove(bpy.data.scenes[scene_name])
    sc = bpy.data.scenes.new(scene_name)
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x = sc.render.resolution_y = CELL
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.eevee.taa_render_samples = 32
    sc.view_settings.view_transform = 'Standard'
    sc.world = None

    die = bpy.data.objects.new("FaceSheetDie", nm)
    sc.collection.objects.link(die)
    die.rotation_mode = 'QUATERNION'

    cd = bpy.data.cameras.new("FaceCam")
    cd.type = 'ORTHO'
    # Framed off the die rather than fixed: a tetrahedron is far wider relative to its
    # face planes than a cube is, and 1.15 cropped its corners off.
    cd.ortho_scale = 2.3 * max(v.co.length for v in nm.vertices)
    cam = bpy.data.objects.new("FaceCam", cd)
    sc.collection.objects.link(cam)
    cam.location = (0, 0, 3)        # straight down at the face, no perspective
    cam.rotation_euler = (0, 0, 0)
    sc.camera = cam

    # bpy.ops.render.render() renders the context scene, not the one just built
    if bpy.context.window:
        bpy.context.window.scene = sc

    up = Vector((0, 0, 1))
    previous = set()
    for k, (n, _d, _r, _verts) in enumerate(faces):
        for i in previous:                      # un-ink the last face
            dm.data[i].color = (0.0, 0.0, 0.0, 1.0)
        for i in recessed[k]:                   # ink this one
            dm.data[i].color = (1.0, 1.0, 1.0, 1.0)
        previous = recessed[k]

        die.rotation_quaternion = (
            G["Quaternion"](Vector((1, 0, 0)), math.pi) if n.dot(up) < -0.9999
            else n.rotation_difference(up))
        bpy.context.view_layer.update()
        sc.render.filepath = os.path.join(out, "face_%02d.png" % k)
        bpy.ops.render.render(write_still=True)
    print("FACE SHEET RENDERED: %d faces -> %s" % (len(faces), out))


def render_rest():
    """Every value at its resting pose, through the game camera."""
    import bpy, runpy
    here = os.path.dirname(os.path.abspath(__file__))
    G = runpy.run_path(os.path.join(here, "render.py"))
    cfg, work = G["CFG"], G["WORK"]
    out = os.path.join(work, "restsheet")
    os.makedirs(out, exist_ok=True)

    sc, die, cam, values, _ramp = G["build_scene"]()
    if bpy.context.window:
        bpy.context.window.scene = sc
    sc.render.resolution_x = sc.render.resolution_y = CELL
    rest_z = G["rest_height"]()

    # The game camera is aimed high, so a die dropping in from the top of the frame is
    # fully visible; a die at rest then sits low and a close crop cuts its feet off.
    # Aim at the resting height instead, and frame off the die rather than a constant --
    # the d4 is drawn 1.7x the size of the d6.
    cam.location.z += rest_z - 0.92
    reach = max(v.co.length for v in die.data.vertices) * die.scale.x
    cam.data.ortho_scale = 2.6 * reach

    for v in range(1, cfg["faces"] + 1):
        die.location = (0, 0, rest_z)
        die.rotation_quaternion = G["rest_quat"](values, v)
        bpy.context.view_layer.update()
        sc.render.filepath = os.path.join(out, "rest_%02d.png" % v)
        bpy.ops.render.render(write_still=True)
    print("REST SHEET RENDERED: %d values -> %s" % (cfg["faces"], out))


def render_twists():
    """Every value at every twist: the sheet that tells you what face_twists should be.

    A numbered face has as many ways up as it has corners, and which one reads right is
    not derivable -- it depends on how the glyph was drawn. So render them all once and
    read the answer off, the same bargain as `face_values`.
    """
    import bpy, runpy
    here = os.path.dirname(os.path.abspath(__file__))
    G = runpy.run_path(os.path.join(here, "render.py"))
    cfg, work = G["CFG"], G["WORK"]
    out = os.path.join(work, "twistsheet")
    os.makedirs(out, exist_ok=True)

    sc, die, cam, _values, _ramp = G["build_scene"]()
    if bpy.context.window:
        bpy.context.window.scene = sc
    sc.render.resolution_x = sc.render.resolution_y = CELL
    rest_z = G["rest_height"]()
    cam.location.z += rest_z - 0.92
    cam.data.ortho_scale = 2.6 * max(v.co.length for v in die.data.vertices) * die.scale.x

    nm = die.data
    faces = G["face_planes"](nm, cfg["faces"])
    sym = cfg.get("face_symmetry") or G["face_symmetry"](nm, faces[0])
    steps = cfg.get("twist_steps") or sym
    probe = dict(cfg)
    for t in range(steps):
        probe["face_twists"] = [t] * cfg["faces"]
        values = G["face_masks"](nm, probe)
        for v in range(1, cfg["faces"] + 1):
            die.location = (0, 0, rest_z)
            die.rotation_quaternion = G["rest_quat"](values, v)
            bpy.context.view_layer.update()
            sc.render.filepath = os.path.join(out, "v%02d_t%d.png" % (v, t))
            bpy.ops.render.render(write_still=True)
    print("TWIST SHEET RENDERED: %d values x %d twists -> %s"
          % (cfg["faces"], steps, out))


def assemble_twists(die_name):
    """One row per value, one column per twist."""
    from PIL import Image, ImageDraw
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import dice_config as cfgmod

    cfg = cfgmod.die(die_name)
    src = os.path.join(cfgmod.work_dir(die_name), "twistsheet")
    files = sorted(os.listdir(src))
    twists = sorted({int(f[f.index("_t") + 2]) for f in files if f.startswith("v")})
    pad, label, bg, ink = 6, 22, (246, 244, 240), (20, 20, 20)

    # Crop to the face the value is read off and enlarge it, rather than tiling whole
    # dice. A whole die at thumbnail size is not enough to tell a numeral rotated by a
    # third of a turn from an upright one -- read that way, four of the d8's eight
    # entries came out wrong, and the mistake only surfaced when the finished die was
    # looked at. The face sits in the upper middle of the frame for every solid here.
    crop = (int(CELL * 0.23), int(CELL * 0.12), int(CELL * 0.77), int(CELL * 0.49))
    cw, ch = (crop[2] - crop[0]) * 3, (crop[3] - crop[1]) * 3
    sheet = Image.new("RGB", (len(twists) * (cw + pad) + pad + 40,
                              cfg["faces"] * (ch + pad) + pad + label), bg)
    draw = ImageDraw.Draw(sheet)
    for t in twists:
        draw.text((44 + t * (cw + pad), 6), "twist %d" % t, fill=ink)
    for v in range(1, cfg["faces"] + 1):
        y = label + pad + (v - 1) * (ch + pad)
        draw.text((8, y + ch // 2), "%d" % v, fill=ink)
        for t in twists:
            f = os.path.join(src, "v%02d_t%d.png" % (v, t))
            if not os.path.exists(f):
                continue
            tile = Image.new("RGBA", (CELL, CELL), bg + (255,))
            tile.alpha_composite(Image.open(f).convert("RGBA"))
            sheet.paste(tile.convert("RGB").crop(crop).resize((cw, ch), Image.LANCZOS),
                        (40 + t * (cw + pad), y))
    dest = os.path.join(src, "%s_twists.png" % die_name)
    sheet.save(dest)
    print("%s -> %s  (%dx%d)" % (die_name, dest, sheet.size[0], sheet.size[1]))
    print("For each row pick the column whose numeral reads upright; that column index")
    print("is the face_twists entry for that value's face.")
    return dest


def assemble(die_name, rest=False):
    from PIL import Image, ImageDraw
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import dice_config as cfgmod

    kind = "restsheet" if rest else "facesheet"
    stem = "rest_" if rest else "face_"
    src = os.path.join(cfgmod.work_dir(die_name), kind)
    files = sorted(f for f in os.listdir(src) if f.startswith(stem))
    if not files:
        raise SystemExit("no renders in %s" % src)

    cols = 5
    rows = (len(files) + cols - 1) // cols
    pad, label = 8, 26
    bg, ink = (246, 244, 240), (20, 20, 20)
    sheet = Image.new("RGB", (cols * (CELL + pad) + pad,
                              rows * (CELL + label + pad) + pad), bg)
    draw = ImageDraw.Draw(sheet)
    for i, f in enumerate(files):
        r, c = divmod(i, cols)
        x = pad + c * (CELL + pad)
        y = pad + r * (CELL + label + pad)
        tile = Image.new("RGBA", (CELL, CELL), bg + (255,))
        tile.alpha_composite(Image.open(os.path.join(src, f)).convert("RGBA"))
        sheet.paste(tile.convert("RGB"), (x, y + label))
        draw.text((x + 6, y + 6), "%s %d" % ("shows" if rest else "face",
                                             i + 1 if rest else i), fill=ink)

    dest = os.path.join(src, "%s_%s.png" % (die_name, "rest" if rest else "faces"))
    sheet.save(dest)
    print("%d tiles -> %s  (%dx%d)" % (len(files), dest, sheet.size[0], sheet.size[1]))
    if rest:
        print("Each tile is labelled with the value the game thinks it is showing.")
        print("A wrong number is a face_values entry; a number on its side or upside")
        print("down is a face_twists entry.")
    else:
        print("Read the numeral on each and record it in dice_config.py as face_values,")
        print("one entry per face index. Watch 6 against 9 -- the model should underline one.")
    return dest


if __name__ == "__main__":
    if "--assemble" in sys.argv:
        assemble(sys.argv[sys.argv.index("--assemble") + 1])
    elif "--assemble-rest" in sys.argv:
        assemble(sys.argv[sys.argv.index("--assemble-rest") + 1], rest=True)
    elif "--assemble-twists" in sys.argv:
        assemble_twists(sys.argv[sys.argv.index("--assemble-twists") + 1])
    elif "--twists" in sys.argv:
        render_twists()
    elif "--rest" in sys.argv:
        render_rest()
    else:
        render()
