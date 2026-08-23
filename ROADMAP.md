# Roadmap

This started as a first look at Godot 4 and stopped at "the die works". The board underneath
it was drawn for a board game that was never wired up — and **as of August 2026 it never will
be.** The project is a dice-rolling sandbox: throw dice, watch them tumble, read the numbers.
No players, no turns, no win condition. Items 3 and 4 are dropped for that reason. Item 8,
adding the rest of the dice from the source pack, is **done** — all eight ship. One large
piece of work remains, planned rather than started: **item 9**, getting a playable build onto
GitHub Pages. What stands in its way is listed in
[KNOWNISSUES](KNOWNISSUES.md) section 9, and the payload question there wants deciding before
anyone starts.

## 1. Make the physics decide the number — partly addressed, still open

**Not done as written.** The number no longer comes from a bare `System.Random` draw, but it
is still not read off the die's physical state.

### What it does now

`Dice.Roll()` takes the number from **the frame of the idle tumble the die was let go on**,
then nudges it with a random offset (`ResultJitter`, default 2). The idle loop is 30 frames
and covers all six faces across its length, so the moment of release picks a base face and
the jitter decides how close to it the throw actually lands. The roll clip also **resumes
from that same frame**, so the spin the player was watching carries into the throw instead of
cutting to the start of a clip.

The effect is that a throw can be influenced but not aimed, and the number is tied to
something the player can see rather than to an invisible draw. It stays fair: 12,000 rolls
sweeping the release frame evenly landed between 1,953 and 2,049 per face against an expected
2,000. A die rolled from rest has no release frame and falls back to a plain draw.

### Why the full version was reverted

A complete implementation was built, tested and then deliberately removed. The problem is the
scene: the board is seen from directly above with gravity off, so a die *sliding* across it
would never change which face is up — screen-plane rotation cannot turn a face toward the
camera. Making the physics decide therefore meant simulating a 3D orientation and rolling it
forward from the distance travelled, as a cube crossing a table does.

That worked, and the pure orientation maths passed nine invariant tests. It also meant the
landing clip could not be chosen until the die stopped, which cost the six 91-frame roll
animations most of their screen time. It was more machinery than this toy warrants, so it was
taken out again. If it is ever wanted back, the shape of the answer is recorded here.

### What is still missing

The reported number has no connection to where the die physically ends up. Reading it off the
die's real state remains the honest fix, and the note above is the reason it is not cheap.

## 2. Show the result — ✅ done, August 2026

The result reaches the screen. `DiceHud` (built in code, not in the scene) lists every die
on the board with its current value and a running **Total**, and `GameManager.OnDiceRolled`
updates it from the `DiceRolled` signal — so an exported build now reports something.

It was **not** done the way this item originally proposed. The plan was to bind the spare
`Label` already sitting in `game.tscn`; instead the HUD grew out of the multi-dice work,
because one label cannot show eight dice. **That `Label` is still there and still unused** —
a `Label` node at (1075, 582) that no code fetches. Either delete it or repurpose it; right
now it is dead scene furniture.

**Hovering a die names its number**, in a small tag beside the cursor, added once the d20
shipped (item 8). It is not decoration: a d20's up-face is the smallest face on the die and
the camera sees it at 31 degrees, so reading a 17 off the sprite is a squint where reading a
d6's pips is not. The tag hides while the die is held and for the whole of a roll — the
result is decided the moment a throw starts, and showing it before the clip lands would give
the throw away.

Still missing, and worth a follow-up: the value shown is derived from the release frame of
the tumble, not read off where the die physically stops. Item 1 is what makes the number
mean anything; this item only makes it visible.

## 3. ~~Finish the board game the board implies~~ — dropped, August 2026

**Decided against.** The board art implies a game, and the scaffolding for one survives, but
building it would turn a dice sandbox into a different project. The dice are the interesting
part and the part that works; a ring of cells and a token moving round it adds rules without
adding anything to look at.

What is still in the tree because of this, and can now be deleted whenever someone wants to:

- `scripts/Player.cs` — a `Node2D` with a `CellIndex` and a `MoveToCell(Vector2)`, never
  instantiated by anything.
- `scenes/Player.tscn` — the piece, using a Kenney black playing piece.
- `assets/kenney-boardgame/piece-black-border04.png`, used only by that scene, and
  `chip-red-white.png`, now used by nothing at all.

Preserved in `v0.1-original` and not worth restoring: a `cellPositions` array of eight
`Vector2`s in `GameManager` and a commented-out `_Ready` block that would have spawned the
player on the first cell. Its coordinates never matched the drawn board anyway — the eight
cells form a ring inside a 100–300 px box, while the board spans roughly 1150 × 670 px.

## 4. ~~Turn order and a second player~~ — dropped with item 3

Nothing was ever written for it, and with no board game there is nothing for it to sequence.

## 5. A browser demo — moved to item 9

It grew from "an option we cannot take" into the largest single piece of work left, so it
lives at the end of the file now rather than in the middle. The route is settled: a
hand-written GDScript port in a second tree, after item 8.

## 6. Regenerate the die animation from scratch — ✅ done, August 2026

The die animation was the one asset the project did not have clean rights to. It has been
re-rendered from **Blend Swap blend #82440 (CC0)** in Blender, and the frames are now
covered by this repository's MIT licence. Full method in [docs/ASSETS.md](docs/ASSETS.md).

It was a re-render, not a substitution — dropping in a flat CC0 dice sprite would have
settled the licensing and thrown away the visual appeal in the same move. What it kept:

- a **3D look** — a real cube tumbling in 3D rather than a flat icon spinning;
- **artistically exaggerated rotation**, decaying over 66 frames into a damped settle,
  with motion blur accumulated from up to 20 renders per frame so the fast part smears
  instead of strobing;
- the existing structure exactly — six landing animations (`1`–`6`) of 91 frames plus two
  30-frame idle loops (`idle0`, `idle1`), all at 30 fps, so **`Dice.cs` did not change.**

The resolution problem went with it: 128 px cells drawn at 1:1 instead of 512 px cells drawn
at 128 px. **3.9 MB instead of 18.6 MB**, and genuinely antialiased for the first time — the
old frames came out of a GIF and had binary alpha.

### 6b. The other dice

Moved to item 8 below, and no longer described as "the cheap part" — measuring it changed the
picture.

## 7. Stop the die tunnelling through the walls — ✅ done, August 2026

One line: `continuous_cd = 1` (`CCD_MODE_CAST_RAY`) on the `RigidBody2D` in `dice.tscn`. It was
the first thing this item suggested trying and it was the whole answer.

Measured by firing dice at all four walls and all four corners with the recovery disabled:
the untreated body starts leaking at **8,000 px/s** and is losing 7 of 8 throws by 30,000.
With `CastRay` it is **0 escapes at every speed up to 400,000 px/s**, and ordinary play is
unchanged to the pixel — the same throw reaches the same wall contact point and rests in the
same place with CCD on or off.

Three findings worth keeping, since they contradict what this item assumed:

- **`CastShape` does nothing here.** The more expensive CCD mode measured identically to no CCD
  at all. Measure before reaching for it.
- **Raising the tick rate is not a fix.** 120 Hz buys about one speed bracket, then fails the
  same way.
- **The corner-seam theory was wrong.** Computing the wall slabs from the committed scene shows
  all four corners overlap, so there was never a seam to thread.

The teleport-and-zero-velocity recovery stays, and now never fires. Release velocity is still
unclamped, which is a question about feel rather than correctness. Full numbers in
[KNOWNISSUES.md](KNOWNISSUES.md) issue 4.

## 8. Add the rest of the dice from the pack — ✅ done, August 2026

The CC0 source pack (`assets/Dice D20 D12 D8 D10 D8 D6 D4/`, Blend Swap #82440) holds eight
solids. All eight are in the game:

| Object | Faces | State |
|---|---|---|
| `D6 Dotted` | 6 | **done** — the die the project started with |
| `D4` | 4 | **done**, August 2026 |
| `D6 Numbered` | 6 | **done**, August 2026 |
| `D8` | 8 | **done**, August 2026 |
| `D10` | 10 | **done**, August 2026 |
| `D10 Percentile` | 10 | **done**, August 2026 |
| `D12` | 12 | **done**, August 2026 |
| `D20` | 20 | **done**, August 2026 |

**76 faces in total.** That number was the whole problem, and 8a below is where it was faced.

All of them shipped, at **46.34 MB** of PNGs and **7,396 atlas regions** — against 8a's
estimate of 43.8 MB and 7,396 frames, made before any of it was rendered. Both figures were
then cut by thinning every roll clip from 91 frames to 60: **32.20 MB and 5,040 regions**,
importing to 11.07 MB. See [tools/clip-lab/](tools/clip-lab/README.md). The frame count was
arithmetic and had to be right; the size landing within 6% of a figure extrapolated from a
single d6 was luckier than it deserved to be. Per die: d6 3.61, d20 11.85, d4 3.06, numbered
d6 3.10, d8 4.73, d10 6.46, percentile d10 6.50, d12 7.03.

**Nothing in the game knows a face count, a die's name, or what a face is worth.** `Dice.cs`
counts a die's faces from its own clips, the palette and the die list name each one from the
die itself, and `ValueStep` scales a face into a value for the one die that needs it. Adding a
ninth solid, if the pack ever grew, would be an entry in `dice_config.py`, two tables read off
a contact sheet, a render run, and a line in `game.tscn`.

### 8a. The blocker: 76 faces will not fit in this repository

Measured from what is already committed: one d6 is **3.61 MB** across 8 sheets, which is
~6.0 KB per landing frame and ~7.2 KB per idle frame at 128 px cells. Extending the current
architecture — one 91-frame landing clip per face, two 30-frame idle loops per die type —
gives:

| | frames | size |
|---|---|---|
| 76 landing clips | 6,916 | 40.4 MB |
| 16 idle loops | 480 | 3.4 MB |
| **total** | **7,396** | **43.8 MB** |

That is **12× the current 3.61 MB**, in PNGs, in git forever. It would also mean ~7,400
`AtlasTexture` sub-resources against the 606 in `dice.tscn` today. Render cost is *not* the
problem — roughly an hour of Blender plus half an hour of compositing, once.

#### Sharing the opening frames does not work — measured, August 2026

The obvious saving is to share the fast opening frames between the faces of one die, since a
die blurred beyond recognition ought to look the same whichever face it will land on. This was
tested properly: the d6 was re-rendered with **one tumble shared by all six faces** — identical
turn count, axis and drift, differing only in the orientation they come to rest on — then
composited and diffed frame by frame.

It does not help, and the reason is structural rather than a tuning problem.

| frame | six faces, own tumbles (shipped) | six faces, one shared tumble |
|---|---|---|
| 0 | 47.00 | 2.06 |
| 24 | 31.59 | 2.33 |
| 48 | 18.24 | 2.37 |
| 72 | 7.92 | 2.23 |
| 90 | 2.24 | 2.24 |

The shipped clips converge, because each face is a *different throw*. The shared-tumble clips
sit flat at ~2.2 from the first frame to the last — and 2.2 is exactly what six dice at rest
showing six different numbers measure. **The clips are equally distinguishable at every frame**,
so there is no prefix to share.

The cause: the animation is built as `pose(f) = tumble(f) ∘ rest(n)`. Sharing the tumble makes
`tumble(f)` common, but the two poses then differ by the constant rotation
`rest(n₁)⁻¹ ∘ rest(n₂)` at *every* frame. The die never stops carrying the face it is going to
land on. Motion blur hides it from a viewer, but nothing is actually shared.

Making frames shareable needs a different animation, not a different parameter: the tumble
would have to be genuinely face-agnostic up to a handoff frame, with each tail then slewing to
its own resting orientation. That reorientation has to happen while the die is still spinning
fast enough to hide it — so the handoff lands around frame 40 and the tails run 50 frames.

#### What the options actually cost

> **Superseded by measurement, August 2026.** These were estimated before the pack was
> rendered. [tools/clip-lab/](tools/clip-lab/README.md) now measures the same options
> against the committed sheets, and the shared-prefix figure below is optimistic: it assumes
> a handoff around frame 45–50, and the artwork stops hiding the face by frame 42. At a
> handoff the pictures support (frame 30) it is **35.3 MB / 27%**, which is *worse* than
> simply cutting the clips to 61 frames. The redesign only pays once the tails are shortened
> as well, at which point it is 24.2 MB / 50%. The clip lab also confirms the prefix cannot
> be shared without re-rendering — the faces are separate trajectories, and splicing them
> jumps visibly.

| | size | note |
|---|---|---|
| all 76 faces, as the pipeline stands | 43.8 MB | actually 48.6 MB as shipped |
| all 76, shared prefix + slewing tails | 27.5 MB | 37% saved, needs the animation redesign above — **measured at 27% for a safe handoff** |
| all 76, clips cut from 91 to 61 frames | 30.5 MB | 30% saved, no redesign; loses a third of the tumble |
| **d6 + d20** | **14.7 MB** | d6 already shipped |
| d6 + d20 + d4 | 17.2 MB | |
| d6 + d20 + d4 + d8 | 21.9 MB | |
| d6 alone, today | 3.61 MB | |

The redesign buys 37% for real work and still lands at 27.5 MB — seven times the current
budget. **Sharing is not the lever.** The only options that keep this repository anywhere near
its current size are shipping a subset, or accepting that the artwork dominates it.

Frames cannot be trimmed from the front, incidentally: `Dice.Roll()` enters the clip at the
release frame, which is an idle frame 0–29, so those are all live entry points.

#### Decided, August 2026: **d6 + d20, and a pipeline that is configured per die**

Ship the d20 next and stop there for now, at about **14.7 MB** — four times the current
artwork budget rather than twelve. It is the one die anyone reaches for besides a d6, it needs
no animation redesign, and the sharing idea is off the table on the evidence above.

The rest of the pack is **deferred, not dropped**, so the work below is to be built
**parameterised from the start**: adding a d10 later should be a table entry and a render run,
not another pass through `render.py`. Concretely that means

- one **per-die configuration block** — source object, face count, face-to-value table, resting
  yaw, throw parameters — rather than the current module-level `SRC_OBJECT` and six hardcoded
  `FACE` entries;
- **`build_scene()` and the geometry pass taking that block**, so nothing reads "six" from a
  constant (8c);
- **output paths and `SpriteFrames` names derived from the block**, so one die's sheets and
  scene never collide with another's (8e);
- `Dice.cs` reading its face count from the scene rather than assuming `% 6` (8d).

The subset decision is therefore about **what gets rendered**, not about what the tooling can
do. Nothing here should have to be revisited to add the remaining six dice.

**This decision comes before any rendering.**

### 8b. The d20's face-to-value table — ✅ done, August 2026

The d6 was tractable because its faces carry *pips*: `pip_counts()` counts connected clusters
of recessed geometry and gets 1–6 for free. Every other die in the pack is alphanumeric, and
you cannot count a glyph. So the mapping was read by eye, once, and committed.

`tools/dice-render/face_sheet.py` renders every face flat-on into an indexed contact sheet.
Two details it needs to be usable, both learned the hard way: the whole die has to be in frame
rather than zoomed to the face, and **only the target face's glyph may be inked** — mark them
all and every neighbour shows a readable numeral too, and then it is anyone's guess which one
the image is of.

The result is in `dice_config.py` as `face_values`, and it is **machine-checked, not merely
read**: `face_masks()` verifies that all ten opposite pairs sum to 21 and that each of 1–20
appears exactly once. A misread would have to be a self-consistent conspiracy to survive that.
The 6/9 hazard resolved itself — both carry a disambiguating dot, and a swap would have broken
the sum check anyway.

#### The twist, which this item did not anticipate

Bringing a face normal to +Z leaves the spin *about* +Z completely free. For pips that does
not matter. For numerals it decides whether the die reads `13` or something lying on its side,
and the first sheet had most numerals rotated.

The numerals turn out to be aligned to the triangle's edges, so each face has exactly **three**
candidate orientations and picking the upright one is a ternary choice. `face_sheet.py` renders
all three per face; the answers are `face_twists` in the config. The corner directions are
found by averaging `exp(3i*theta)` over the rim, which picks out a triangle's three-fold
symmetry however the mesh happens to be ordered.

The correct candidate is **not** a constant across faces, because the in-plane basis rotates
with the face normal — the d20 uses all three.

Confirmed by rendering the resting pose for all twenty values through the game camera: every
one shows the right numeral, upright.

#### Two fixes that came out of it

- **`recessed_by_face()` took the first matching face, not the nearest.** On a d20 the faces
  are only ~41° apart, so a vertex near a shared edge falls inside two bands. Taking the first
  left stray marks on one face and *bitten-off* two-digit numerals on the other — both visible
  in the first contact sheet. It now takes the nearest plane, which is well defined because the
  die is convex and no vertex is ever above a face plane.
- **The red tint was hardcoded to whichever face carried a 1.** That is a d6 affectation; it
  put a red numeral on the d20. It is `red_value` in the config now, `1` for the d6 and `None`
  for the d20, where tinting either the 1 or the 20 would be a defensible choice and neither
  should be invented.

Both dice re-verified afterwards: the d6's masks are still identical vertex for vertex.

### 8c. The geometry pass works off arbitrary face normals — ✅ done, August 2026

`render.py` no longer assumes a cube. The two functions that did have been replaced:

- `pip_masks()`, which classified vertices by which of six axis directions they faced, is now
  `face_planes()` + `recessed_by_face()` + `pip_counts()`. Polygons are grouped by normal and
  distance from the centre, and the **N largest groups by area** are the faces, with N taken
  from `dice_config.py`.
- `rest_quat()`, which looked up one of six axis-aligned Euler rotations, now takes the minimal
  rotation from the face normal to +Z.

`render.py` also reads its die from the config now: `DICE_DIE=d20 blender ... --python
render.py`, defaulting to `d6`.

**Verified against the code it replaced, on the d6:**

| | |
|---|---|
| value → face normal | same for all six |
| `DotMask` | 2,520 vertices, **0 differ** |
| `RedPip` | 120 vertices, **0 differ** |
| resting rotation | identical, worst deviation 1.2e-07 |
| rendered resting frame | **pixel-identical** to the committed sheets, max channel difference 0 |

Two details that made that possible. The minimal rotation happens to equal the old lookup for
five of the six faces; only the face pointing straight away from the camera is ambiguous, since
any in-plane axis will do, so it is pinned to a flip about +X — which is what the old table
did. And the in-plane radius used to decide which vertices sit "on" a face is measured from the
face's own geometry rather than hardcoded at 0.40, which is what makes it transfer to a solid
whose faces are a different size.

**The D20 is read correctly:** 20 face planes, all at offset 0.422–0.423, every one with an
opposite, every one carrying recessed glyph geometry (222 to 1,092 vertices, varying with the
numeral), and `rest_quat` brings all 20 to +Z.

Values come from counting pip clusters, which only works on a pipped die; on the D20 the
check fails loudly with a message naming 8b rather than producing 20 wrong faces silently.
That was the one remaining blocker at the time, and 8b removed it.

One thing 8c did not catch, because measuring the d6 against itself cannot: **the die is
scaled to its bounding box, which makes an icosahedron render a third too small.** See 8f.

### 8d. Code that hardcodes six — ✅ done, August 2026

Nothing here needed a face count exported. A die's `SpriteFrames` already carries one clip per
face, named `1`..`n`, so `Dice.FaceCount` counts the animations whose names parse as integers
and caches the answer. A d20 scene therefore needs no extra wiring, and a scene with a gap in
the numbering (`1, 2, 4`) warns rather than quietly rolling a number it cannot animate.
`FaceCount` resolves on first read rather than in `_Ready`, because `DicePalette` asks a scene
it has only instantiated.

Around it:

- `WrapFace` and `PlaceOnFace`'s bounds check now run off `FaceCount`.
- `GameManager.DiceScene` became `DiceScenes`, an exported `Array[PackedScene]`, and
  `SpawnDie` takes the scene to spawn. A second die type is an extra element in `game.tscn`.
- `DicePalette` builds one entry per scene, and derives both from the die itself: the label
  from its face count, the icon from the last frame of its `1` clip. There is no per-die
  artwork to keep in step.
- `DiceHud` labels dice `d6 #1` — the kind, then the sequence number. The old `D{Id}` read as
  die-kind notation the moment a d20 existed. Deriving the kind from the face count lasted
  until two dice shared one; see 8g.

**One thing the item did not anticipate: the release-frame mapping was about to become
unfair.** `ChooseResult` mapped the integer frame index onto a face, and six faces over a
thirty-frame idle loop divided evenly — five frames each. Twenty faces over thirty frames does
not: some faces would get two frames and some one, a 2:1 bias built into the die. The release
position is now continuous (`Frame + FrameProgress`), which splits evenly for any face count.
A 12,000-roll sweep over a synthetic 20-clip die lands 600 on every face; on the old mapping
it would have been 1,200 and 600 alternating.

Fourteen headless checks: the d6's frame-to-face mapping unchanged and still fair over 12,000
jittered rolls; a hand-built 20-clip `SpriteFrames` reporting `FaceCount == 20`, staying in
range, distributing evenly, and wrapping jitter rather than running off the end; `PlaceOnFace`
accepting 20 and refusing 21; `AnimatedSprite` being live on `Instantiate` before the node
enters the tree, which the palette depends on; `game.tscn` supplying its die through the array
export; and `SpawnDie(scene, pos)` adding a die.

`docs/screenshot.png` changed, and only where it should: the HUD rows read `d6 #2` instead of
`D#2`, the palette shows a `D6`. Dice, positions and values are identical, and `docs/roll.gif`
is byte-identical.

**Still open:** the HUD's **Total** is a plain sum, which is wrong for a percentile d10 (8a).
That is a decision to make when one exists, not now.

### 8e. `dice.tscn` is generated — ✅ done, August 2026

The scene carries one `AtlasTexture` sub-resource per animation frame: 606 for the d6, about
2,000 for a d20. It is now written by `tools/dice-render/make_scene.py` from
`tools/dice-render/dice_config.py` plus whatever sheets are on disk.

`dice_config.py` is the per-die table the rest of item 8 is meant to hang off — source object,
face count, sheet prefix, scene path and uid, cell size, fps, and the whole `RigidBody2D`
setup. Adding the d10 later is an entry in `DICE`, not another pass through the tooling. The
six dice not being rendered yet are listed in `DEFERRED` so their face counts live in one place.

Two things make the generator trustworthy rather than merely plausible:

- **Its first job was to reproduce the scene it replaces.** Running it without `--write`
  compares what it would emit against the committed scene, field by field — textures,
  animation names, loop flags, speeds, every frame's atlas region, every sub-resource and
  every node property. It matched the hand-maintained d6 exactly.
- **That comparison was itself tested.** Fifteen deliberate mutations of the config — wrong
  collider radius, wrong bounce, wrong CCD mode, wrong fps, wrong column count, a missing idle
  — were each detected, two of them by the generator refusing to emit at all because the sheet
  grid no longer matched the PNG on disk. A checker that only ever says "matches" is worse
  than none.

The generated scene then replaced the committed one, and **`docs/screenshot.png` and
`docs/roll.gif` regenerated byte-identical** — Godot loading the new scene, stepping all 606
frames and producing the same hashes is about as direct as this gets. Twelve in-engine checks
confirm the body settings, collider, physics material and clip structure all survived.

Sub-resource ids are now deterministic (`AtlasTexture_3_042` rather than
`AtlasTexture_qxx4u`), so the file diffs sensibly when something really does change.
`tools/dice-render/validate.py` still checks the result against the PNGs and is unchanged.

**Do not hand-edit `scenes/dice.tscn` any more.** Change `dice_config.py` and regenerate.

### 8f. Rendering the d20 — ✅ done, August 2026

The point of 8a–8e was that this step should be a run, not a project. It nearly was, and the
one thing that did need fixing is worth recording.

**Dice were being scaled to equal bounding box, which is not equal size.** `normalised_mesh`
fits every die into a unit cube. A cube fills its bounding box; an icosahedron does not, so
the d20 came out **42 px across against the d6's 58 px** — a third smaller, sharing a board.
The fix is not a per-die fudge factor. A convex body's *mean* silhouette over all
orientations is exactly a quarter of its surface area (Cauchy), and a die is a convex body
that tumbles through every orientation, so `presentation_scale()` divides by `sqrt(area)`,
calibrated to leave the d6 at exactly 1.0. The d20 lands at 1.447 and measures **58×62 at
rest against the d6's 58×62**. `REST_Z` follows from the die's inradius, so a die drawn
bigger rests higher instead of sinking into the board, and every height in `SEGS` became an
offset above it.

That also meant the d20 needed **no collider changes at all**: its resting sprite sits within
a pixel of the d6's, so `collider_radius` and `collider_offset` carried over untouched.

Two other things the run needed:

- **Per-face throw variation is generated now.** Twenty landing clips that read as one clip
  played twenty times would be worse than none, and twenty hand-tuned sets of numbers are not
  worth anyone's afternoon. `throw_params()` spreads turn count, tumble axes and drift over
  the ranges the d6's hand-picked six occupy, using a low-discrepancy sequence rather than
  random draws, which clump. The d6 pins its own six in `face_throws` because its artwork is
  already shipped and a re-render has to match.
- **A red glyph is a setting, not a rule.** Tinting the 1 is a d6 thing; on a d20 either the 1
  or the 20 would be defensible, so `red_value` is per die and `None` for the d20. `--red 20`
  renders a variant to look at without editing the table.

The whole pipeline is now driven by `pipeline.py`, one clip at a time — a d20's sub-frames
are ~2.2 GB if they all exist at once, and clip-by-clip with pruning keeps the peak near a
twentieth of that while making the run resumable. 21 clips, 48 minutes, 11.85 MB of sheets,
1,880 atlas regions in `scenes/d20.tscn`.

**The refactor was checked against the shipped artwork, not assumed.** Re-rendering
`dice_idle0` with the generalised code gives 54 pixels of 491,520 differing by 1/255 — EEVEE's
sampling noise — and `make_scene.py d6` still reproduces `scenes/dice.tscn` exactly. Nine
in-engine checks cover the d20 end to end: 20 clips at 91 frames, both idles, every resting
frame resolving to a real 128 px atlas region, rolling reaching all 20 faces, the palette
building two entries, and `SpawnDie` putting a d20 on the board.

### 8g. The d4 and the numbered d6 — ✅ done, August 2026

The first dice added after the tooling was declared finished, and the point of them was to
find out whether it really was. Mostly: both are a config entry, two tables read by eye, and
`pipeline.py <die> --run --install --scene`. Three things did need building, and one was a
real bug in what had already shipped.

**The d4 has no parallel faces**, so it cannot rest with one face up — it stands on a face
with a vertex at the top. `rest_face_down` sends the chosen face to −Z instead of +Z. It is
also a *missing-numeral* die: each face carries three numerals and omits one, and the omitted
one is what the die shows when it lands on that face. That holds under either d4 convention,
which is worth knowing because this model is bottom-read and the apex-read reading gives the
same table. There is no opposite-faces check to lean on — a tetrahedron has none — but each
value appearing on exactly three faces is a real constraint and the reading satisfies it.

**The die was being centred on its bounding box.** That is the true centre for anything
centrally symmetric, and it is how every die had been normalised since the pipeline was
written. For a tetrahedron it is wrong by a quarter of the die: the d4's four face planes sat
at 0.10 to 0.42 from the origin instead of all alike. That put the rotation pivot off centre,
the resting height wrong, and — because face radii decide which recessed vertices belong to
which face — **glyphs assigned to the wrong faces**. The first reading of the d4's contact
sheet was wrong because of it. `recentre_on_faces` now solves the incentre exactly:
`nᵢ·c + r = dᵢ`, least squares over every face.

It is applied only above a tolerance, which is not a fudge. For a centrally symmetric solid
the solve returns numerical noise — 4e-4 of the d20's inradius against 1.0 of the d4's — and
moving by that noise is not free, because `recessed_by_face` decides what is a glyph by an
absolute depth below the face plane. Measured: shifting the d20 by its own noise re-inked the
numerals enough to change **71,355 pixels** of one landing clip. The d20 now re-renders
byte-identical, and so does the d6.

**Face symmetry is measured, and the first rule for it was unsound.** A square face has four
ways up and a triangle three, so `face_twists` needed to stop assuming three. Scoring how
strongly the rim repeats every 360/m degrees and taking the strongest reports a triangle as
six-sided — three corners are perfectly six-fold coherent as well as three-fold. It is the
*smallest* m that fits, over corner vertices only.

Two smaller things:

- **`face_sheet.py` had been broken since the nearest-plane fix** in 8b: `face_planes()` grew
  a fourth element and the unpack was never updated. It had not been run since. It now also
  renders two sheets it did not before — every value at its resting pose through the game
  camera, and every value at every twist. The second is what `face_twists` is read off; the
  flat-on sheet cannot show it, because it presents every face the same way up by
  construction.
- **A die may carry its own palette name.** The label was derived from the face count, which
  works until two dice have the same one: the pipped and numbered d6 both came out "d6".
  `Dice.DieLabel` overrides it, written into the scene only when it differs from the derived
  name, so no existing scene changed. The palette and the die list now use the same name.

**The numbered d6 renders smaller than the pipped one — 48 px across at rest against 58 —
and there is no choice about it.** A numeral is upright at exactly one rotation about the
vertical, which pins that cube face-forward; the pipped d6's pips have no way up, so its
rotation is free and it stands corner-forward, and a cube is 1.4× wider across its corners
than across its faces. All four twists give an identical silhouette, so no other twist
recovers the size. Only tilting every numeral 45° would, which is worse.

**The d4 renders larger — 70 px — and that is the silhouette rule working.** It equalises the
*mean* silhouette over all orientations, and a tetrahedron is the least spherical solid in the
pack, so matching on average leaves it chunkier at rest. Colliders now follow the drawn die:
`pipeline.py <die> --collider` measures the resting sprite and reports the circle that fits,
by a rule that reproduces the d6's and the d20's shipped numbers.

### 8h. The d8 — ✅ done, August 2026

The first die that was genuinely just a run. An octahedron is centrally symmetric, so the
incentre correction is a no-op; every face has an opposite, so `face_values` is machine-checked
against pairs summing to 9 and passed on the first reading; the faces are triangles, so three
twists each. Nine clips, twenty minutes, 4.73 MB, and it rests at 58x60 against the d6's 58x62
-- close enough to spherical that the silhouette rule needs no argument.

**The tooling did have one real defect, and it was in the reading, not the rendering.** The
twist sheet tiled whole dice at thumbnail size, and on an octahedron the face carrying the
value is foreshortened to about half its height by the camera. At that size a numeral rotated
by a third of a turn is not reliably distinguishable from an upright one: **four of the eight
entries were read wrong**, and the mistake only surfaced when the finished die was looked at
and one numeral sat crooked.

Two things came out of it. Before re-reading, the table's *application* was ruled out as the
cause mechanically rather than by eye: each value's resting render must be pixel-identical to
one of its three twist renders, and all eight matched at delta 0, which left only the reading.
And `assemble_twists` now crops to the face the value is read off and enlarges it, instead of
tiling whole dice -- the corrected table came straight off the new sheet.

That fix matters more than the d8 does. The same misreading was available on every numbered die
already shipped, and the d20's twenty entries were read off the old sheet.

**The die list grew to fit.** Five die types on the board is five rows, which overflowed the
drawer and opened onto a half-cut row. The list scrolls and always has, but a board holding one
of each should not look broken; the drawer is 38px taller. The palette still scrolls at five
entries and will need smaller ones before all eight fit.

### 8i. The d10 — ✅ done, August 2026

Three things about this die are unlike every other, and each needed the tooling to grow.

**It is printed 0-9, not 1-10.** The game needs a clip per face and `Dice.cs` names them
1..n, so the face showing 0 is stored as value 10 -- the ordinary reading of a d10. That broke
the only machine check `face_values` gets, since opposite faces sum to 9 rather than 11. The
check now compares what is *printed* rather than what is stored, which keeps it for both kinds
of die; `zero_based` says which a die is.

**Its faces are kites, which have no rotational symmetry at all.** `face_symmetry` measured
nothing above 0.5 and refused rather than guessing, which was the right answer. There is no set
of equivalent ways up here -- the twist is a free angle.

**And two things that had been the same number until now had to come apart.** The first attempt
set `face_symmetry = 12` to get thirty-degree steps, but `corner_angle` uses that same number
for its moment, so it was taking a twelve-fold moment of a kite: noise. Every face got its own
arbitrary zero and the best steps came out scattered -- 5, 3, 8, 10, 10 for the first five
values. A face's symmetry fixes the *reference direction*; how finely the twist is quantised is
a separate question, and `twist_steps` is now separate from `face_symmetry`.

With the reference stable the ten faces fell into two clean groups exactly 180 degrees apart:
the kite's mirror ambiguity, because a two-fold moment finds the long *axis* but not which end
is the point. The dotted 6 and 9 settled it, being the only two digits whose orientation is
unmistakable. The one-fold moment resolves the axis into a direction -- the rim's own centre of
mass sits toward one end -- and with that, **all ten faces want the same rotation**. So the d10
needs no per-face twist table at all: `resolve_axis`, one `twist_offset_deg` of 135, and every
value came out upright first try.

That is a better outcome than the d8, where a ten-entry table was read by eye and four were
wrong. The two flips predictable by eye both matched, and the one value the geometry disagreed
with was the one already flagged as ambiguous.

`render.py` moved a long way for this, so the d6 was re-rendered afterwards and still matches
the committed sheets to within sampler noise.

**The screenshot board stopped growing.** Six die types no longer fit the five-row die list, and
the UI is getting its own pass once the pack is complete, so the board shows five of the six
rather than opening onto a half-cut row. The numbered d6 is the one left off, being the least
distinct from the die already there; the palette still offers all six, and scrolls.

### 8j. The percentile d10 — ✅ done, August 2026

The die every earlier note deferred, on the grounds that "it shows 00-90, which the game code
has no concept for". It does now.

**A die's face and a die's value stopped being the same number.** `Dice.ValueStep` is what one
face is worth when reported: the stored result stays 1..`FaceCount`, because that is what names
the animation clips and what `PlaceOnFace` addresses, and `Value` is what a player reads off the
die. Only this one sets it, to 10. The HUD and the running total use `Value`; everything that
addresses a face still uses the stored number.

The 00 face reports **100**, the same reading that makes a plain d10's 0 a ten. A percentile die
alone is more often read as tens giving 0-90, and only becomes 1-100 paired with a d10 — but a
sandbox that sums dice should not have a face worth nothing, and the two d10s should agree with
each other. Changing it means treating the tenth face as 0.

**It is not zero-based, despite the printed 00, and that was worth having a check for.** The
plain d10 was, so the obvious move was to copy the flag across; the reading disproved it. This
die's printed values pair up to 110 counting 00 as 100, which is the *ordinary* sum-to-eleven
rule in the stored numbering, where the plain d10's pair up to 9 and need the modular one. Two
dice from the same pack, numbered to different conventions. Only the machine check would have
caught that being copied over.

Everything else transferred whole. Same solid as the plain d10, same lettering, so the same
kite handling — `face_symmetry=2`, `resolve_axis`, one 135-degree offset, no per-face twist
table — and all ten values came out upright on the first rest sheet. Its collider measures
identical to the plain d10's, which is the answer you would want from measuring the same shape
twice.

The board in the screenshot swapped the plain d10 for this one: they are the same solid, so
nothing is lost visually, and the die list now shows a row reading `d10 % #4  70` against a
total of 103, which is the new behaviour actually doing something.

### 8k. The d12 — ✅ done, August 2026

The last solid in the pack, and the one that found the oldest bug in the geometry pass.

**`face_symmetry` could not measure a pentagon.** It took the rim as everything past 0.85 of
the circumradius, which drags each corner's bevel neighbours in with it. Corners 120 degrees
apart on a triangle tolerate that; 72 degrees apart on a pentagon do not -- the d12 scored 0.48
at m=5 where it should score 1.00, below the threshold, so the function refused. Past 0.90 it
scores exactly 1.00. The cut is adaptive now, tightening until it gets a decisive answer.

That is the setting that decides how every measured die is presented, so all eight were checked
afterwards: the five that measure their own symmetry are unchanged, the d12 resolves to 5, and
the d10 and percentile d10 still refuse -- which is the refusal worth protecting, since a guess
there would silently produce a wrong model.

**The values are the identity**, 1..12 in face order, and did not have to be taken on trust: the
six opposite pairs sum to 13, and the five faces carrying two glyph clusters are exactly 6 and 9
(underdotted) plus the two-digit 10, 11 and 12. Two independent corroborations of one reading.

**One twist was misread again, and caught the same way.** Value 10 has an underline beneath the
numeral, which made the sideways rendering look level in the grid. The pixel-identity check --
each resting render must be byte-identical to one of that value's twist renders -- came back
clean on all twelve, which ruled out the plumbing and left only the reading. That check has now
caught this class of mistake on the d8, the d10 and the d12.

### What it took, in order

1. Decide 8a. Nothing else was worth doing until the size question had an answer.
2. Write the scene generator (8e) and point `validate.py` at its output.
3. Generalise the geometry (8c) and prove it on the d6 — known answers, directly comparable
   against the die already shipping.
4. Build the face-to-value tables (8b).
5. Generalise the code (8d).
6. Render the d20 (8f).

7. Render the remaining six, one at a time (8g, 8h, 8i, 8j, 8k), fixing the tooling where
   each one found a hole in it.

**Each die after the first found exactly one real defect**, and none of them was in the part
that looked hard. The d4 found the die being centred on its bounding box, which is wrong for
anything not centrally symmetric and had been wrong since the pipeline was written. The d8
found the twist sheet being unreadable at the size it was drawn. The d10 found a face's
symmetry and a twist's granularity being the same number when they are not. The percentile d10
found a die's face and a die's value being the same number when they are not. The d12 found the
rim cut that decides a face's corner count being too loose for a pentagon.

That is a good argument for having rendered them one at a time rather than in a batch: every
one of those was caught by looking at a finished die and finding one thing wrong with it.

## 9. A browser demo, via a hand-written GDScript port — started, August 2026

A playable GitHub Pages build is the single most valuable thing this repository could gain,
and it is still blocked. Godot's own documentation, checked August 2026, is blunt about it:

> Projects written in C# using Godot 4 currently cannot be exported to the web.

So waiting is not a plan. **The decision (August 2026): port the gameplay to GDScript by hand
into a second tree, after item 8.** C# stays canonical — it is the project, and it is where
work happens. The GDScript tree is a derived artifact that exists only to produce the web
build.

A translator was considered and deferred; see 9e.

One piece of good news up front, because it removes the trap most Pages deploys hit: since
Godot 4.3 the **single-threaded** web export is the default, and it does not need the
`Cross-Origin-Opener-Policy` / `Cross-Origin-Embedder-Policy` headers that `SharedArrayBuffer`
requires. GitHub Pages cannot set custom headers, so this project must stay on the
single-threaded export — and by default it already would.

**Exit condition.** If .NET web export ever ships, all of this is deletable: drop `web/`, drop
the workflow, export the C# project directly. That line in the Godot docs is the thing to watch.

Re-checked against 4.4.1 in August 2026 by adding a Web preset and running the export. The
editor refuses before it starts, in as many words: *"Export to Web is currently not supported
in Godot 4 when using C#/.NET."* Still blocked, still item 9.

**The theme shader does not need porting.** `.gdshader` is language-agnostic, so
`shaders/dice_theme.gdshader` crosses to the GDScript tree verbatim — the one part of the C#
side that costs nothing. It is written for this target: everything in it is GLSL ES 3.00, which
is what a WebGL2 export compiles to, and the neighbourhood taps in mode 3 use `textureLod`
rather than `texture` because implicit-derivative sampling inside non-uniform control flow is
undefined in that dialect. Threading does not enter into it — `thread_support` is a WebAssembly
setting and shaders run on the GPU — but single-threaded builds have no background shader
compilation, so the one program should be warmed during load rather than on the first throw.
See [tools/theme-lab/README.md](tools/theme-lab/README.md).

### 9a. The port

**Re-counted August 2026: 4,465 lines across twelve scripts** — it was 4,160 when this
entry was written, and it grew again between the estimate and the start, exactly as the
warning below said it would. `Player.cs` has since been deleted (item 3).

**Progress, August 2026.** 9a is **done**: all twelve scripts are ported, 4,465 lines of
C#, and `game.tscn` loads and runs under the standard engine with 29 checks passing.
9b's rewriter and 9c's workflow are built. What is left is the part the suggested order
put first and this did not do — **the export has never been run**, because no
standard-engine web templates are installed locally. The workflow is deliberately
`workflow_dispatch`-only until that first run succeeds. See [tools/web-port/README.md](tools/web-port/README.md), which carries
the running state; what follows is the plan it is being measured against.

The one part of the suggested order that has **not** been done is its first step —
proving the deployment. Only the `.mono` export templates are installed locally, so
nothing has been exported yet. The port turning out to be testable without that is luck:
running a GDScript project needs no templates, exporting one does. The old estimate here said 940 lines across five and
"a day's work, roughly"; that was written before the dice pack, the right-click menus, the
themes, the audio, the save file and the touch controls. It is several days now, and the
estimate is the thing most likely to be wrong again by the time anyone starts — **re-count
before planning.**

`shaders/dice_theme.gdshader` is not in that total and does not need porting: `.gdshader`
is language-agnostic and crosses over verbatim.

The largest single file is `GameManager.cs` at 1,269 lines, which is where most of the risk
is. `Dice.cs` (628) is the other one that matters, because it is a state machine whose
transitions are load-bearing — see KNOWNISSUES 3.

It lives in `web/`, committed. What must **not** be committed is a second copy of `assets/`:
Godot roots `res://` at `project.godot`, so the GDScript project needs the assets inside its
own tree, and duplicating 3.6 MB of spritesheets in git — far more once item 8 lands — is
exactly the kind of thing the 2026 cleanup existed to undo. So `web/` holds only what is
genuinely different:

```
web/
  project.godot          no [dotnet] section, no "C#" in config/features
  export_presets.cfg     a Web preset, single-threaded
  scripts/*.gd           the hand-written port
```

Everything else — `assets/`, and the rewritten `scenes/` — is assembled by CI at build time.

The constructs that will not map straight across, counted from the current `scripts/`:

| C# in this project | GDScript |
|---|---|
| `[Signal] delegate void DiceRolledEventHandler(int)` | `signal dice_rolled(result: int)` |
| `event Action<Dice> DeleteRequested` (`DiceHud`) | no equivalent — a signal or a `Callable` |
| `HashSet<Dice>` (`GameManager.deletingDice`) | **no equivalent**; a `Dictionary` used as a set |
| `Dictionary<Dice, Vector2>`, `List<Dice>` | `Dictionary`, `Array` — untyped |
| 19 pattern matches (`is not Dice other`, `face is < 1 or > 6`) | expanded to plain conditionals |
| 5 expression-bodied properties (`public bool IsHeld => …`) | a `func`, or a getter block |
| `static readonly StringName Idle0 = "idle0"` | `const IDLE0 := &"idle0"` |
| 3 interpolated strings | `"Total: %d" % total` |

Nothing worse than that is in here: **no LINQ, no `async`/`await`, no `enum`s, no interfaces,
no inheritance beyond Godot base classes.** That is what makes a hand port a day rather than a
week.

### 9b. Scene and project rewriting — still automated, still needed

Dropping the translator does **not** drop this. The scenes name C# in ways a hand port cannot
fix by itself, and `dice.tscn` is ~4,300 lines of which 606 are `AtlasTexture` regions — nobody
maintains a second copy of that by hand.

So `tools/web-port/` gets a rewriter that reads `scenes/` and emits the GDScript equivalents:

- Repoint every script `ExtResource` at a `.gd` file, and regenerate the `.uid` sidecars.
- **Rename exported property keys.** `dice.tscn` contains
  `AnimatedSprite = NodePath("AnimatedSprite2D")`; the GDScript export is `animated_sprite`, so
  the key must change. Miss this and the scene loads with null exports and **no error at all**.
- Leave the 606 atlas regions untouched. `tools/dice-render/validate.py` already proves that,
  and should run against the rewritten scene as part of CI.

### 9b-touch. Shake-to-throw needs the browser, not Godot

`ShakeGesture` already decides what a shake is, and `GameManager` feeds it from
`Input.GetAccelerometer()`. That works on an Android or iOS export and returns zero
everywhere else, including in a browser: **Godot's web platform does not implement the
sensor APIs.** The GDScript port therefore has one thing to add that the C# tree cannot
carry over —

- read the DOM's `devicemotion` event through `JavaScriptBridge` and call `Feed`;
- ask for it only on a gesture, because **iOS 13+ requires
  `DeviceMotionEvent.requestPermission()` from inside a user interaction** and refuses
  otherwise;
- the page must be served over HTTPS, which GitHub Pages is.

Everything else about the gesture — the threshold, the cooldown, ignoring a tilt — is
already written and already tested, and needs no second copy.

### 9c. The pipeline

A GitHub Actions workflow on `ubuntu-latest`:

1. Install Godot **standard**, not .NET — the exported project contains no C#.
2. Fetch the matching web export templates.
3. Assemble a scratch project: `web/` + a copy of `assets/` + `scenes/` through the rewriter.
4. `godot --headless --import`, then
   `godot --headless --export-release "Web" build/index.html`.
5. `actions/upload-pages-artifact` + `actions/deploy-pages`.

### 9d. Parity — the load-bearing part

C# being canonical means the two trees drift the moment either changes, and a stale web demo
is worse than none. This is the mitigation, and the machinery already exists:

- **The headless test harness** (see [CLAUDE.md](CLAUDE.md)) drives real scenes and asserts on
  real engine state, currently 18 checks. Port it too, run it against both trees, and require
  identical output.
- **The screenshot pipeline** (`tools/screenshots/`) is byte-deterministic — two runs produce
  identical files. Render `docs/screenshot.png` from both trees and diff. They should match
  exactly, which catches rendering and scene-wiring differences the harness cannot see.

Both run in CI, and **both should fail the build**, not warn. A parity check nobody is forced
to look at is a parity check that is already broken.

Two things soften the drift risk in practice. Item 8's weight is in `tools/` — Python, shared
by both trees — so only the small `Dice.cs` / `DicePalette.cs` changes need mirroring. And the
gameplay is close to finished: the board game is dropped, and the dice sandbox is what it is.

### 9e. The translator — deferred to its own project

Generating the port rather than writing it was the original plan and remains attractive in the
abstract, because it removes the drift problem at the root instead of policing it. It lost on
cost: weeks against a day. It is deferred, not rejected, and would be a separate project rather
than something this repository carries.

What was worked out, so it is not lost:

- The tool is **Roslyn** (`Microsoft.CodeAnalysis.CSharp`), and the important part is the
  **semantic model**, not the syntax tree. Parsing C# is the easy 20%; without type resolution
  you cannot tell what `Play` means on `AnimatedSprite`, and name mapping becomes guesswork.
  That means a `Compilation` with references to the Godot assemblies.
- **Do not hand-write the Godot API name table.** `godot --dump-extension-api` emits every
  class, method, property and signal in snake_case, and the C# bindings are generated from the
  same data in PascalCase. Pairing them yields an authoritative engine-wide symbol map that is
  generated rather than maintained, and regenerates when Godot updates.
- Scope it to the subset in 9a and **fail loudly** on anything outside. A translator that
  refuses to emit beats one that emits something subtly wrong.
- The two oracles in 9d are what make it verifiable at all.

If .NET web export ships first, none of this is needed.

### Suggested order

1. Add a `Web` export preset and confirm a **hand-stubbed** GDScript project exports and runs
   on Pages. Prove the deployment before building anything to deploy into it — the unknowns
   live there, and everything else is wasted if Pages fights back.
2. Build the scene rewriter (9b). It is needed whatever else happens.
3. Port `Dice.cs` first: the most self-contained script, and the harness covers it hardest.
4. Port the rest, then the harness, then wire the parity gates (9d).
5. Only start after item 8 — porting a moving target twice is the one way to make this
   genuinely expensive.
