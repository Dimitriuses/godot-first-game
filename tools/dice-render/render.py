"""Build the toon render scene and render crisp sub-frames for each die animation.

Runs *inside* Blender, against the CC0 source model in references/. It does not
touch the model or save the .blend -- everything is built into a throwaway scene
called "DiceRender".

    blender "references/Dice D20 D12 D8 D10 D8 D6 D4/Dices blendswap.blend" \
            --background --python tools/dice-render/render.py

Which die: `DICE_DIE=d20` (default d6). Which clips: `DICE_ONLY=face3,idle1`
(default all of them) -- rendering a clip at a time keeps the sub-frames on disk down
to something a 20-sided die fits in. `DICE_RED=20` tints one face's glyph.

Output goes to build/<die>/faces/<clip>/, one directory per animation holding the
sub-frames plus a meta.json describing them. Motion blur, outline and shadow are
applied afterwards by composite.py.
"""
import bpy, bmesh, colorsys, math, json, os, sys, time
from mathutils import Vector, Quaternion
from bpy_extras.object_utils import world_to_camera_view

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dice_config                                          # noqa: E402

# Which die to render: `DICE_DIE=d20 blender ... --python render.py`.
DIE        = os.environ.get("DICE_DIE", "d6")
CFG        = dice_config.die(DIE)
SRC_OBJECT = CFG["source_object"]
N_FACES    = CFG["faces"]

SCENE      = "DiceRender"
RES        = 512              # rendered at 512, downsampled to 128 by composite.py
YAW        = math.radians(-6) # slight turn; more than this pushes a face near edge-on

# How big a die is drawn. `normalised_mesh` fits every die into a unit cube, which is
# the wrong measure of "the same size": a cube fills its bounding box and an
# icosahedron does not, so a d20 normalised that way renders about a third smaller
# than the d6 sharing the board with it -- measured at 42px against 58px across.
#
# The right invariant for a die that tumbles through every orientation is its *mean*
# silhouette, and by Cauchy's formula that is exactly a quarter of its surface area.
# So scale by 1/sqrt(area), calibrated to leave the d6 -- whose artwork is already
# shipped -- at exactly 1.0. That is the number below, measured off the model.
#
# It is not perfectly invariant: the recessed glyphs are part of the surface, so the
# numbered d6 measures 2.4100 against the dotted d6's 2.4007. That is 0.4%, a third of
# a pixel at 128, and not worth a convex hull to remove.
SILHOUETTE = 2.4007206052

# How far off-centre a die has to be, as a fraction of its inradius, before
# `recentre_on_faces` will move it. See that function for why this is not zero.
TOLERANCE = 1e-2

# Set per die by build_scene(): the height of the die's centre when it is resting on
# the ground, which is its inradius once scaled. Everything vertical is expressed as
# an offset from it.
REST_Z     = 0.5

WORK = dice_config.work_dir(DIE)

# Render only these animations, comma separated: `DICE_ONLY=face3,idle1`. Empty renders
# everything. Rendering one clip at a time is how a d20 stays inside a sane amount of
# disk -- 15,000 sub-frames at 512px is about 2.2 GB if they all have to exist at once.
ONLY = [t for t in os.environ.get("DICE_ONLY", "").replace(" ", "").split(",") if t]

# Sampled from the previous artwork so the die still reads the same over the board.
PALETTE = {"lit": "F1F0F7", "mid": "D6DCEA", "dark": "BDBECF", "darkest": "929AAB",
           "pip": "121212", "red": "D42A2A"}
# Fixed shading direction. Chosen so that at rest the top face lands in the "lit"
# band, the left face in "mid" and the right face in "dark" (see BANDS).
KEY   = Vector((0.209, -0.4996, 0.8407)).normalized()
BANDS = [(0.00, "darkest"), (0.14, "dark"), (0.38, "mid"), (0.65, "lit")]


SCALE = 1.0     # set with REST_Z by build_scene()


def rest_height():
    """REST_Z as build_scene() left it, for callers that loaded this module by path."""
    return REST_Z


def srgb2lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return srgb_tuple_to_lin(c)


def srgb_tuple_to_lin(c):
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


def presentation_scale(nm):
    """How much to enlarge this die so it reads the same size as the d6."""
    return SILHOUETTE / math.sqrt(sum(p.area for p in nm.polygons))


def recentre_on_faces(nm, n_faces):
    """Move the mesh so every numbered face is the same distance from the origin.

    `normalised_mesh` centres on the bounding box, which is the centre for anything
    centrally symmetric -- a cube, an octahedron, an icosahedron -- and measurably wrong
    for anything that is not. A tetrahedron's bbox centre leaves its four face planes at
    0.10 to 0.42 instead of all alike, which puts the rotation pivot off the middle of
    the die (it wobbles), the resting height wrong, and the in-plane radii that decide a
    face's corners inconsistent between congruent faces.

    The point that fixes all three is the incentre: solve n_i . c + r = d_i for the
    centre c and the common distance r, least squares over every face. Exactly
    determined for a tetrahedron, overdetermined and already satisfied for the rest.

    Applied only when it is worth something, hence TOLERANCE. For a centrally symmetric
    solid the bounding-box centre already *is* the incentre and the solve returns pure
    numerical noise -- the d20's answer is 4e-4 of its inradius against the d4's 1.0.
    Moving by that noise is not free: `recessed_by_face` decides what is a glyph by an
    absolute depth below the face plane, so nudging the planes flips borderline vertices
    in and out of the masks and visibly re-inks the numerals. Measured: shifting the d20
    by its noise changed 71,355 pixels of one landing clip.
    """
    import numpy as np
    faces = face_planes(nm, n_faces)
    a = np.array([[n.x, n.y, n.z, 1.0] for n, _d, _r, _v in faces])
    b = np.array([d for _n, d, _r, _v in faces])
    solved = np.linalg.lstsq(a, b, rcond=None)[0]
    shift = Vector((float(solved[0]), float(solved[1]), float(solved[2])))
    if shift.length <= TOLERANCE * abs(float(solved[3])):
        return Vector((0.0, 0.0, 0.0))
    for v in nm.vertices:
        v.co = v.co - shift
    return shift


def face_planes(nm, n_faces):
    """The die's numbered faces, as (normal, offset, in-plane radius).

    Polygons are grouped by normal direction and distance from the centre, then the
    `n_faces` largest groups by area are the faces. Two rules that look right and are
    not: *the planes furthest from the centre* fails, because a beveled die's rounded
    corners sit further out than its faces do (0.50 against 0.42 on the D20); and a
    fixed area threshold finds nine planes on a six-sided die. Measured across the whole
    pack, the next-largest plane after the real faces is 7.8x to 41x smaller.
    """
    bm = bmesh.new(); bm.from_mesh(nm); bm.normal_update()
    groups = []                     # [normal, offset, area, {vertex indices}]
    for f in bm.faces:
        n = f.normal.normalized()
        d = f.calc_center_median().dot(n)
        a = f.calc_area()
        for g in groups:
            if n.dot(g[0]) > 0.995 and abs(d - g[1]) < 0.012:
                w = g[2] + a
                g[0] = (g[0] * g[2] + n * a).normalized()
                g[1] = (g[1] * g[2] + d * a) / w
                g[2] = w
                g[3].update(v.index for v in f.verts)
                break
        else:
            groups.append([n, d, a, {v.index for v in f.verts}])
    bm.free()

    groups.sort(key=lambda g: -g[2])
    faces, rest = groups[:n_faces], groups[n_faces:]
    if len(faces) < n_faces:
        raise SystemExit("found %d planes, need %d faces" % (len(faces), n_faces))
    if rest and faces[-1][2] < rest[0][2] * 2.0:
        raise SystemExit("face selection is ambiguous: smallest face %.4f, next plane %.4f"
                         % (faces[-1][2], rest[0][2]))

    out = []
    for n, d, _a, verts in faces:
        radius = max((nm.vertices[i].co - n * nm.vertices[i].co.dot(n)).length
                     for i in verts)
        out.append((n.copy(), d, radius, frozenset(verts)))

    # Order by normal, not by area. Selection is by area, but a D20's faces differ by
    # under 12% and are summed in mesh order, so ties could reshuffle between runs --
    # and a face-to-value table (ROADMAP 8b) is indexed by position, so it must not.
    out.sort(key=lambda f: (round(f[0].z, 4), round(f[0].y, 4), round(f[0].x, 4)))
    return out


def recessed_by_face(nm, faces, band=0.06, depth=0.004):
    """Vertices lying on each face and cut below its plane: the pips or the numerals.

    The model ships a `Dots` vertex group, but it covers a square patch around each
    round dimple (77% of all faces), which renders the pips as squares. Taking the
    geometry that is actually recessed gives the true rim instead, and works the same
    for indented numerals on the dice that carry those.
    """
    out = [set() for _ in faces]
    for i, v in enumerate(nm.vertices):
        # Nearest plane wins, not the first one that matches. On a d20 the faces are
        # only ~41 degrees apart, so a vertex near a shared edge falls inside two
        # bands; taking the first left strays on one face and bitten-off digits on
        # the other. The die is convex, so no vertex is above any face plane and the
        # smallest gap is always the face the vertex genuinely belongs to.
        best, best_gap = -1, None
        for k, (n, d, radius, _verts) in enumerate(faces):
            proj = v.co.dot(n)
            gap = d - proj
            if -band < gap < band and (v.co - n * proj).length < radius * 1.02:
                if best_gap is None or gap < best_gap:
                    best, best_gap = k, gap
        if best >= 0 and best_gap > depth:
            out[best].add(i)
    return out


def pip_counts(nm, recessed):
    """How many separate pips sit on each face, by connected components."""
    bm = bmesh.new(); bm.from_mesh(nm); bm.verts.ensure_lookup_table()
    counts = []
    for verts in recessed:
        seen, clusters = set(), 0
        for i in verts:
            if i in seen:
                continue
            clusters += 1
            stack = [i]
            seen.add(i)
            while stack:
                c = stack.pop()
                for e in bm.verts[c].link_edges:
                    o = e.other_vert(bm.verts[c]).index
                    if o in verts and o not in seen:
                        seen.add(o)
                        stack.append(o)
        counts.append(clusters)
    bm.free()
    return counts


def plane_basis(n):
    """Two unit vectors spanning a face's plane."""
    u = Vector((0, 0, 1)).cross(n)
    if u.length < 1e-6:
        u = Vector((1, 0, 0))
    u.normalize()
    return u, n.cross(u).normalized()


def rim(nm, face, inner=0.5):
    """The face's outer vertices, as (radius, angle) in its own plane."""
    n, _d, radius, verts = face
    u, w = plane_basis(n)
    out = []
    for i in verts:
        p = nm.vertices[i].co
        p = p - n * p.dot(n)
        if p.length >= radius * inner:
            out.append((p.length, math.atan2(p.dot(w), p.dot(u))))
    return out


def coherence(pts, m):
    """How strongly a set of rim points repeats every 360/m degrees, from 0 to 1."""
    sc = sum(r * math.cos(m * t) for r, t in pts)
    ss = sum(r * math.sin(m * t) for r, t in pts)
    return math.hypot(sc, ss) / (sum(r for r, _t in pts) or 1e-9)


def face_symmetry(nm, face):
    """How many corners the face has: 3 for the d4/d8/d20, 4 for a cube, 5 for a d12.

    Measured rather than configured, and it is the *smallest* m that fits, not the
    strongest. Strongest is unsound: a triangle's three corners are perfectly coherent
    at m=6 as well as m=3, and a square's four at m=8, so taking the highest score
    reports a d4 as six-sided.

    Only the outermost vertices count. A triangle's edge midpoints sit at half its
    circumradius and are three-fold coherent 60 degrees out of phase with its corners,
    which cancels most of the signal -- including them scores a triangle 0.33 at m=3.
    """
    pts = rim(nm, face, inner=0.85)
    for m in (3, 4, 5, 6):
        if coherence(pts, m) > 0.7:
            return m
    raise SystemExit("cannot tell how many corners this face has: %s"
                     % ", ".join("m=%d %.2f" % (m, coherence(pts, m)) for m in (3, 4, 5, 6)))


def corner_angle(nm, face, m):
    """Where the face's corners point, as an angle in its own plane.

    Averaging exp(i*m*theta) over the rim picks out an m-sided face's symmetry however
    the mesh happens to be ordered; the corners then sit at that angle and at multiples
    of 360/m from it. Only used when a die has a `face_twists` table.

    Takes the wide rim, unlike `face_symmetry` above. Non-corner vertices dilute the
    signal but do not move its phase -- a triangle's edge midpoints are three-fold
    coherent 180 degrees out, which subtracts from the magnitude and leaves the angle
    alone. Measured across the pack the two cuts agree exactly on the d4 and the d6, and
    to 2.6 degrees on the d20, well inside a 120-degree twist step. The wide one stays
    because it is what rendered the d20 artwork that is already committed.
    """
    pts = rim(nm, face, inner=0.5)
    sc = sum(r * math.cos(m * t) for r, t in pts)
    ss = sum(r * math.sin(m * t) for r, t in pts)
    return math.atan2(ss, sc) / m


def face_base(normal, face_down):
    """Rotation that brings a face's normal to the camera axis.

    For the one face pointing straight away the rotation is ambiguous -- any axis in
    the plane will do -- so it is pinned to a flip about +X, which is what the
    axis-aligned lookup this replaced did.

    `face_down` sends the normal to -Z instead of +Z. A tetrahedron has no parallel
    faces, so a d4 cannot rest with one face up: it stands on a face with a vertex at
    the top, and the number is read off the faces around that vertex.
    """
    target = Vector((0, 0, -1)) if face_down else Vector((0, 0, 1))
    return (Quaternion(Vector((1, 0, 0)), math.pi) if normal.dot(target) < -0.9999
            else normal.rotation_difference(target))


def face_roll(nm, face, candidate, m, face_down):
    """Spin about the view axis that puts the chosen corner at the top of the screen.

    Bringing a face normal to the camera axis leaves the spin about that axis free. For
    pips that does not matter; for a numeral it decides whether the die reads "13" or
    something lying on its side, so it has to be pinned.
    """
    n = face[0]
    base = face_base(n, face_down)
    u, w = plane_basis(n)
    ang = corner_angle(nm, face, m) + candidate * (2 * math.pi / m)
    screen = base @ (u * math.cos(ang) + w * math.sin(ang))
    return math.atan2(screen.x, screen.y)


def face_masks(nm, cfg):
    """Colour attributes for the glyphs, and how each face should be presented.

    Returns {value: (normal, roll)}. Values come from counting pips when the die has
    them, and from `face_values` in the config otherwise -- you cannot count a glyph,
    so an alphanumeric die's mapping is read off `face_sheet.py` once by eye.
    """
    n_faces = cfg["faces"]
    faces = face_planes(nm, n_faces)
    recessed = recessed_by_face(nm, faces)
    table = cfg.get("face_values")

    if table:
        if sorted(table) != list(range(1, n_faces + 1)):
            raise SystemExit("face_values for %s is not a permutation of 1..%d: %s"
                             % (cfg["name"], n_faces, sorted(table)))
        counts = list(table)
    else:
        counts = pip_counts(nm, recessed)
        if sorted(counts) != list(range(1, n_faces + 1)):
            raise SystemExit(
                "pip counts are not 1..%d but %s -- an alphanumeric die needs a "
                "face_values table in dice_config.py (ROADMAP 8b)"
                % (n_faces, sorted(counts)))
    for k, (n, _d, _r, _v) in enumerate(faces):
        opp = [j for j, (m, _, _, _) in enumerate(faces) if m.dot(n) < -0.995]
        if opp and counts[k] + counts[opp[0]] != n_faces + 1:
            raise SystemExit("faces %d and %d carry %d and %d; opposites should sum to %d"
                             % (k, opp[0], counts[k], counts[opp[0]], n_faces + 1))

    # Which face, if any, gets the red tint. On the d6 that is the lone pip and always
    # has been; on a numbered die it is a design choice, so it comes from the config.
    red = dice_config.red_for(cfg)
    one = counts.index(red) if red in counts else None
    for attr in ("DotMask", "RedPip"):
        if attr in nm.color_attributes:
            nm.color_attributes.remove(nm.color_attributes[attr])
    dm = nm.color_attributes.new(name="DotMask", type='FLOAT_COLOR', domain='POINT')
    rp = nm.color_attributes.new(name="RedPip", type='FLOAT_COLOR', domain='POINT')
    marked = set().union(*recessed) if recessed else set()
    for i in range(len(nm.vertices)):
        d = 1.0 if i in marked else 0.0
        r = 1.0 if (one is not None and i in recessed[one]) else 0.0
        dm.data[i].color = (d, d, d, 1.0)
        rp.data[i].color = (r, r, r, 1.0)

    twists = cfg.get("face_twists")
    if twists is not None and len(twists) != n_faces:
        raise SystemExit("face_twists for %s has %d entries, need %d"
                         % (cfg["name"], len(twists), n_faces))
    down = cfg["rest_face_down"]
    sym = cfg.get("face_symmetry") or face_symmetry(nm, faces[0])
    if twists is not None and any(not 0 <= t < sym for t in twists):
        raise SystemExit("face_twists for %s must be 0..%d on a %d-sided face"
                         % (cfg["name"], sym - 1, sym))
    rolls = ([face_roll(nm, faces[k], twists[k], sym, down) for k in range(n_faces)]
             if twists is not None else [0.0] * n_faces)

    return {counts[k]: (faces[k][0], rolls[k]) for k in range(n_faces)}


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
    return mat, ramp


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
    recentre_on_faces(nm, N_FACES)
    values = face_masks(nm, CFG)
    mat, ramp = toon_material()
    nm.materials.append(mat)

    # Scale the object, not the mesh. The geometry pass above works in absolute
    # distances -- how far below its face plane a vertex has to sit to count as part
    # of a glyph -- so resizing the mesh underneath it would quietly change which
    # vertices are glyphs.
    global REST_Z, SCALE
    SCALE = presentation_scale(nm)
    REST_Z = min(d for _n, d, _r, _v in face_planes(nm, N_FACES)) * SCALE

    die = bpy.data.objects.new("DieRender", nm)
    die.scale = (SCALE, SCALE, SCALE)
    sc.collection.objects.link(die)
    die.rotation_mode = 'QUATERNION'

    cd = bpy.data.cameras.new("DieCam")
    cd.type = 'ORTHO'
    cd.ortho_scale = 3.25       # margin for the high starting pose; avoids top-edge crop
    cam = bpy.data.objects.new("DieCam", cd)
    sc.collection.objects.link(cam)
    elev, azim, dist = math.radians(31), math.radians(45), 7.2
    d = Vector((math.cos(elev) * math.sin(azim), -math.cos(elev) * math.cos(azim),
                math.sin(elev)))
    cam.location = Vector((0, 0, 0.92)) + d * dist   # aimed high so the die sits low in frame
    cam.rotation_euler = (math.pi / 2 - elev, 0, azim)
    sc.camera = cam
    return sc, die, cam, values, ramp


# -------------------------------------------------------------------- animation
# Rotation that brings each value's face to world +Z, derived from the pip counts.
def rest_quat(values, n):
    """Bring face `n` up to the camera, spin it upright, then apply the presentation yaw.

    `roll` is the extra spin that makes a numeral read the right way up; it is zero for
    a pipped die, which has no particular way up. Both spins are about +Z so they add.
    """
    normal, roll = values[n]
    base = face_base(Vector(normal).normalized(), CFG["rest_face_down"])
    return Quaternion(Vector((0, 0, 1)), YAW + roll) @ base


# (f_start, f_end, z_start, z_end, apex), all heights *above the resting height* so
# the drop and the bounces still land on the ground for a die that rests higher than
# the d6 does. Absolute, not scaled: a bigger die bouncing the same distance reads as
# heavier, which is the right way for it to be wrong.
SEGS = [(0, 22, 1.20, 0.00, None), (22, 40, 0.00, 0.00, 0.75),
        (40, 52, 0.00, 0.00, 0.32), (52, 60, 0.00, 0.00, 0.12),
        (60, 65, 0.00, 0.00, 0.04), (65, 90, 0.00, 0.00, None)]


def bounce_z(f, segs=SEGS):
    for a, b, z0, z1, apex in segs:
        if f <= b or (a, b) == segs[-1][:2]:
            if f < a:
                continue
            u = 0.0 if b == a else min(max((f - a) / (b - a), 0.0), 1.0)
            if apex is None:
                return REST_Z + z0 + (z1 - z0) * (u * u if z1 < z0 else u)
            return REST_Z + 4.0 * apex * u * (1.0 - u)
    return REST_Z


SPIN_END, ROCK_END = 66.0, 86.0     # tumble decays by 66, settle wobble dies by 86


def face_motion(values, n, f, nframes=91):
    p = dice_config.throw_params(n, CFG)
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


# Both idle loops use the opening speed of the numbered roll: the derivative of
# turns * 2*pi * (1 - f/SPIN_END)^2.2 at frame zero.  Keeping a whole number of
# turns makes the 30-frame loop seamless, so seven turns is also the closest
# whole-turn match to the six face clips (6.2--7.9 turns over 66 frames).
IDLE_TURNS = 7.0
IDLE = {0: dict(axis=(0.25, 0.45, 0.86), bob=0.20),
        1: dict(axis=(0.72, -0.38, 0.58), bob=0.30)}
IDLE_BASE = dice_config.idle_bases(CFG)     # which face each loop spins about


def idle_motion(values, k, f, nframes=30):
    p = IDLE[k]
    s = f / nframes                                 # /n, not /(n-1)
    z = REST_Z + 0.28 + p["bob"] * math.sin(2 * math.pi * s)
    q = Quaternion(Vector(p["axis"]).normalized(),
                   IDLE_TURNS * 2 * math.pi * s) @ rest_quat(values, IDLE_BASE[k])
    return Vector((0.0, 0.0, z)), q


def set_palette_gradient(ramp, phase):
    """Cycle idle1 through the original animation's seamless rainbow."""
    keys = [key for _, key in BANDS]
    hue = phase % 1.0
    tint = Vector(colorsys.hsv_to_rgb(hue, 0.55, 1.0))
    lit = Vector((0.95, 0.95, 0.98))
    for elem, key in zip(ramp.color_ramp.elements, keys):
        base = Vector(tuple(int(PALETTE[key][i:i + 2], 16) / 255 for i in (0, 2, 4)))
        shade = sum(base) / sum(lit)
        elem.color = srgb_tuple_to_lin(tuple(min(1.0, c * shade) for c in tint))


# --------------------------------------------------------------------- rendering
def shadow_params(sc, cam, die):
    """Screen position and pixel radius of the ground patch under the die."""
    W, H = sc.render.resolution_x, sc.render.resolution_y
    g = Vector((die.location.x, die.location.y, 0.0))
    c = world_to_camera_view(sc, cam, g)
    e = world_to_camera_view(sc, cam, g + Vector((0.5 * SCALE, 0.0, 0.0)))
    h = max(0.0, die.location.z - REST_Z)
    return dict(x=c.x * W, y=(1.0 - c.y) * H,
                r=math.hypot((e.x - c.x) * W, (e.y - c.y) * H),
                scale=1.0 / (1.0 + 0.55 * h), alpha=0.42 / (1.0 + 0.95 * h))


def ang_between(qa, qb):
    return 2.0 * math.acos(min(1.0, abs((qa.inverted() @ qb).normalized().w)))


def render_anim(sc, cam, die, tag, nframes, motion, shutter=0.9,
                deg_per_sub=4.0, max_sub=20, before_sample=None):
    """Render each frame as several crisp samples across its shutter interval."""
    out = os.path.join(WORK, "faces", tag)
    os.makedirs(out, exist_ok=True)
    meta = {"tag": tag, "nframes": nframes, "res": sc.render.resolution_x, "frames": []}
    for f in range(nframes):
        deg = math.degrees(ang_between(motion(f - 0.5)[1], motion(f + 0.5)[1]))
        nsub = int(min(max_sub, max(1, math.ceil(deg / deg_per_sub))))
        subs = []
        for i in range(nsub):
            off = 0.0 if nsub == 1 else shutter * (i / (nsub - 1) - 0.5)
            if before_sample:
                before_sample(f + off)
            die.location, die.rotation_quaternion = motion(f + off)
            bpy.context.view_layer.update()
            sc.render.filepath = os.path.join(out, "f%03d_s%02d.png" % (f, i))
            bpy.ops.render.render(write_still=True)
            subs.append(shadow_params(sc, cam, die))
        meta["frames"].append({"f": f, "nsub": nsub, "deg": round(deg, 2), "subs": subs})
    json.dump(meta, open(os.path.join(out, "meta.json"), "w"))
    return sum(x["nsub"] for x in meta["frames"])


def anim_jobs(cfg):
    """(tag, frame count, motion factory) for every clip this die needs."""
    jobs = [("face%d" % n, cfg["roll_frames"],
             lambda values, n=n, nf=cfg["roll_frames"]:
                 (lambda f: face_motion(values, n, f, nf), None))
            for n in range(1, cfg["faces"] + 1)]
    for k, name in enumerate(cfg["idles"]):
        jobs.append((name, cfg["idle_frames"],
                     lambda values, k=k, nf=cfg["idle_frames"]:
                         (lambda f: idle_motion(values, k, f, nf),
                          (lambda f: set_palette_gradient(RAMP, f / nf)) if k == 1 else None)))
    return jobs


RAMP = None         # set by main(); the idle1 gradient hook needs it inside a lambda


def main():
    global RAMP
    sc, die, cam, values, ramp = build_scene()
    RAMP = ramp
    if bpy.context.window:
        bpy.context.window.scene = sc

    jobs = anim_jobs(CFG)
    if ONLY:
        known = {tag for tag, _, _ in jobs}
        unknown = [t for t in ONLY if t not in known]
        if unknown:
            raise SystemExit("DICE_ONLY names clips %s does not have: %s"
                             % (DIE, ", ".join(unknown)))
        jobs = [j for j in jobs if j[0] in ONLY]

    # value -> which way that face points, and how far it is spun to read upright.
    # Inverting this to key on the normal does not work: a Vector is unhashable.
    print("%s: %s" % (DIE, ", ".join(
        "%d@(%.2f,%.2f,%.2f)%+.0fdeg" % ((v,) + tuple(n) + (math.degrees(r),))
        for v, (n, r) in sorted(values.items()))))
    print("%s: rendering %d of %d clips -> %s"
          % (DIE, len(jobs), CFG["faces"] + len(CFG["idles"]), WORK))
    os.makedirs(WORK, exist_ok=True)
    total = 0
    for tag, nframes, factory in jobs:
        motion, hook = factory(values)
        t0 = time.time()
        total += render_anim(sc, cam, die, tag, nframes, motion, before_sample=hook)
        # One line per clip, flushed: this is the only progress a caller driving the
        # render from outside Blender gets to see.
        print("  %-7s %3d frames, %5d sub-frames so far, %.0fs"
              % (tag, nframes, total, time.time() - t0), flush=True)
    print("%d sub-frames -> %s" % (total, WORK))


if __name__ == "__main__":
    main()
