"""Build the toon render scene and render crisp sub-frames for each die animation.

Runs *inside* Blender, against the CC0 source model in references/. It does not
touch the model or save the .blend -- everything is built into a throwaway scene
called "DiceRender".

    blender "references/Dice D20 D12 D8 D10 D8 D6 D4/Dices blendswap.blend" \
            --background --python tools/dice-render/render.py

Output goes to $DICE_WORK (default tools/dice-render/build/), one directory per
animation holding the sub-frames plus a meta.json describing them. Motion blur,
outline and shadow are applied afterwards by composite.py.
"""
import bpy, bmesh, math, json, os, sys
from mathutils import Vector, Quaternion, Euler
from bpy_extras.object_utils import world_to_camera_view

SRC_OBJECT = "D6 Dotted"      # which die in the pack to render
SCENE      = "DiceRender"
RES        = 512              # rendered at 512, downsampled to 128 by composite.py
REST_Z     = 0.5              # die centre height when sitting on the ground
YAW        = math.radians(-6) # slight turn; more than this pushes a face near edge-on

WORK = os.environ.get("DICE_WORK") or os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "build")

# Sampled from the previous artwork so the die still reads the same over the board.
PALETTE = {"lit": "F1F0F7", "mid": "D6DCEA", "dark": "BDBECF", "darkest": "929AAB",
           "pip": "121212", "red": "D42A2A"}
# Fixed shading direction. Chosen so that at rest the top face lands in the "lit"
# band, the left face in "mid" and the right face in "dark" (see BANDS).
KEY   = Vector((0.209, -0.4996, 0.8407)).normalized()
BANDS = [(0.00, "darkest"), (0.14, "dark"), (0.38, "mid"), (0.65, "lit")]


def srgb2lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    f = lambda v: v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return tuple(f(v) for v in c) + (1.0,)


# --------------------------------------------------------------------- geometry
def normalised_mesh(src):
    """Copy the source mesh into a unit cube centred on the origin."""
    me, mw = src.data, src.matrix_world
    co = [mw @ v.co for v in me.vertices]
    mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    ctr, size = (mn + mx) / 2, max(mx - mn)
    nm = me.copy()
    nm.name = "DieRenderMesh"
    nm.materials.clear()
    for i, v in enumerate(nm.vertices):
        v.co = ((mw @ me.vertices[i].co) - ctr) / size
    return nm


AXES = [(0, 1), (0, -1), (1, 1), (1, -1), (2, 1), (2, -1)]


def pip_masks(nm):
    """Mark the pips from the recessed dimple geometry and report pips per face.

    The model ships a `Dots` vertex group, but it covers a square patch around
    each round dimple (77% of all faces), which renders the pips as squares.
    The vertices that actually sit below the face plane give the true circular
    rim instead.
    """
    onface, recessed = {}, set()
    for i, v in enumerate(nm.vertices):
        for ax, sgn in AXES:
            flat = max(abs(v.co[k]) for k in range(3) if k != ax)
            if sgn * v.co[ax] > 0.40 and flat < 0.40:
                onface[i] = (ax, sgn)
                if sgn * v.co[ax] < 0.5 - 0.003:
                    recessed.add(i)
                break

    bm = bmesh.new(); bm.from_mesh(nm); bm.verts.ensure_lookup_table()
    seen, pips = set(), []
    for i in recessed:
        if i in seen:
            continue
        stack, comp = [i], []
        seen.add(i)
        while stack:
            c = stack.pop(); comp.append(c)
            for e in bm.verts[c].link_edges:
                o = e.other_vert(bm.verts[c]).index
                if o in recessed and o not in seen:
                    seen.add(o); stack.append(o)
        pips.append(comp)
    bm.free()

    counts = {}
    for comp in pips:
        counts[onface[comp[0]]] = counts.get(onface[comp[0]], 0) + 1
    one = [k for k, v in counts.items() if v == 1]
    if len(one) != 1:
        raise SystemExit("expected exactly one single-pip face, found %d" % len(one))

    for nm_ in ("DotMask", "RedPip"):
        if nm_ in nm.color_attributes:
            nm.color_attributes.remove(nm.color_attributes[nm_])
    dm = nm.color_attributes.new(name="DotMask", type='FLOAT_COLOR', domain='POINT')
    rp = nm.color_attributes.new(name="RedPip", type='FLOAT_COLOR', domain='POINT')
    for i in range(len(nm.vertices)):
        d = 1.0 if i in recessed else 0.0
        r = 1.0 if (d and onface[i] == one[0]) else 0.0
        dm.data[i].color = (d, d, d, 1.0)
        rp.data[i].color = (r, r, r, 1.0)
    return {v: k for k, v in counts.items()}       # value -> (axis, sign)


# --------------------------------------------------------------------- material
def toon_material():
    mat = bpy.data.materials.new("DieToon")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    mk = lambda t, **kw: (lambda x: ([setattr(x, k, v) for k, v in kw.items()], x)[1])(
        nt.nodes.new(t))

    geo = mk('ShaderNodeNewGeometry', location=(-900, 300))
    dot = mk('ShaderNodeVectorMath', location=(-700, 300)); dot.operation = 'DOT_PRODUCT'
    dot.inputs[1].default_value = KEY[:]
    ramp = mk('ShaderNodeValToRGB', location=(-500, 300))
    cr = ramp.color_ramp
    cr.interpolation = 'CONSTANT'
    cr.elements[0].position, cr.elements[0].color = BANDS[0][0], srgb2lin(PALETTE[BANDS[0][1]])
    cr.elements[1].position, cr.elements[1].color = BANDS[1][0], srgb2lin(PALETTE[BANDS[1][1]])
    for pos, key in BANDS[2:]:
        cr.elements.new(pos).color = srgb2lin(PALETTE[key])

    acol = mk('ShaderNodeVertexColor', location=(-900, -150)); acol.layer_name = "DotMask"
    ared = mk('ShaderNodeVertexColor', location=(-900, -350)); ared.layer_name = "RedPip"
    thr = mk('ShaderNodeMath', location=(-700, -150))
    thr.operation = 'GREATER_THAN'; thr.inputs[1].default_value = 0.15
    thrR = mk('ShaderNodeMath', location=(-700, -350))
    thrR.operation = 'GREATER_THAN'; thrR.inputs[1].default_value = 0.15
    pipmix = mk('ShaderNodeMixRGB', location=(-450, -250))
    pipmix.inputs['Color1'].default_value = srgb2lin(PALETTE["pip"])
    pipmix.inputs['Color2'].default_value = srgb2lin(PALETTE["red"])
    bodymix = mk('ShaderNodeMixRGB', location=(-150, 0))
    emis = mk('ShaderNodeEmission', location=(120, 0))
    out = mk('ShaderNodeOutputMaterial', location=(340, 0))

    L = nt.links.new
    L(geo.outputs['Normal'], dot.inputs[0]); L(dot.outputs['Value'], ramp.inputs[0])
    L(acol.outputs['Color'], thr.inputs[0]); L(ared.outputs['Color'], thrR.inputs[0])
    L(thrR.outputs[0], pipmix.inputs['Fac'])
    L(ramp.outputs['Color'], bodymix.inputs['Color1'])
    L(pipmix.outputs['Color'], bodymix.inputs['Color2'])
    L(thr.outputs[0], bodymix.inputs['Fac'])
    L(bodymix.outputs['Color'], emis.inputs['Color'])
    L(emis.outputs[0], out.inputs['Surface'])
    return mat


def build_scene():
    if SCENE in bpy.data.scenes:
        bpy.data.scenes.remove(bpy.data.scenes[SCENE])
    sc = bpy.data.scenes.new(SCENE)
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x = sc.render.resolution_y = RES
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.compression = 15
    sc.eevee.taa_render_samples = 24
    sc.view_settings.view_transform = 'Standard'   # exact toon colours, no filmic curve
    sc.world = None                                # shading is normal-driven, not lit

    src = bpy.data.objects[SRC_OBJECT]
    nm = normalised_mesh(src)
    values = pip_masks(nm)
    nm.materials.append(toon_material())

    die = bpy.data.objects.new("DieRender", nm)
    sc.collection.objects.link(die)
    die.rotation_mode = 'QUATERNION'

    cd = bpy.data.cameras.new("DieCam"); cd.type = 'PERSP'; cd.lens = 85
    cam = bpy.data.objects.new("DieCam", cd)
    sc.collection.objects.link(cam)
    elev, azim, dist = math.radians(31), math.radians(45), 7.2
    d = Vector((math.cos(elev) * math.sin(azim), -math.cos(elev) * math.cos(azim),
                math.sin(elev)))
    cam.location = Vector((0, 0, 0.92)) + d * dist   # aimed high so the die sits low in frame
    cam.rotation_euler = (math.pi / 2 - elev, 0, azim)
    sc.camera = cam
    return sc, die, cam, values


# -------------------------------------------------------------------- animation
# Rotation that brings each value's face to world +Z, derived from the pip counts.
def rest_quat(values, n):
    ax, sgn = values[n]
    v = Vector((0, 0, 0)); v[ax] = sgn
    base = {(2, 1): Euler((0, 0, 0)), (2, -1): Euler((math.pi, 0, 0)),
            (0, 1): Euler((0, -math.pi / 2, 0)), (0, -1): Euler((0, math.pi / 2, 0)),
            (1, 1): Euler((math.pi / 2, 0, 0)), (1, -1): Euler((-math.pi / 2, 0, 0))}[(ax, sgn)]
    return Quaternion((0, 0, 1), YAW) @ base.to_quaternion()


# (f_start, f_end, z_start, z_end, apex); apex=None -> straight fall / flat
SEGS = [(0, 22, 1.70, REST_Z, None), (22, 40, REST_Z, REST_Z, 1.25),
        (40, 52, REST_Z, REST_Z, 0.82), (52, 60, REST_Z, REST_Z, 0.62),
        (60, 65, REST_Z, REST_Z, 0.54), (65, 90, REST_Z, REST_Z, None)]


def bounce_z(f, segs=SEGS):
    for a, b, z0, z1, apex in segs:
        if f <= b or (a, b) == segs[-1][:2]:
            if f < a:
                continue
            u = 0.0 if b == a else min(max((f - a) / (b - a), 0.0), 1.0)
            if apex is None:
                return z0 + (z1 - z0) * (u * u if z1 < z0 else u)
            return REST_Z + 4.0 * (apex - REST_Z) * u * (1.0 - u)
    return REST_Z


# per-face variation so the six throws don't read as the same clip
FACE = {
    1: dict(turns=7.0, a0=( 0.35, 0.75, 0.55), a1=( 0.10, 0.30, 0.95), drift=(-0.38,  0.26)),
    2: dict(turns=6.2, a0=(-0.60, 0.55, 0.58), a1=(-0.15, 0.15, 0.98), drift=( 0.34,  0.22)),
    3: dict(turns=7.6, a0=( 0.70,-0.45, 0.55), a1=( 0.25,-0.10, 0.96), drift=(-0.28, -0.32)),
    4: dict(turns=6.6, a0=(-0.45,-0.70, 0.52), a1=(-0.05,-0.25, 0.97), drift=( 0.40, -0.18)),
    5: dict(turns=7.9, a0=( 0.55, 0.62,-0.56), a1=( 0.18, 0.20, 0.94), drift=(-0.20,  0.38)),
    6: dict(turns=6.9, a0=(-0.68, 0.40,-0.60), a1=(-0.20, 0.12, 0.97), drift=( 0.26,  0.34)),
}
SPIN_END, ROCK_END = 66.0, 86.0     # tumble decays by 66, settle wobble dies by 86


def face_motion(values, n, f, nframes=91):
    p = FACE[n]
    s = min(1.0, max(0.0, f / (nframes - 1)))       # sub-frame sampling overshoots both ends
    z = bounce_z(min(float(nframes - 1), max(0.0, f)))
    k = (1.0 - s) ** 1.6
    loc = Vector((p["drift"][0] * k, p["drift"][1] * k, z))

    q = rest_quat(values, n)
    if f < ROCK_END:
        u = max(0.0, f - SPIN_END)
        amp = math.radians(9.0) * (1.0 if f < SPIN_END else
                                   math.exp(-u / 5.5) * math.cos(u * 0.85))
        q = Quaternion(Vector((0.80, 0.60, 0.0)).normalized(), amp) @ q
    if f < SPIN_END:
        w = min(1.0, max(0.0, f / SPIN_END))
        ang = p["turns"] * 2 * math.pi * (1.0 - w) ** 2.2
        ax = Vector(p["a0"]).normalized().lerp(Vector(p["a1"]).normalized(), w).normalized()
        q = Quaternion(ax, ang) @ q
    return loc, q


# Idle loops spin a whole number of turns over the clip, so frame N == frame 0.
IDLE = {0: dict(turns=2.0, axis=(0.25, 0.45, 0.86), bob=0.20, base=1),
        1: dict(turns=3.0, axis=(0.72, -0.38, 0.58), bob=0.30, base=5)}


def idle_motion(values, k, f, nframes=30):
    p = IDLE[k]
    s = f / nframes                                 # /n, not /(n-1)
    z = REST_Z + 0.28 + p["bob"] * math.sin(2 * math.pi * s)
    q = Quaternion(Vector(p["axis"]).normalized(),
                   p["turns"] * 2 * math.pi * s) @ rest_quat(values, p["base"])
    return Vector((0.0, 0.0, z)), q


# --------------------------------------------------------------------- rendering
def shadow_params(sc, cam, die):
    """Screen position and pixel radius of the ground patch under the die."""
    W, H = sc.render.resolution_x, sc.render.resolution_y
    g = Vector((die.location.x, die.location.y, 0.0))
    c = world_to_camera_view(sc, cam, g)
    e = world_to_camera_view(sc, cam, g + Vector((0.5, 0.0, 0.0)))
    h = max(0.0, die.location.z - REST_Z)
    return dict(x=c.x * W, y=(1.0 - c.y) * H,
                r=math.hypot((e.x - c.x) * W, (e.y - c.y) * H),
                scale=1.0 / (1.0 + 0.55 * h), alpha=0.42 / (1.0 + 0.95 * h))


def ang_between(qa, qb):
    return 2.0 * math.acos(min(1.0, abs((qa.inverted() @ qb).normalized().w)))


def render_anim(sc, cam, die, tag, nframes, motion, shutter=0.9,
                deg_per_sub=4.0, max_sub=20):
    """Render each frame as several crisp samples across its shutter interval."""
    out = os.path.join(WORK, tag)
    os.makedirs(out, exist_ok=True)
    meta = {"tag": tag, "nframes": nframes, "res": sc.render.resolution_x, "frames": []}
    for f in range(nframes):
        deg = math.degrees(ang_between(motion(f - 0.5)[1], motion(f + 0.5)[1]))
        nsub = int(min(max_sub, max(1, math.ceil(deg / deg_per_sub))))
        subs = []
        for i in range(nsub):
            off = 0.0 if nsub == 1 else shutter * (i / (nsub - 1) - 0.5)
            die.location, die.rotation_quaternion = motion(f + off)
            bpy.context.view_layer.update()
            sc.render.filepath = os.path.join(out, "f%03d_s%02d.png" % (f, i))
            bpy.ops.render.render(write_still=True)
            subs.append(shadow_params(sc, cam, die))
        meta["frames"].append({"f": f, "nsub": nsub, "deg": round(deg, 2), "subs": subs})
    json.dump(meta, open(os.path.join(out, "meta.json"), "w"))
    return sum(x["nsub"] for x in meta["frames"])


def main():
    sc, die, cam, values = build_scene()
    if bpy.context.window:
        bpy.context.window.scene = sc
    print("pips per face:", {v: k for k, v in values.items()})
    os.makedirs(WORK, exist_ok=True)
    total = 0
    for n in range(1, 7):
        total += render_anim(sc, cam, die, "face%d" % n, 91,
                             lambda f, n=n: face_motion(values, n, f))
        print("  face%d done (%d sub-frames so far)" % (n, total))
    for k in (0, 1):
        total += render_anim(sc, cam, die, "idle%d" % k, 30,
                             lambda f, k=k: idle_motion(values, k, f))
        print("  idle%d done (%d sub-frames so far)" % (k, total))
    print("%d sub-frames -> %s" % (total, WORK))


if __name__ == "__main__":
    main()
