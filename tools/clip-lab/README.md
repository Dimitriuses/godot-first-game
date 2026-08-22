# Clip lab

Prototypes and measurements for making the die animation smaller than its 48.6 MB.

```sh
python tools/clip-lab/analyse.py                      # the numbers
godot --headless --path . res://tools/clip-lab/clip_lab.tscn --quit-after 4000
```

`out/` holds the pictures the argument rests on and is gitignored; regenerate it from the
snippets in this file if it is missing.

## The idea being tested

Split each roll in two: a **blurred prefix** shared by every face, and a **per-face tail**
where the die settles and the answer becomes readable. 76 tails instead of 76 whole clips.
Optionally stretch the prefix at playback so a throw can last longer without more frames.

## What the measurements say

**1. The prefix cannot be shared without re-rendering.** The eight dice were rendered as
one independent trajectory per face — they start in different places, at different
orientations, and converge on a common resting pose. At frame 90 every face's centroid is
identical to a tenth of a pixel; at frame 0 they are spread 33 px apart.

Splicing one face's prefix onto another's tail therefore jumps. Measured as the size of the
step at the join against an ordinary frame step:

| splice at | ordinary step | spliced step | ratio |
|---|---|---|---|
| 30 | 8.47 | 16.92 | 2.0× |
| 40 | 10.26 | 17.75 | 1.7× |
| 50 | 6.21 | 12.80 | 2.1× |
| 60 | 3.42 | 8.67 | 2.5× |

1.0× would be invisible. `out/seam.png` shows what 2.0× looks like and it is not subtle —
the die changes orientation, position and apparent size in one frame. **This is a re-render,
not a repack.**

**2. The window where the face is genuinely hidden is shorter than it looks.** An automated
measure of blur — the share of the die's footprint that is soft edge — says the smear lasts
until frame 52. The pictures say otherwise: `out/readable.png` has countable pips by frame
42, and arguably by 36. The metric finds where *residual* blur ends, which is later than
where *readability* starts, and it was wrong to trust it. Read off the images:

- frames 4–30: a smear, no face identifiable;
- 30–36: borderline;
- 42 onward: plainly readable.

A shared prefix has to end while the die is still unreadable, or every roll briefly shows
the prefix's face before settling on a different one. **Handoff at frame 30** is what the
artwork supports.

**3. What each option actually costs.** Measured from the committed sheets at 5–7 KB/frame:

| option | size | saved |
|---|---|---|
| today | 48.6 MB | |
| shared prefix, handoff 30 | 35.3 MB | 27% |
| …prefix stored as 15 frames, stretched | 34.6 MB | 29% |
| …and tails cut 61 → 40 | **24.2 MB** | **50%** |
| whole clips cut 91 → 61, no redesign | 33.8 MB | 30% |

Two things fall out of that table, and they are the point of this whole exercise:

- **Stretching the prefix saves almost nothing.** There are eight prefixes and seventy-six
  tails; the prefix's length is not where the weight is. Stretch it for the *gameplay*
  reason — variable throw length — not to save space.
- **The redesign only beats the trivial option once the tails are shortened too.** Sharing
  the prefix at a safe handoff saves 27%; simply cutting every clip from 91 frames to 61
  saves 30%, needs no new machinery and no re-render. The redesign is worth it at 50%, and
  that number requires shortening the tails, which is a change to how the settle looks.

ROADMAP 8 estimated "shared prefix + slewing tails" at 27.5 MB / 37%. That was written
before the pack existed and assumes a handoff around frame 45–50 — later than the artwork
can hide. At a handoff the pictures support it is 35.3 MB / 27%.

## The playback prototype

`ClipLab.cs` plays a spliced two-part roll and stretches the prefix. It confirms the runtime
half works and costs nothing to build — switching clip mid-roll is the trick `Dice.Roll`
already uses to resume a tumble.

| stretch | prefix | tail | total |
|---|---|---|---|
| 0.50× | 0.36 s | 1.99 s | 2.35 s |
| 1.00× | 0.96 s | 2.00 s | 2.96 s |
| 1.75× | 1.69 s | 2.11 s | 3.81 s |
| 3.00× | 2.91 s | 2.01 s | 4.91 s |

One 30-frame prefix gives throws from 2.35 s to 4.91 s. The tail is never stretched: it is
the part with the answer in it, and slowing it down reads as the die hesitating.

The prototype's join is visibly wrong, because it splices clips that were never meant to
join — that is finding 1, not a bug in the prototype.

## Cutting 91 frames to 61 — tried, August 2026

`decimate.py` rebuilds a die's clips with fewer frames by *selecting* frames that already
exist. No Blender, no source model, no change to how the animation is drawn — which is what
makes it the cheap option, and why it was worth trying before the redesign.

```sh
python tools/clip-lab/decimate.py d6 --frames 61
```

**Real saving on the d6: 36%** (3,268 KB → 2,081 KB), against the 30% the model predicted.
The extra comes from the eased plan landing on 60 unique frames rather than 61.

### Which frames to drop is not obvious, and it matters

What a viewer sees each tick is the **step** — how much the picture changed since the last
frame. Dropping frames makes steps bigger, and where that hurts is not uniform: during the
blur every frame is a smear and a big step is invisible; during the settle the die is sharp
and the same step reads as a stutter. Mean step per region, d6:

| plan | frames | blur step | settle step | worst step | worst at |
|---|---|---|---|---|---|
| as shipped | 91 | 8.18 | 1.05 | 11.95 | f24 |
| evenly spaced | 61 | 9.35 | **1.70** | 17.14 | f24 |
| **eased** | 60 | 10.07 | **1.38** | 14.54 | f42 |

The eased plan spends its frames towards the end where they are seen and takes the saving
out of the blur where they are not: a fifth better on the settle for the same budget.

A sparse first half joined to a dense second half is *worse* than either, and was tried: an
abrupt change of rate puts the largest step exactly at the boundary, which is where the die
is starting to become readable. Hence a curve, not two rates.

### What it costs in feel

At 30 fps a 60-frame clip lasts **2.00 s** against 3.03 s. That is the real decision, and it
is a gameplay one rather than a technical one:

- **Keep 30 fps** and the throw is a third shorter. Steps are unchanged from the table
  above. Three seconds is a long time to wait for a die, so this may well be an improvement.
- **Stretch it back to 3.03 s** (playing at ~20 fps) and the throw feels as it does now, but
  every step grows by half again on top of the numbers above.

`out/cut61.gif` shows all three side by side at real speed — as shipped, cut, and cut but
stretched. Watch it before deciding; the numbers cannot settle this one.

### If it is adopted

Nothing in `scripts/` hardcodes 91 — `Dice` reads `SpriteFrames.GetFrameCount`, and so does
the screenshot tool. The work is: run `decimate.py` for all eight dice, move the sheets into
`assets/dice/`, and regenerate the scenes so the atlas regions match
(`tools/dice-render/make_scene.py`). `tools/dice-render/validate.py` checks a scene against
the sheets on disk and should be run afterwards.

## If this is taken further

The re-render needs `tools/dice-render/` to emit, per die, one face-agnostic tumble for
frames 0–29 and then 76 tails that each *begin* from the pose that tumble ends on. That is
the "slewing tails" ROADMAP 8 describes, and the slew has to finish before the face becomes
readable — roughly frames 30–42 — or the die will visibly snap into its answer.
