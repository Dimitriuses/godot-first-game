"""Render every face of a die flat-on, indexed, so a human can read the numerals once.

Pip counts give a d6 its face-to-value mapping for free. Every other die in the pack is
alphanumeric and you cannot count a glyph, so the mapping has to be read by eye exactly
once and written into `dice_config.py` as `face_values` (ROADMAP 8b).

    DICE_DIE=d20 blender "<the blend>" --background --python tools/dice-render/face_sheet.py
    python tools/dice-render/face_sheet.py --assemble d20

The first command renders one image per face into $DICE_WORK/facesheet/<die>/; the
second tiles them into a single labelled sheet to look at. Face indices are the order
`face_planes()` returns, which is sorted by normal and therefore stable between runs.
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
    out = os.path.join(work, "facesheet", cfg["name"])
    os.makedirs(out, exist_ok=True)

    src = bpy.data.objects[cfg["source_object"]]
    nm = G["normalised_mesh"](src)
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
    cd.ortho_scale = 1.15        # the whole die, with a little air around it
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
    for k, (n, _d, _r) in enumerate(faces):
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


def assemble(die_name):
    from PIL import Image, ImageDraw
    import dice_config as cfgmod

    work = os.environ.get("DICE_WORK") or os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "build")
    src = os.path.join(work, "facesheet", die_name)
    files = sorted(f for f in os.listdir(src) if f.startswith("face_"))
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
        draw.text((x + 6, y + 6), "face %d" % i, fill=ink)

    dest = os.path.join(src, "%s_faces.png" % die_name)
    sheet.save(dest)
    print("%d faces -> %s  (%dx%d)" % (len(files), dest, sheet.size[0], sheet.size[1]))
    print("Read the numeral on each and record it in dice_config.py as face_values,")
    print("one entry per face index. Watch 6 against 9 -- the model should underline one.")
    return dest


if __name__ == "__main__":
    if "--assemble" in sys.argv:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        assemble(sys.argv[sys.argv.index("--assemble") + 1])
    else:
        render()
