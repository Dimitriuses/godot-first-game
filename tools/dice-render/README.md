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

Needs Blender 4.4+ and Python with `pillow` and `numpy`.

### Jupyter notebook (Windows)

Open `dice_render.ipynb`, run the setup cell, and then run the pipeline cell. The notebook
asks you to type `RUN` before every stage; opening it does not execute anything. Update the
`BLENDER` path in the setup cell if Blender is installed elsewhere.

```sh
export DICE_WORK=/tmp/dice-build          # optional; defaults to ./build

# 1. render crisp sub-frames  (~5 min, ~4900 renders at 512px)
& blender.exe `
  "assets\Dice D20 D12 D8 D10 D8 D6 D4\Dices blendswap.blend" `
  --background `
  --python "tools\dice-render\render.py"

# 2. accumulate them into motion-blurred 128px frames  (~2 min)
python tools/dice-render/composite.py

# 3. pack to spritesheets and drop them into assets/dice/
python tools/dice-render/pack.py --install

# 4. check the scene still lines up with the sheets
python tools/dice-render/validate.py
```

Step 1 builds a throwaway scene called `DiceRender` inside the open file. It does not
modify the source model and does not save the `.blend`.

## What each stage does

| | |
|---|---|
| `render.py` | Builds the toon material and camera, then renders each output frame as up to 20 crisp samples across its shutter interval. Numbered-roll samples go to `build/faces/face#`; idle samples go to `build/idle#`. Writes a `meta.json` per animation recording the sub-frame count and the ground-shadow position for each sample. |
| `composite.py` | Per sub-frame: shadow, then an alpha-dilated black outline, then the die. Averages the sub-frames — that average *is* the motion blur — then box-downsamples 512→128. |
| `pack.py` | Lays frames out 10 per row into the sheets `dice.tscn` indexes. |
| `validate.py` | Re-reads `dice.tscn` and checks every atlas region against the PNGs on disk. |

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
- **Sub-frame count scales with angular speed** (`deg_per_sub`), so slow frames near the
  landing cost one render and fast ones cost twenty. Dropping `max_sub` below about 16
  brings back visible ghosting on the first few frames.
- **The camera is orthographic.** `ortho_scale` controls framing without introducing
  perspective convergence as the die moves toward or away from the camera.
- **Both idle loops match the opening speed of a numbered roll.** `idle1` additionally
  cycles through the original animation's rainbow hues as a seamless colour gradient.
- **Frame counts are load-bearing.** `dice.tscn` holds 91 frames per face and 30 per idle
  loop, and `Dice.cs` plays them by name. Changing the counts means regenerating the atlas
  regions in the scene, not just the sheets.

## Adding the other dice (ROADMAP 6b)

`SRC_OBJECT` picks which object in the pack to render, and the pack also holds a D4, D8,
D10, D10-percentile, D12, D20 and a numbered D6.

The honest caveat: the pip and orientation code assumes an **axis-aligned six-faced solid**.
`pip_masks()` classifies vertices by which of the six axis directions they face, and
`rest_quat()` uses a lookup of six axis-aligned rotations. A D8 or D20 needs both replaced
with something that works off arbitrary face normals — find the face planes, pick the
one carrying each number, and build the rotation that brings its normal to +Z. The camera,
material, blur and compositing stages are shape-agnostic and carry over unchanged.
