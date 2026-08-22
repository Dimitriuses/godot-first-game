"""What the die animation actually costs, and what the proposed redesigns would save.

    python tools/clip-lab/analyse.py

Reads the committed sheets in assets/dice/ and reports four things:

  1. what is stored today, in frames and megabytes, per die;
  2. where the motion blur ends — the frame after which a face is readable, which is
     the only sensible place to split a clip in two;
  3. whether the blurred half could be *shared* between faces without re-rendering;
  4. what each redesign would cost, in real megabytes.

Nothing here modifies anything. It exists so the size question can be answered with
measurements instead of estimates — the estimate in ROADMAP 8 was made before the pack
was rendered and is worth checking against this.
"""

import glob
import os

import numpy as np
from PIL import Image

DICE = ["d4", "d6", "d6n", "d8", "d10", "d10p", "d12", "d20"]
ROLL_FRAMES = 91
IDLE_FRAMES = 30
IDLE_CLIPS = 2
CELL = 128
ROOT = os.path.join(os.path.dirname(__file__), "..", "..")


def sheet(die, clip):
    return os.path.normpath(os.path.join(ROOT, "assets", "dice", die,
                                         f"{clip}_sprites.png"))


def cells(path, n=ROLL_FRAMES):
    """The n frames of one sheet, as separate arrays."""
    a = np.asarray(Image.open(path).convert("RGBA"))
    cols = a.shape[1] // CELL
    return [a[(i // cols) * CELL:(i // cols) * CELL + CELL,
              (i % cols) * CELL:(i % cols) * CELL + CELL] for i in range(n)]


def faces_of(die):
    found = []
    for path in sorted(glob.glob(os.path.join(os.path.dirname(sheet(die, 1)),
                                              "*_sprites.png"))):
        name = os.path.basename(path).split("_")[0]
        if name.isdigit():
            found.append(int(name))
    return sorted(found)


# ---------------------------------------------------------------- what is stored

def inventory():
    rows = []
    for die in DICE:
        faces = faces_of(die)
        roll = sum(os.path.getsize(sheet(die, f)) for f in faces)
        idle = sum(os.path.getsize(sheet(die, f"idle{i}")) for i in range(IDLE_CLIPS))
        rows.append({
            "die": die,
            "faces": len(faces),
            "roll_bytes": roll,
            "idle_bytes": idle,
            "roll_frames": len(faces) * ROLL_FRAMES,
            "idle_frames": IDLE_CLIPS * IDLE_FRAMES,
            "kb_per_frame": roll / (len(faces) * ROLL_FRAMES) / 1024,
        })
    return rows


# ------------------------------------------------------------- where the blur ends

def softness(cell):
    """How much of the die's footprint is a soft edge. Motion blur is mostly edge."""
    alpha = cell[:, :, 3]
    solid = int((alpha > 250).sum())
    edge = int(((alpha > 20) & (alpha <= 250)).sum())
    return edge / max(1, solid + edge)


def handoff(die, settle=1.5):
    """The first frame after which the die is no longer visibly smeared.

    Measured against the clip's own resting frame rather than an absolute threshold,
    because a d20 at rest has far more edge than a d6 does — it has more facets.
    """
    curve = [softness(c) for c in cells(sheet(die, faces_of(die)[0]))]
    rest = curve[-1]
    for i in range(ROLL_FRAMES - 1, -1, -1):
        if curve[i] > rest * settle:
            return i + 1, rest
    return 0, rest


# ------------------------------------------- can the blurred half be shared as-is?

def seam_cost(die, at):
    """How a spliced frame step compares with an ordinary one, as a ratio.

    Splicing means playing face A's blurred prefix and then face B's tail. If the
    resulting step at the join looks like any other step, nobody can see it; 1.0 is
    perfect and anything much above that is a visible hitch.
    """
    faces = faces_of(die)
    clips = [np.asarray(cells(sheet(die, f)), dtype=np.int16) for f in faces]
    natural = np.mean([np.abs(c[at] - c[at - 1]).mean() for c in clips])
    spliced = np.mean([np.abs(clips[k][at] - clips[0][at - 1]).mean()
                       for k in range(1, len(clips))])
    return spliced / natural, natural, spliced


# ------------------------------------------------------------------- the size model

def model(rows, prefix_at, stored_prefix=None, tail_frames=None):
    """Bytes for one redesign.

    prefix_at      the frame the blurred, shared part runs up to
    stored_prefix  how many frames of it are actually stored, if it is stretched over
                   the rest at playback time; None means store all of them
    tail_frames    how long each per-face tail is; None means whatever is left
    """
    total = 0
    for r in rows:
        kb = r["kb_per_frame"] * 1024
        prefix = stored_prefix if stored_prefix else prefix_at
        tail = tail_frames if tail_frames else ROLL_FRAMES - prefix_at
        total += kb * (prefix + r["faces"] * tail) + r["idle_bytes"]
    return total


def main():
    rows = inventory()
    today = sum(r["roll_bytes"] + r["idle_bytes"] for r in rows)
    frames_today = sum(r["roll_frames"] + r["idle_frames"] for r in rows)

    print("=" * 74)
    print("WHAT IS STORED TODAY")
    print("=" * 74)
    print(f"{'die':6}{'faces':>7}{'roll frames':>13}{'MB':>9}{'KB/frame':>11}")
    for r in rows:
        print(f"{r['die']:6}{r['faces']:>7}{r['roll_frames']:>13}"
              f"{(r['roll_bytes'] + r['idle_bytes']) / 1e6:>9.2f}"
              f"{r['kb_per_frame']:>11.1f}")
    print(f"{'all':6}{sum(r['faces'] for r in rows):>7}{frames_today:>13}"
          f"{today / 1e6:>9.2f}")

    print()
    print("=" * 74)
    print("HOW LONG IS THE DIE UNREADABLE?")
    print("=" * 74)
    print("Softness is the share of the die that is soft edge — a proxy for smear.")
    print("It says when blur *ends*, which is later than when the face becomes")
    print("readable: at frame 48 a d6's pips are already countable while this metric")
    print("still calls it blurred. Trust out/readable.png over this column.")
    print()
    print(f"{'frame':>6}{'d6 softness':>14}{'d20 softness':>15}")
    curves = {d: [softness(c) for c in cells(sheet(d, faces_of(d)[0]))]
              for d in ["d6", "d20"]}
    for i in range(0, 91, 6):
        print(f"{i:>6}{curves['d6'][i] * 100:>13.0f}%{curves['d20'][i] * 100:>14.0f}%")
    print()
    print("  Read off the pictures instead: the smear is total from about frame 4 to")
    print("  frame 30, borderline to 36, and the face is plainly readable by 42.")
    print("  A shared prefix has to end while the die is still unreadable, or every")
    print("  roll shows the prefix's face before settling on a different one.")

    print()
    print("=" * 74)
    print("COULD THE BLURRED PART BE SHARED WITHOUT RE-RENDERING?")
    print("=" * 74)
    print(f"{'splice at':>10}{'ordinary step':>15}{'spliced step':>14}{'ratio':>8}")
    for at in [30, 40, 50, 60, 70]:
        ratio, natural, spliced = seam_cost("d6", at)
        print(f"{at:>10}{natural:>15.2f}{spliced:>14.2f}{ratio:>7.1f}x")
    print("\n  1.0x would be invisible. These are 2x and up, so the join is a real")
    print("  step — the clips are separate trajectories, not one tumble with")
    print("  different endings. Sharing needs a re-render; see the note in the README.")

    print()
    print("=" * 74)
    print("WHAT EACH REDESIGN WOULD COST")
    print("=" * 74)
    print(f"{'handoff':>8}{'prefix':>8}{'tail':>6}{'shared':>9}{'+stretch':>10}"
          f"{'+short tails':>14}")
    for at in [24, 30, 36, 45, 52]:
        shared = model(rows, at)
        stretched = model(rows, at, stored_prefix=15)
        short = model(rows, at, stored_prefix=15,
                      tail_frames=max(12, (ROLL_FRAMES - at) * 2 // 3))
        print(f"{at:>8}{at:>8}{ROLL_FRAMES - at:>6}"
              f"{shared / 1e6:>8.1f}M{stretched / 1e6:>9.1f}M{short / 1e6:>13.1f}M")
    print()
    print(f"{'today':>8}{'':>8}{'':>6}{today / 1e6:>8.1f}M")
    cut = model(rows, 0, stored_prefix=0, tail_frames=61)
    print(f"{'91->61':>8}{'':>8}{'':>6}{cut / 1e6:>8.1f}M   "
          f"({100 * (1 - cut / today):.0f}% off, no redesign, loses a third of the tumble)")
    print()
    print("Percentages off today, for the handoff the pictures support (30):")
    for label, size in [("shared prefix", model(rows, 30)),
                        ("+ prefix stretched from 15", model(rows, 30, stored_prefix=15)),
                        ("+ tails 61 -> 40", model(rows, 30, stored_prefix=15,
                                                   tail_frames=40))]:
        print(f"  {label:34}{size / 1e6:>7.1f}M{100 * (1 - size / today):>7.0f}%")

    print()
    print("The prefix is shared by faces, so there are only eight of them however long")
    print("they are: stretching it saves little. The per-face tails are 76 clips and are")
    print("where the weight is — shortening those is what moves the number.")


if __name__ == "__main__":
    main()
