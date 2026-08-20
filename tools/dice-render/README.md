# Die animation pipeline

Regenerates everything under `assets/dice/` from the CC0 source model. This is the
pipeline that produced the current frames (ROADMAP item 6); it is kept so the art is
reproducible rather than a binary blob nobody can rebuild.

Nothing here runs as part of the game or the build. It is offline art tooling.

## Source

`assets/Dice D20 D12 D8 D10 D8 D6 D4/` — Blend Swap blend #82440, CC0 1.0, licence
page included in the download. See [`../../docs/ASSETS.md`](../../docs/ASSETS.md).

The `.blend` is **gitignored** (17 MB); the licence page that ships with it is committed, as
the record of the grant. Download the blend again from Blend Swap #82440 if you need to
re-run this.

## Running it

Needs Blender 4.4+ and Python with `pillow` and `numpy`. `pipeline.py` drives the whole
thing; the individual stages below still run on their own if you want one of them.

```sh
python tools/dice-render/pipeline.py d20                     # dry run: what it would do
python tools/dice-render/pipeline.py d20 --run               # render, composite, pack
python tools/dice-render/pipeline.py d20 --run --install --scene
python tools/dice-render/pipeline.py d20 --status            # what is already on disk
```

Nothing lands in the repository until `--install` (sheets into `assets/dice/<die>/`) and
`--scene` (regenerate the die's `.tscn`). Everything before that stays in
`tools/dice-render/build/<die>/`, which is gitignored.

Each die keeps its sheets in a directory of its own, named after it, and the files inside are
named after the animation: `assets/dice/d20/17_sprites.png`, `assets/dice/d6/idle0_sprites.png`.
Forty-four sheets in one flat folder needed a prefix on every filename to stay apart, and the
prefix said nothing the folder does not.

Useful flags:

| | |
|---|---|
| `--only 3,idle0` | just those clips, named as the game names them |
| `--resume` | skip clips already composited — an interrupted run continues |
| `--red 20` | tint that face's glyph red for this run; `--red none` for no tint |
| `--keep` | keep the 512px sub-frames instead of pruning as it goes |
| `--blender <path>` | if it is not where `BLENDER_CANDIDATES` expects |
| `--collider` | measure the installed resting sprite and report the collider that fits it |

**It renders one clip at a time, on purpose.** A d20's sub-frames are about 2.2 GB if they
all have to exist at once; compositing and pruning each clip before starting the next keeps
the peak near a twentieth of that, and makes the run resumable. Roughly a minute per
landing clip, so about 45 minutes for a d20.

### Jupyter notebook (Windows)

`dice_render.ipynb` is a wrapper over the same script: pick the die in cell 2, render one
clip in cell 4 and look at it, run the rest in cell 5, install in cell 7. Opening it
executes nothing, and the two cells that cost something ask first. Cell 6 previews any
clip, and prints every face's resting frame in a row — which is how you check a
`face_values` table.

### The stages on their own

```sh
export DICE_WORK=/tmp/dice-build          # optional; defaults to ./build
                                          # a die always gets its own subdirectory

# 1. render crisp sub-frames  (DICE_DIE picks the die, DICE_ONLY picks clips)
DICE_DIE=d20 DICE_ONLY=face3 blender.exe   "assets/Dice D20 D12 D8 D10 D8 D6 D4/Dices blendswap.blend"   --background --python tools/dice-render/render.py

# 2. accumulate them into motion-blurred 128px frames
python tools/dice-render/composite.py --die d20 face3

# 3. pack to spritesheets and drop them into assets/dice/d20/
python tools/dice-render/pack.py --die d20 --install

# 4. write the scene, then check it lines up with the sheets
python tools/dice-render/make_scene.py d20 --write
python tools/dice-render/validate.py d20
```

Step 1 builds a throwaway scene called `DiceRender` inside the open file. It does not
modify the source model and does not save the `.blend`.

## What each stage does

| | |
|---|---|
| `pipeline.py` | Drives the other five, one clip at a time, with `--resume`, `--status` and `--collider`. The thing to run. |
| `render.py` | Builds the toon material and camera, then renders each output frame as up to 20 crisp samples across its shutter interval, into `build/<die>/faces/<clip>/`. Writes a `meta.json` per animation recording the sub-frame count and the ground-shadow position for each sample. `DICE_DIE`, `DICE_ONLY` and `DICE_RED` select the die, the clips and the tinted face. |
| `composite.py` | Per sub-frame: shadow, then an alpha-dilated black outline, then the die. Averages the sub-frames — that average *is* the motion blur — then box-downsamples 512→128. |
| `pack.py` | Lays frames out `cols` per row into the sheets the die's scene indexes. |
| `make_scene.py` | Writes a die's `.tscn` from `dice_config.py` plus the sheets on disk — 606 atlas regions for the d6, ~2,000 for a d20. Run without `--write` it *compares* instead, field by field, against the scene already committed. |
| `dice_config.py` | The per-die table: source object, face count, scene path and uid, cell size, fps, the face-value and twist tables and the whole `RigidBody2D` setup. Adding a die is an entry here. |
| `validate.py` | Re-reads a die's `.tscn` and checks every atlas region against the PNGs on disk. |

## Generating the scene

`scenes/dice.tscn` is **generated, not hand-edited** (ROADMAP 8e):

```sh
python tools/dice-render/make_scene.py d6            # compare against what is committed
python tools/dice-render/make_scene.py d6 --write    # write it
```

The comparison is the one to run most often: the generator's claim to being correct is that
it reproduces the scene it replaced, so it checks textures, animation names, loop flags,
speeds, every frame's atlas region, every sub-resource and every node property.

To add a die, add an entry to `DICE` in `dice_config.py`, render its sheets, and run
`make_scene.py <name> --write`. Nothing in the generator knows about six-sidedness.

## Things worth knowing before changing it

- **Shading is driven by the world normal, not by a light.** `KEY` and `BANDS` in
  `render.py` decide which flat colour each face gets. They are tuned so that at rest the
  top face lands in `lit`, the left in `mid` and the right in `dark`. Changing `YAW` moves
  the faces relative to `KEY` and will need `KEY` retuned, or faces start sharing a band.
- **Do not use the model's `Dots` vertex group.** It covers a square patch around each
  round dimple — 77% of all faces — and renders the pips as squares. `pip_masks()` selects
  the vertices actually recessed below the face plane instead.
- **The pip counts are read off the geometry**, not hardcoded, so `rest_quat()` knows which
  way to turn the die without anyone asserting "1 is on -Z".
- **Per-face throw variation is generated, not tuned.** Twenty landing clips that read as one
  clip played twenty times would be worse than none, and twenty sets of hand-picked numbers
  are not worth anyone's afternoon, so `throw_params()` spreads turn count, tumble axes and
  drift over the ranges the d6's six occupy, using a low-discrepancy sequence rather than
  random draws. The d6 pins its six in `face_throws` because its artwork is already shipped.
- **Sub-frame count scales with angular speed** (`deg_per_sub`), so slow frames near the
  landing cost one render and fast ones cost twenty. Dropping `max_sub` below about 16
  brings back visible ghosting on the first few frames.
- **The die is recentred on its incentre, not its bounding box.** The bbox centre is the true
  centre for anything centrally symmetric and wrong for anything else: a tetrahedron's four
  face planes came out at 0.10 to 0.42 from the origin instead of all alike, which put the
  rotation pivot off centre, the resting height wrong, and the face radii inconsistent — and
  those radii decide which recessed vertices belong to which face, so **glyphs were assigned to
  the wrong faces**. `recentre_on_faces` solves `n_i . c + r = d_i` by least squares.
  It applies only above `TOLERANCE`, and that is not laziness: for a centrally symmetric solid
  the solve returns noise, and moving by noise changes the glyph masks, because
  `recessed_by_face` uses an absolute depth below the face plane. Shifting the d20 by its own
  noise changed 71,355 pixels of one landing clip.
- **Face symmetry is the smallest m that fits, not the strongest.** A triangle's three corners
  are perfectly six-fold coherent as well as three-fold, so "highest score wins" reports a d4
  as six-sided. `face_symmetry` also looks only at the outermost vertices: a triangle's edge
  midpoints sit at half its circumradius and are three-fold coherent 60 degrees out of phase
  with its corners, which cancels most of the signal.
- **Dice are scaled to equal *mean silhouette*, not to equal bounding box.** `normalised_mesh`
  fits every die into a unit cube, which sounds like the same size and is not: a cube fills
  its bounding box and an icosahedron does not, so a d20 normalised that way came out 42px
  across against the d6's 58px. Cauchy's formula says a convex body's mean projected area
  over all orientations is exactly a quarter of its surface area, so `presentation_scale()`
  divides by `sqrt(area)`, calibrated to leave the d6 at exactly 1.0. The d20 lands at 1.447
  and measures 58x62 at rest against the d6's 58x62. The scale is applied to the *object*,
  not the mesh — the geometry pass works in absolute distances below a face plane, so
  resizing the mesh under it would change which vertices count as glyphs.
- **`REST_Z` is per die**, the inradius times that scale, and every height in `SEGS` is an
  offset above it. A die that is drawn bigger rests higher, or it sinks into the board.
- **Equal mean silhouette is not equal size at rest.** It is the right invariant for a die that
  tumbles through every orientation, but the resting extents still differ: the d4 measures
  70px across against the d6's 58, because a tetrahedron is the least spherical solid here. The
  collider follows the drawn die instead — `pipeline.py <die> --collider` measures it, by a
  rule that reproduces the d6's and the d20's shipped numbers.
- **A numbered face pins the die's rotation; a pipped one does not.** A numeral is upright at
  exactly one rotation about the vertical, so the numbered d6 stands face-forward while the
  pipped d6 — free to sit anywhere — stands corner-forward, and comes out 48px wide against
  58px. All four twists give the same silhouette, so there is no twist that recovers it.
- **The camera is orthographic.** `ortho_scale` controls framing without introducing
  perspective convergence as the die moves toward or away from the camera.
- **Both idle loops match the opening speed of a numbered roll.** `idle1` additionally
  cycles through the original animation's rainbow hues as a seamless colour gradient.
- **Frame counts are load-bearing.** Each die's scene holds 91 frames per face and 30 per
  idle loop, and `Dice.cs` plays them by name. Changing the counts means regenerating the
  atlas regions in the scene, not just the sheets.
- **A generated scene has no uid unless you give it one.** Godot mints uids on import from
  the editor; a `.tscn` written by a script does not get one, and `game.tscn` refers to these
  scenes by uid. `scene_uid` in the config holds one minted with `ResourceUID.create_id()`,
  and it has to stay put across regenerations.

## Adding the other dice (ROADMAP 8)

`dice_config.py` holds the per-die table. **All eight are rendered** — 76 faces, 46.34 MB of
sheets, 7,396 atlas regions. 8a estimated 43.8 MB before any of it existed and was right to
within 6%.

Adding one is a config entry, its two tables, and a run:

```sh
python tools/dice-render/pipeline.py d10 --run --install --scene
```

then add the generated scene to `DiceScenes` on the `GameManager` node in `scenes/game.tscn`.
Nothing in the game code changes: `Dice.cs` counts a die's faces off its own clips, and the
palette derives each entry's label and icon from the die itself.

Both `make_scene.py` and the geometry pass in `render.py` are shape-agnostic now: the face
count comes from the config, faces are found by grouping polygons into planes and taking the
**N largest by area**, and `rest_quat()` takes the minimal rotation from a face normal to +Z.
That was verified by rebuilding the d6 with it — same masks vertex for vertex, and a
pixel-identical resting frame (ROADMAP 8c) — and then by rendering the d20 (ROADMAP 8f).

Two rules that look right and are not: *the planes furthest from the centre* fails, because a
beveled die's rounded corners sit further out than its faces; and a fixed area threshold finds
nine planes on a six-sided die. The margin for the top-N rule is 7.8× to 41× across the pack.

**How tight the rim cut has to be depends on the face.** `face_symmetry` looks only at the
outermost vertices, and a rounded corner smears the signal further the closer the corners are
together: 120° apart on a triangle is forgiving, 72° on a pentagon is not — the d12 scores 0.48
at m=5 past 0.85 of the circumradius and exactly 1.00 past 0.90. So it tightens until it gets a
decisive answer, and refuses if none does.

An alphanumeric die needs two tables in `dice_config.py`, because neither can be derived:

- `face_values` — which value each face carries. Pip counting only works on a pipped die.
- `face_twists` — which of the face's three corners points up, so the numeral reads the right
  way round rather than lying on its side.

Both are read once off `face_sheet.py`, which renders three different sheets:

```sh
# 1. every face flat-on, one glyph inked at a time -> face_values
DICE_DIE=d20 blender "<the blend>" --background --python tools/dice-render/face_sheet.py
python tools/dice-render/face_sheet.py --assemble d20

# 2. every value at every way up, through the game camera -> face_twists
DICE_DIE=d20 blender "<the blend>" -b --python tools/dice-render/face_sheet.py -- --twists
python tools/dice-render/face_sheet.py --assemble-twists d20

# 3. every value as the game will show it, to check the two tables together
DICE_DIE=d20 blender "<the blend>" -b --python tools/dice-render/face_sheet.py -- --rest
python tools/dice-render/face_sheet.py --assemble-rest d20
```

The flat-on sheet **cannot** tell you the twists: it presents every face the same way up by
construction, so every numeral looks upright on it whatever the die will actually do.

**Read the twist sheet carefully — it is the one step with no machine check behind it.** The
face carrying the value is foreshortened by the camera, on an octahedron to about half its
height, and a numeral rotated by a third of a turn can pass for an upright one at a glance.
That is why `--assemble-twists` crops to that face and enlarges it rather than tiling whole
dice; read at thumbnail size, four of the d8's eight entries came out wrong. Always finish with
`--rest` and look at every value.

If one looks wrong there, check the table is being *applied* correctly before assuming you
misread it. Each resting render is produced by one of the twist renders, so it should be
pixel-identical to exactly one of them:

```python
# rest_04.png must equal v04_t<face_twists[face of value 4]>.png, to the byte
```

`face_values` is then machine-checked — opposite faces must sum to `faces + 1` and every value
must appear once — so a misreading has to be a self-consistent conspiracy to get through.

**The d4 has no up-face**, and it is handled: a tetrahedron has no parallel faces, so
`rest_face_down` stands it on a face with a vertex at the top. It is a missing-numeral die —
each face carries three numerals and omits one, and the omitted one is what it shows when it
lands on that face, which is true whether the die is read at the apex or along the bottom edge.
It also has no opposite faces and therefore no machine check on `face_values`; the constraint
that does hold is that each value appears on exactly three faces.

**The d10 is printed 0–9**, and the face showing 0 is stored as value 10 — `zero_based` says so,
and the opposite-faces check compares printed digits rather than stored values.

**Its faces are kites, which have no rotational symmetry**, so there is no set of equivalent
ways up and `face_symmetry` correctly refuses to measure one. Four settings cover that case, and
they are easy to confuse:

| | |
|---|---|
| `face_symmetry` | the order of the moment that fixes the *reference direction* |
| `twist_steps` | how many twists are offered, when that is not the symmetry |
| `resolve_axis` | turn a reference axis into a direction, using the one-fold moment |
| `twist_offset_deg` | where the artwork sits relative to the reference |

Do **not** raise `face_symmetry` to get finer twists: `corner_angle` takes its moment at that
order, and a twelve-fold moment of a kite is noise — every face then gets its own arbitrary
zero. With the axis resolved the d10 needs no per-face table at all, just the one offset.

**The percentile d10 shows 00–90**, and `value_step=10` is what turns its stored 1..10 into the
10..100 the game reports — its "00" face stores 10 and reads as 100. It is *not* `zero_based`
despite the printed 00: that flag is about how the opposite faces check out, and this die
satisfies the ordinary sum-to-`faces + 1` rule where the plain d10 needs the modular one. Two
dice from the same pack, numbered to different conventions; copying the flag across would have
been caught by the check, which is the point of having it.

The camera, toon material, motion-blur accumulation and compositing stages are all
shape-agnostic and carry over unchanged.
