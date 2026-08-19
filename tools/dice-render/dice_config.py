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
    sheets_dir="assets/dice",
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
)

DICE = {
    "d6": dict(
        label="D6",
        source_object="D6 Dotted",      # object name inside the CC0 blend
        faces=6,
        sheet_prefix="dice",            # dice_1_sprites.png ... dice_idle1_sprites.png
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
        label="D20",
        source_object="D20",
        faces=20,
        sheet_prefix="d20",
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
}

# Dice in the pack that are not being rendered yet (ROADMAP 8a). Recorded so the face
# counts live in one place and nobody has to reopen the blend to look them up.
DEFERRED = {
    "d4": ("D4", 4), "d6n": ("D6 Numbered", 6), "d8": ("D8", 8),
    "d10": ("D10", 10), "d10p": ("D10 Percentile", 10), "d12": ("D12", 12),
}


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


def sheet_path(cfg, animation):
    """Repo-relative path of the spritesheet backing one animation."""
    return "%s/%s_%s_sprites.png" % (cfg["sheets_dir"], cfg["sheet_prefix"], animation)


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
