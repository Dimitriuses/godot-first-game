# Asset provenance

The MIT licence in `LICENSE` covers the **code and project files**, and the die frames under
`assets/dice/`, which were rendered for this repository from a CC0 model. The rest of the
artwork came from elsewhere and keeps its own terms. This file records what each asset is,
where it came from, and how that was established — by measurement where possible.

## `assets/kenney-boardgame/` — CC0, no restrictions

| File | Source | Used by |
|---|---|---|
| `chip-red-white.png` | Boardgame pack v2 by Kenney Vleugels — www.kenney.nl | **nothing, as of August 2026** |
| `piece-black-border04.png` | same pack | `scenes/Player.tscn` |

`chip-red-white.png` was the texture on an invisible `Sprite2D` inside `dice.tscn`. That node
was a leftover and has been removed, so the file is now unreferenced. The board game that
would have used it was dropped in August 2026 (ROADMAP 3), so **both** Kenney sprites are now
dead weight — `piece-black-border04.png` survives only because `scenes/Player.tscn`, itself
unused, still points at it. All of it can go whenever someone wants to.

The pack's own licence file ships alongside them as `LICENSE.txt`:

> CC0 License (Creative Commons Zero) — You may use these graphics in personal and
> commercial projects. Credit (Kenney or www.kenney.nl) would be nice but is not mandatory.

Credit is given here voluntarily. **Thanks to Kenney.**

A second Kenney pack, *Board Game Icons (1.1)* (also CC0), was committed to this repository
in 2025 and has been removed — not one of its 1,543 files was referenced by any scene.

## `assets/petixel-prototype/` — origin not established

| File | Used for |
|---|---|
| `tileset-prototype-b.png` | the board floor and walls |
| `prototype-characters.png` | the figures around the edge of the board |

These came from a pack named *"Petixel Prototype 48x48"*. **No licence file was included
with the copy in this repository**, and the PNGs carry no metadata identifying the author or
terms. The pack was not identified with confidence, so no claim is made about its licence.
If you are the author, please open an issue and it will be credited or removed.

## `assets/dice/` — CC0 source, rendered for this repository

The eight spritesheets that make up the die (`dice_1_sprites.png` … `dice_6_sprites.png`
at 1280×1280, `dice_idle0_sprites.png` and `dice_idle1_sprites.png` at 1280×384, 3.9 MB in
total) were **rendered for this repository in August 2026** from a public-domain 3D model.

The source is **Blend Swap blend #82440, "Dices (D20, D12, D8, D10, D8, D6, D4)"**, released
under [Creative Commons Zero 1.0](https://creativecommons.org/publicdomain/zero/1.0/). The
licence page as published by Blend Swap ships with the download and is kept alongside the
model in `assets/Dice D20 D12 D8 D10 D8 D6 D4/`:

> You are free to use this asset privately for any use you see fit. If you choose to
> distribute copies or modified versions of this asset you must do so under the following
> requirements: **There are no requirements for this license.**

CC0 imposes no conditions, so no attribution is owed. It is given here anyway, as it is for
Kenney above. Only the `D6 Dotted` object is used so far; the same pack also holds a D4, D8,
D10, D10-percentile, D12, D20 and a numbered D6, under the same terms — which is what makes
the other dice cheap to add later.

### How they were rendered

The model supplies geometry only; the look is built on top of it in Blender 4.4 (EEVEE):

- **Toon shading is driven by the world-space face normal**, not by a light. The normal is
  dotted with a fixed direction and pushed through a four-stop constant colour ramp, so each
  face lands on an exact flat colour and the result is deterministic frame to frame. The
  four greys and the red are sampled from the previous artwork, so the die still reads the
  same over the board.
- **The pips are masked from the recessed dimple geometry.** The model's own `Dots` vertex
  group covers 77% of its faces — a square patch around each round dimple — and using it
  directly renders the pips as squares. Selecting the vertices that actually sit below the
  face plane gives the true circular rim instead. The single pip on the `1` face is tinted
  red, as it was before.
- **Motion blur is accumulated, not post-processed.** Each output frame averages up to 20
  separate renders taken across the frame's shutter interval, which is what produces the
  smear on the fast part of the tumble instead of a strobing cube.
- **The silhouette outline and the ground shadow are composited per sub-frame**, before
  accumulation, so they smear with the die rather than being pasted onto a blurred image.
- Frames are rendered at 512px and box-downsampled to 128px, giving 4× supersampled edges.
  The previous frames came out of a GIF and had binary alpha — 0 partially transparent
  pixels — so the new ones are genuinely antialiased.

The tumble is deliberately faster than real physics, decaying over 66 frames into a damped
settle; the die lands on its stated face on every one of the six clips.

**These frames are covered by this repository's MIT licence.** The source model is CC0, and
the render setup, animation and compositing are original work.

### Resolution

The sheets are 128px cells drawn at 128px, replacing 512px cells that were drawn at 128px.
That is the four-times-oversized artwork noted in `KNOWNISSUES.md`, now fixed: 3.9 MB
instead of 18.6 MB, for the same on-screen result.

## `icon.svg`

The stock Godot Engine project icon, unmodified.
