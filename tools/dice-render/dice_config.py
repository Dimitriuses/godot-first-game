"""One table describing each die in the pack.

Everything that differs between dice lives here, so adding the d10 later is an entry
plus a render run rather than another pass through the pipeline. Read by
`make_scene.py` today and by `render.py` once the geometry pass is generalised
(ROADMAP 8c).

Only the dice actually rendered have their sheets on disk; the rest are here so the
shape of an entry is obvious and so face counts are recorded in one place.
"""
import math
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

# Shared by every die. A die entry may override any of these.
DEFAULTS = dict(
    sheets_root="assets/dice",      # each die gets a subdirectory of this
    script="res://scripts/Dice.cs",

    cell=128,               # sprite cell, pixels
    cols=10,                # cells per sheet row
    fps=30.0,
    roll_frames=91,         # one landing clip per face
    idle_frames=30,         # the two looping tumbles
    idles=("idle0", "idle1"),

    # RigidBody2D setup, mirroring what dice.tscn has always carried
    collider_radius=32.0,
    collider_offset=(1, 12),    # lines the collider up with the drawn die
    bounce=0.6,
    absorbent=True,
    gravity_scale=0.0,
    linear_damp=1.5,
    freeze_mode=1,              # Kinematic
    center_of_mass_mode=1,      # Custom, pinned to the origin
    continuous_cd=1,            # CCD_MODE_CAST_RAY; see KNOWNISSUES 4
    input_pickable=True,

    # Rendering. `red_value` tints one face's glyph red; None leaves the die
    # monochrome. On the d6 that is the lone pip and always has been -- on a
    # numbered die it is a design choice, so it is a setting rather than a rule.
    # `DICE_RED=20` (or `DICE_RED=none`) overrides it for one render without
    # editing the table; see `red_for()`.
    red_value=None,

    # Which face each idle loop spins about. Any face does, since the loop is a
    # whole number of turns about a fixed axis -- but a d4 has no face 5, so it
    # is clamped rather than assumed.
    idle_bases=(1, 5),

    # Per-face throw variation, so the landing clips do not read as one clip
    # played n times: {face: dict(turns, a0, a1, drift)}. None generates them
    # (see `throw_params` in render.py), which is what any new die should use.
    face_throws=None,

    # Whether a value's face points away from the camera when the die rests on it.
    # True only for the d4, which has no parallel faces to stand on.
    rest_face_down=False,

    # Corners per face, which is how many distinct `face_twists` a face has. Measured
    # off the rim when None, which is right for every regular solid in the pack; the
    # override is here for a face that has no rotational symmetry to measure, like the
    # d10's kites.
    face_symmetry=None,

    # Whether the die is printed 0..n-1 rather than 1..n, as a d10 is. Values are still
    # stored 1..n -- the game needs a clip per face and `Dice.cs` names them 1..n -- so
    # the top face holds n and is printed as 0, which is the usual reading of a d10.
    # Only the opposite-faces check cares, and it compares what is printed.
    zero_based=False,

    # What one face is worth when reported. 10 on a percentile d10, which is printed in
    # tens; 1 everywhere else. The stored value stays 1..n either way, because the game
    # plays a clip per face and names them 1..n.
    value_step=1,

    # How many twists to offer per face. Defaults to the face's symmetry, which is the
    # right number when the ways up are equivalent. A face with no symmetry -- the d10's
    # kites -- has a free angle instead, so it quantises it finely and records a step
    # per face. `face_symmetry` still fixes the reference the steps are measured from.
    twist_steps=None,

    # Whether the reference direction needs its m-fold ambiguity resolved. Off for a
    # face with real rotational symmetry, where the ambiguity is the symmetry and the
    # twists cover it. On for one without -- see `resolved_corner_angle`.
    resolve_axis=False,

    # A constant rotation added to every face's reference, in degrees. The reference is
    # geometry; this is where the artwork sits relative to it, which no amount of
    # measuring the solid can tell you.
    twist_offset_deg=0.0,
)

DICE = {
    "d6": dict(
        label="d6",
        source_object="D6 Dotted",      # object name inside the CC0 blend
        faces=6,
        scene="scenes/dice.tscn",
        # game.tscn refers to this scene by uid, so it must be preserved
        scene_uid="uid://ct6xq3adgo4q7",
        face_values=None,               # pipped: counted off the geometry
        face_twists=None,               # pips need no particular way up
        red_value=1,                    # the lone pip has always been red

        # Pinned, not generated. These six are the numbers that produced the
        # artwork in assets/dice/, so the generator is not allowed to drift them
        # -- a re-render has to come out matching what is committed.
        face_throws={
            1: dict(turns=7.0, a0=( 0.35, 0.75, 0.55), a1=( 0.10, 0.30, 0.95), drift=(-0.38,  0.26)),
            2: dict(turns=6.2, a0=(-0.60, 0.55, 0.58), a1=(-0.15, 0.15, 0.98), drift=( 0.34,  0.22)),
            3: dict(turns=7.6, a0=( 0.70,-0.45, 0.55), a1=( 0.25,-0.10, 0.96), drift=(-0.28, -0.32)),
            4: dict(turns=6.6, a0=(-0.45,-0.70, 0.52), a1=(-0.05,-0.25, 0.97), drift=( 0.40, -0.18)),
            5: dict(turns=7.9, a0=( 0.55, 0.62,-0.56), a1=( 0.18, 0.20, 0.94), drift=(-0.20,  0.38)),
            6: dict(turns=6.9, a0=(-0.68, 0.40,-0.60), a1=(-0.20, 0.12, 0.97), drift=( 0.26,  0.34)),
        },
    ),
    "d20": dict(
        label="d20",
        source_object="D20",
        faces=20,
        scene="scenes/d20.tscn",
        # Minted with ResourceUID.create_id() rather than left to Godot: a scene
        # generated outside the editor never gets one, and game.tscn refers to this
        # one by uid, so it has to be stable across regenerations.
        scene_uid="uid://c0v46k6vajc7y",

        # Which value each face carries, indexed by the order face_planes() returns.
        # You cannot count a glyph, so this was read once off the contact sheet from
        # `face_sheet.py` and cross-checked: all ten opposite pairs sum to 21, and each
        # of 1..20 appears exactly once.
        face_values=[1, 13, 19, 7, 11, 5, 15, 3, 17, 9,
                     4, 18, 16, 12, 10, 6, 14, 2, 8, 20],

        # Which of the triangle's three corners points to screen-up so that the numeral
        # reads the right way round. Bringing a face normal to +Z leaves the spin about
        # that axis free, and on a numbered die that spin is not free at all -- get it
        # wrong and the die shows numbers lying on their side. Read off the same sheet.
        face_twists=[1, 1, 1, 1, 1, 0, 1, 1, 0, 0,
                     0, 2, 0, 0, 2, 2, 2, 2, 2, 0],

        # No red numeral: `red_value` defaults to None. Tinting the 1 is a d6 thing;
        # on a d20 either the 1 or the 20 would be a defensible choice, so it is left
        # off rather than invented. `DICE_RED=20` renders a variant to look at.
    ),
    "d6n": dict(
        # Distinct from the dotted d6's "d6": both have six faces, so the palette and
        # the die list would name them identically and offer no way to tell which row
        # or which drawer entry is which.
        label="d6 num",
        source_object="D6 Numbered",
        faces=6,
        scene="scenes/d6n.tscn",
        scene_uid="uid://bkk1sn4g0pswk",
        # Read off face_sheet.py. The face order happens to run 1..6, which the
        # opposite-faces-sum-to-7 check confirms rather than assumes.
        face_values=[1, 2, 3, 4, 5, 6],

        # Quarter turns, one per face, read off `face_sheet.py --twists`. Square faces,
        # so 0..3. Indexed by face, and face k carries value k+1 here, so this is also
        # the per-value list: 1 and 4 need three quarter turns, 2 and 3 one, 5 and 6
        # none. The 6 is the face with two glyph clusters -- it is underdotted, which is
        # how this model tells a 6 from a 9.
        face_twists=[3, 1, 1, 3, 0, 0],
        red_value=None,

        # Smaller than the dotted d6 despite being the same solid, and there is no
        # choice about it. A numeral is upright at exactly one rotation about the
        # vertical, which pins this cube face-forward, where the pipped d6 -- whose
        # pips have no way up, so whose rotation is free -- stands corner-forward. A
        # cube is 1.4x wider across its corners than across its faces. All four twists
        # give the same silhouette, so no other twist recovers the size; only tilting
        # every numeral 45 degrees would, which is worse.
        collider_radius=28.0,
        collider_offset=(0, 12),
    ),
    "d8": dict(
        label="d8",
        source_object="D8",
        faces=8,
        scene="scenes/d8.tscn",
        scene_uid="uid://cnitnj3gcgwec",
        # Read off face_sheet.py. Machine-checked from there: the four opposite pairs
        # each sum to 9 and every value appears once. The 6 is the face with two glyph
        # clusters -- underdotted, which is how this model tells a 6 from a 9, and the
        # only reason to be sure it is not a 9 on a die that has no 9.
        face_values=[1, 4, 3, 7, 2, 6, 5, 8],
        # Thirds of a turn, one per face, read off `face_sheet.py --twists`. Indexed by
        # face, so read alongside face_values above: values 1, 2, 3 and 4 want two
        # thirds, 5 and 7 one, 6 and 8 none.
        face_twists=[2, 2, 2, 1, 2, 0, 1, 0],

        # Measured off the finished sheets with `pipeline.py d8 --collider`, which is
        # a pixel under the shared default: an octahedron is close enough to spherical
        # that the silhouette rule lands it at 58x60 against the d6's 58x62.
        collider_radius=31.0,
        collider_offset=(0, 12),
    ),
    "d10": dict(
        label="d10",
        source_object="D10",
        faces=10,
        scene="scenes/d10.tscn",
        scene_uid="uid://ba1dvbo1x4a82",

        # Printed 0..9, not 1..10. The face showing 0 is stored as value 10 and its clip
        # is named "10", which is the ordinary reading of a d10 and the only one that
        # fits a game whose clips run 1..n.
        zero_based=True,

        # Read off face_sheet.py, and machine-checked: face k is opposite face 9-k, and
        # all five pairs of printed digits sum to 9.
        face_values=[9, 5, 1, 3, 7, 2, 6, 8, 4, 10],

        # A kite has no rotational symmetry at all -- only a mirror line -- so there is
        # no set of equivalent ways up to choose between, and `face_symmetry` measures
        # nothing above 0.5 and says so rather than guessing. Two is not a symmetry here
        # but it is the right *reference*: the two-fold moment of the rim picks out the
        # kite's long axis, which is stable from face to face. The twist itself is a
        # free angle, quantised to twelve steps of 30 degrees.
        face_symmetry=2,
        resolve_axis=True,

        # One way up, not two: with the axis resolved into a direction every face wants
        # the same rotation, so there is nothing to choose per face and nothing to
        # misread. The angle itself is where this die's numerals sit relative to their
        # kite, measured once off the twist sweep.
        twist_steps=1,
        twist_offset_deg=135.0,
        face_twists=[0] * 10,

        # Measured with `pipeline.py d10 --collider`. Wider than it is tall, unlike
        # every other die here: a trapezohedron at rest is a squat barrel.
        collider_radius=33.0,
        collider_offset=(0, 11),
    ),
    "d10p": dict(
        label="d10 %",
        source_object="D10 Percentile",
        faces=10,
        scene="scenes/d10p.tscn",
        scene_uid="uid://cafekvnoni3m5",

        # Shown as 00, 10, 20 ... 90, and stored as 1..10 with the 00 face holding 10 --
        # the same reading that makes a plain d10's 0 a ten. `value_step` is what turns
        # the stored number into the one the game reports.
        #
        # Not `zero_based`, despite the printed 00: that flag is about how the opposite
        # faces check out, and this die's arrangement satisfies the ordinary rule. Its
        # pairs of printed values all sum to 110 counting 00 as 100, which is 11 in the
        # stored numbering. The plain d10 is the one arranged the other way.
        value_step=10,
        face_values=[10, 6, 2, 4, 8, 3, 7, 9, 5, 1],

        # A kite, like the plain d10, and lettered the same way -- so the same reference
        # and the same offset. Confirmed on the rest sheet rather than assumed.
        face_symmetry=2,
        resolve_axis=True,
        twist_steps=1,
        twist_offset_deg=135.0,
        face_twists=[0] * 10,

        # The same solid as the plain d10, and it measures the same.
        collider_radius=33.0,
        collider_offset=(0, 11),
    ),
    "d12": dict(
        label="d12",
        source_object="D12",
        faces=12,
        scene="scenes/d12.tscn",
        scene_uid="uid://dmyfqtt7npvad",
        # Read off face_sheet.py, and it is the identity -- the face order `face_planes`
        # returns happens to run 1..12 here. Machine-checked all the same: the six
        # opposite pairs sum to 13 and every value appears once. The five faces with two
        # glyph clusters are exactly 6 and 9 (underdotted) plus the two-digit 10, 11 and
        # 12, which corroborates the reading independently.
        face_values=list(range(1, 13)),
        # Fifths of a turn, read off `face_sheet.py --twists`. Indexed by face, and the
        # face order is the identity here, so this is also the per-value list.
        face_twists=[1, 0, 0, 0, 1, 0, 3, 1, 3, 3, 3, 1],

        # Measured with `pipeline.py d12 --collider`; the radius lands on the shared
        # default, the offset a pixel above it.
        collider_radius=32.0,
        collider_offset=(0, 11),
    ),
    "d4": dict(
        label="d4",
        source_object="D4",
        faces=4,
        scene="scenes/d4.tscn",
        scene_uid="uid://ckuwfsdjywlmh",

        # Bigger than the other dice, and correctly so: `presentation_scale` equalises
        # the *mean* silhouette over all orientations, and a tetrahedron is the least
        # spherical solid in the pack, so matching on average leaves it 70px across at
        # rest against the d6's 58. The collider follows the drawn die rather than the
        # rule -- measured half-extent 34.5px, centred 3px left and 16px down of the
        # sprite's middle.
        collider_radius=36.0,
        collider_offset=(-3, 14),

        # A tetrahedron has no parallel faces, so it cannot rest with one face up: it
        # sits on a face with a vertex at the top, and the number is read at that apex.
        # `rest_quat` therefore has to bring the chosen face DOWN rather than up.
        rest_face_down=True,

        # Read off face_sheet.py. Each face carries three numerals and omits one, and
        # the omitted one is what the die shows when it lands on that face -- true for
        # both d4 conventions, since a face's own number is written on its neighbours
        # and a vertex's number is written on the faces around it, not on the face
        # opposite. Faces 0..3 carry {1,2,3}, {1,3,4}, {1,2,4}, {2,3,4}.
        #
        # There is no opposite-faces check to lean on here: a tetrahedron has no
        # parallel faces. What does hold is that each value appears on exactly three
        # faces, which this reading satisfies. Read it again if the geometry pass ever
        # changes -- an earlier version centred the die on its bounding box, which put
        # the face radii out and mixed up which glyph belonged to which face.
        face_values=[4, 2, 3, 1],
        face_twists=[0, 0, 0, 0],           # thirds of a turn; triangular faces
        red_value=None,
    ),
}

# Nothing is deferred any more: every solid in the pack is rendered. Kept as the place
# to record a die that is known about but not built, if the pack ever grows.
DEFERRED = {}


def die(name):
    """Full configuration for one die, defaults filled in."""
    if name not in DICE:
        raise KeyError("unknown die %r; known: %s" % (name, ", ".join(sorted(DICE))))
    cfg = dict(DEFAULTS)
    cfg.update(DICE[name])
    cfg["name"] = name
    return cfg


def animations(cfg):
    """(animation name, frame count, loops) for every clip the die needs, in order."""
    out = [(str(v), cfg["roll_frames"], False) for v in range(1, cfg["faces"] + 1)]
    out += [(idle, cfg["idle_frames"], True) for idle in cfg["idles"]]
    return out


def sheets_dir(cfg):
    """Repo-relative directory holding one die's spritesheets.

    A directory per die rather than one flat pile with a prefix on every filename:
    forty-four sheets in one folder is hard to look at, and the prefix said nothing the
    folder does not.
    """
    return "%s/%s" % (cfg["sheets_root"], cfg["name"])


def sheet_path(cfg, animation):
    """Repo-relative path of the spritesheet backing one animation."""
    return "%s/%s_sprites.png" % (sheets_dir(cfg), animation)


def resource_uid(repo_relative):
    """The uid Godot assigned to a resource, or None if it has not imported it yet.

    Textures keep it in the sidecar `.import`; scripts in a `.uid` file next to them.
    """
    full = os.path.join(ROOT, repo_relative.replace("res://", "").replace("/", os.sep))
    imp = full + ".import"
    if os.path.exists(imp):
        m = re.search(r'^uid="(uid://[^"]+)"', open(imp, encoding="utf-8").read(), re.M)
        if m:
            return m.group(1)
    side = full + ".uid"
    if os.path.exists(side):
        text = open(side, encoding="utf-8").read().strip()
        if text.startswith("uid://"):
            return text
    return None


def work_dir(name):
    """Where one die's intermediates live: `build/<die>/`, or `$DICE_WORK/<die>/`.

    Always a per-die subdirectory. The d6 and the d20 both write `faces/face1/`, so
    sharing one directory means the second render silently eats the first.
    """
    base = os.environ.get("DICE_WORK") or os.path.join(HERE, "build")
    return os.path.join(base, name)


def red_for(cfg):
    """Which face gets the red glyph, honouring a `DICE_RED` override.

    `DICE_RED=20` tints the 20, `DICE_RED=none` tints nothing. The point is to be able
    to look at a variant without editing the table and forgetting to put it back.
    """
    raw = os.environ.get("DICE_RED")
    if raw is None:
        return cfg.get("red_value")
    raw = raw.strip().lower()
    if raw in ("", "none", "off", "no", "0"):
        return None
    try:
        value = int(raw)
    except ValueError:
        raise SystemExit("DICE_RED must be a face value or 'none', not %r" % raw)
    if not 1 <= value <= cfg["faces"]:
        raise SystemExit("DICE_RED=%d is outside 1..%d" % (value, cfg["faces"]))
    return value


# Low-discrepancy constants: successive multiples of an irrational, taken mod 1, spread
# more evenly than random draws do. phi's conjugate and the plastic number's, so the two
# sequences do not lock into step with each other.
_PHI, _PSI = 0.6180339887498949, 0.7548776662466927


def throw_params(face, cfg):
    """Per-face throw variation: turn count, tumble axes and landing drift.

    A die's landing clips must not read as one clip played n times, so each face gets
    its own spin. Twenty of these are too many to tune by hand, and random draws clump,
    so they are spread over the same ranges the d6's hand-picked six occupy. A die that
    wants specific numbers -- the d6, whose artwork is already shipped -- pins them with
    `face_throws` instead.
    """
    table = cfg.get("face_throws")
    if table:
        if face not in table:
            raise SystemExit("%s has a face_throws table with no entry for %d"
                             % (cfg["name"], face))
        return table[face]

    u = ((face - 1) * _PHI) % 1.0
    th = 2 * math.pi * (((face - 1) * _PSI) % 1.0)
    tilt = -1.0 if face % 2 == 0 else 1.0
    return dict(
        turns=6.2 + 1.7 * u,                                    # the d6 spans 6.2--7.9
        a0=(0.68 * math.cos(th), 0.68 * math.sin(th), 0.56 * tilt),
        a1=(0.22 * math.cos(th), 0.22 * math.sin(th), 0.96),    # nearly upright by landing
        drift=(0.40 * math.cos(th + 2.4), 0.40 * math.sin(th + 2.4)),
    )


def idle_bases(cfg):
    """The resting face each idle loop spins about, clamped to faces the die has."""
    return tuple(min(b, cfg["faces"]) for b in cfg["idle_bases"])
