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

## VRAM compression — measured, August 2026, and it is the wrong trade

The idea was that ETC2/ASTC would cut texture memory fourfold. It does. It also multiplies
the download by four and a half, and the memory it saves is the problem that lazy loading
already solved.

One sheet (`d6/1_sprites.png`, 1280×1280, 520 KB) imported four ways, then measured:

| import mode | on disk | in memory | GPU format | PSNR |
|---|---|---|---|---|
| lossless (today) | 357 KB | 8.74 MB | RGBA8 | — |
| **lossy WebP** | **134 KB** | 8.74 MB | RGBA8 | 33.0 dB |
| VRAM compressed | 1,600 KB ×4 variants | **2.18 MB** | DXT5 | 30.4 dB |
| Basis Universal | 1,600 KB | **2.18 MB** | BPTC | 36.2 dB |

Extrapolated to the 92 sheets:

| | download | memory |
|---|---|---|
| lossless (today) | 33.4 MB | 727 MB |
| **lossy WebP** | **12.5 MB** | 727 MB |
| VRAM compressed | 149.5 MB | 182 MB |
| Basis Universal | 149.5 MB | 182 MB |

### Three things worth knowing

**The `.ctex` is already smaller than the PNG.** Lossless import re-encodes to 357 KB from a
520 KB source, so the real download today is **33 MB, not 48.6** — the figure quoted
everywhere else in this repository is the size of the *sources*, which do not ship.

**VRAM compression emits every format variant.** The import produced four files totalling
6.4 MB for one sheet: S3TC and BPTC for desktop, ETC2 and ASTC for mobile. An export picks
what its target needs, but a *web* export may need both desktop and mobile families, because
the same page runs on both.

**DXT5 is the worst option on this artwork specifically.** 30.4 dB, visible blotching across
the flat faces at 3× (`out/vram_quality.png`), and — the part that matters here — it damages
**alpha**, by up to 31 levels. This animation is mostly soft alpha: the motion blur *is* the
artwork. Basis/BPTC is near-indistinguishable at 36.2 dB and is the good version of the same
idea, but carries the same download.

### The recommendation

**Do not use VRAM compression.** It buys memory at four and a half times the download, and
memory stopped being the constraint when the pack went lazy — startup is 79 MB now and grows
only with the dice actually thrown.

**Use lossy WebP instead** if the download matters. That was the conclusion, it was then
applied to the whole pack, and the next section is what it actually did.

The one thing not measured: the desktop importer chose DXT5 and BPTC. A web or mobile target
would use ETC2 or ASTC, which could not be tested here. ETC2 is broadly DXT5-class and ASTC
is better, so the ranking is unlikely to change, but confirm before relying on it.

## If this is taken further

The re-render needs `tools/dice-render/` to emit, per die, one face-agnostic tumble for
frames 0–29 and then 76 tails that each *begin* from the pose that tumble ends on. That is
the "slewing tails" ROADMAP 8 describes, and the slew has to finish before the face becomes
readable — roughly frames 30–42 — or the die will visibly snap into its answer.

## Lossy WebP — applied to the pack, August 2026

`compress/mode=1` in the `.import` files. No re-render, no change to the sheets on disk, and
nothing in `scripts/` knows about it. Measured over all 92 sheets by summing the `.ctex`
files each `.import` points at:

| | download | startup memory |
|---|---|---|
| lossless, as it was | 32.62 MB | 79.1 MB |
| **lossy, as shipped** | **16.29 MB** | 79.1 MB |

**Half the download for nothing.** Memory does not move because the GPU format is `Rgba8`
either way — WebP is a container decision, not a texture one, which is exactly what makes it
different from VRAM compression.

### Quality does not fix the red pip, and that is why the d6 is exempt

At 12× the first all-lossy build showed real artefacts on the d6's pip: a dark halo, green
bleed above it, a mottled interior. Three qualities on `d6/5_sprites.png`, measured against
the lossless *decode* over visible pixels only:

| quality | KB | PSNR | mean error | pip mean | pip max |
|---|---|---|---|---|---|
| 0.70 | 141 | 33.5 dB | 3.90 | 18.79 | 64 |
| 0.85 | 163 | 36.8 dB | 2.64 | 17.63 | 53 |
| 0.95 | 218 | 41.3 dB | 1.43 | 16.66 | 52 |
| lossless | 362 | — | — | — | — |

The frame as a whole improves steadily — 33.5 dB to 41.3 dB — while **the pip barely moves**:
18.79 to 16.66 across a 55% size increase. Quality controls DCT precision; the pip's damage is
**chroma subsampling**, which WebP does regardless. There is no setting that fixes it.

So the pack is split rather than tuned: **quality 0.85 for the 84 sheets outside
`assets/dice/d6/`, and lossless for the eight inside it.** The d6 is the only die carrying a
small saturated feature. The exemption costs 3.4 MB against an all-lossy build and is a
targeted answer to a measured problem rather than a global setting chosen to survive its worst
case.

`idle1` is saturated too — it is the rainbow spin — but a large smooth sweep is what chroma
subsampling handles best, and it is unreadable by design. It is the small sharp spot on
neutral that is the worst case, not saturation as such.

### What it looks like in the game

`docs/screenshot.png` regenerated and diffed against the lossless build, per die:

| die | differing pixels | worst |
|---|---|---|
| d6 (kept lossless) | **0** | **0** |
| d4 | 521 | 48 |
| d20 | 581 | 41 |
| d10% | 559 | 39 |
| d8 | 443 | 37 |

2,301 pixels differ visibly across the whole 1152×648 frame, worst 48 of a possible 765 — down
from 5,281 and 125 at quality 0.70. The d6 being exactly zero is the check that the exemption
is actually in force. `out/lossy_ingame.png` has the 4× crops and an 8×-amplified difference.

### Re-rendering a die resets this

Godot writes a fresh `.import` with the default `compress/mode=0` for a PNG it has not seen
before, so **a newly added die imports lossless and silently costs the pack a megabyte or
two**. Overwriting an existing sheet — which is what `pipeline.py --install` does — keeps the
setting, so only new dice are affected. Check with:

```sh
grep -L "compress/mode=1" assets/dice/*/*_sprites.png.import
```

Anything listed that is not under `d6/` has been reset.
