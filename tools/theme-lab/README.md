# Theme lab

A bench for [`shaders/dice_theme.gdshader`](../../shaders/dice_theme.gdshader), which
recolours a die **without re-rendering it**. Run it and look at the results:

```sh
godot --path . res://tools/theme-lab/theme_lab.tscn --quit-after 2000
```

It writes `out/` — one page per theme showing every mode, a `gallery.png` of every theme
in the mode that ships, and a sweep page per tunable. `out/` is gitignored; the pages are
cheap to regenerate and there is no point versioning pictures of a shader. A window opens
for a few seconds, because `--headless` has no renderer.

## What the artwork gives you to work with

`render.py` toon-shades from a fixed six-colour palette — four body bands (`#F1F0F7`
`#D6DCEA` `#BDBECF` `#929AAB`) and one near-black `#121212` — and `composite.py` draws the
outline in **that same `#121212`**. Two consequences that decide the whole design:

- **Swapping exact colours is hopeless.** Motion blur and antialiasing smear those six
  values into 9,565 distinct opaque colours in a single d6 sheet, and only 38% of pixels
  land on the top eight. What survives the blur is *luminance*, so every mode here is a
  function of it.
- **Nothing separates a glyph from the outline by colour.** They are the same value. Only
  geometry tells them apart: an outline pixel has transparency within a couple of pixels
  of it, a pip does not.

The luminance bands are far apart, which is what makes this tractable at all: glyphs sit
at 0.06–0.16 and the body at 0.60–0.94, with nothing much in between except blur.

## The modes, and what happened to them

| Mode | What it does | Verdict |
|---|---|---|
| 0 original | passthrough | reference |
| 1 multiply | `rgb *= tint` | works, but only ever darkens, and the shading bands crush at saturated tints |
| **2 body ramp** | body luminance through a gradient; glyphs and outline untouched | **ships** |
| 3 body+glyph | as 2, plus a separate glyph colour, outline kept dark by geometry | the only way to get light glyphs; unstable under blur — see below |
| 4 full ramp | everything through the gradient | rejected: the outline goes with it and the die stops having an edge |

**Mode 2 is the recommendation.** It is one extra texture read, it is stable on resting and
blurred frames alike, and it cannot damage the silhouette because it never touches anything
dark. `bone` reproduces the original artwork almost exactly, which is the sanity check that
the luminance mapping is faithful rather than merely pretty.

**Mode 3 is the interesting failure.** It is the only route to a black die with white
numbers, and it works on resting frames once two things are fixed — the drop shadow has to
be excluded (it is dark but semi-transparent, so an alpha gate catches it) and
`outline_reach` has to match the real outline width. But its glyph threshold has no good
value: resting frames want `glyph_cut` around 0.50 to fill thin d20 numerals in, and
motion-blurred frames want 0.30 or the blurred body dips below the cut and flashes white.
A die spends its loudest three seconds motion-blurred, so that is not a trade you can take.
Left in the shader, off by default, with both numbers measured rather than chosen:

- `outline_reach = 2.5` — `composite.py` dilates with a 9px gaussian at 512, which is a
  little over 2px once the sheet is down at 128. Below 2px the rim itself takes the glyph
  colour; above 3px the d20's numbers, which sit within a few pixels of the silhouette,
  get eaten. See `out/sweep_outline_reach.png`.
- `glyph_cut = 0.50` for stills. See `out/sweep_glyph_cut.png` for why nothing works for
  both stills and blur.

## The rainbow clip

`out/idle1.png` is its own page because `idle1` is its own problem. The fast spin is the
one clip not rendered in the grey palette: `set_palette_gradient` sweeps it through a hue
cycle at saturation 0.55, where every other clip in the pack measures under 0.09. Under a
theme that goes wrong twice —

- the body **pulses** as the hue sweeps, because luminance follows the tint: yellow frames
  read bright and blue ones dark, for no reason a player can see;
- the sub-cut pixels the body ramp deliberately keeps are dark **rainbow** rather than
  near-black, so a crimson die spins up with a gold rim round it.

Both are fixed, and the fix for the first falls out of the renderer's own arithmetic: the
tint is `hsv_to_rgb(hue, 0.55, 1.0)`, so its brightest channel is always 1.0 — which makes
the pixel's brightest channel the band's own shade with the hue divided back out. Read the
shade from `max(rgb)` there instead of from luminance and the pulse is gone.

Both are gated on a `rainbow` uniform that `Dice.cs` sets from the clip that is playing.
**Not** on a per-pixel chroma test, which is the obvious idea and is wrong: the d6's red
pip is *more* saturated than the rainbow, so any such test greys out the pip and misreads
the clip at once. An unthemed die is unaffected — Bone has no material, so the rainbow
plays as rendered.

## The browser demo, and single-threaded builds

Written with [ROADMAP](../../ROADMAP.md) item 9 in mind, and it survives that target:

- **Threading is not a shader question.** `thread_support` in a Web export controls
  WebAssembly threads and `SharedArrayBuffer`; shaders compile and run on the GPU through
  WebGL2 either way. GitHub Pages cannot send the COOP/COEP headers `SharedArrayBuffer`
  needs, so the demo must be the single-threaded variant — and nothing here cares.
- **The renderer already matches.** Web supports only the Compatibility renderer, which is
  what `project.godot` has always used, so the lab is exercising the same backend the demo
  would.
- **Everything used is GLSL ES 3.00.** `textureSize`, a `sampler2D` function parameter,
  `smoothstep`/`mix`/`clamp`. No derivatives, no sampler arrays, nothing WebGL1-era.
- The taps in `near_edge` use **`textureLod`, not `texture`**, because they sit inside a
  branch on the pixel's own alpha — non-uniform control flow, where implicit-derivative
  sampling is undefined in that dialect. Switching them produced byte-identical output on
  every page in `out/`, so it costs nothing and removes a class of driver-dependent bugs.
- **One caveat that is real.** A single-threaded build has no background shader
  compilation, so the first draw that uses the shader stalls. All dice share one `Shader`
  resource and uniform values do not create variants, so it is one program however many
  dice or themes exist — warm it once during load rather than on the first throw.
- **Mode 2 is also the cheap one**: two texture reads against mode 3's ten. On a phone GPU
  in a browser that is not nothing.

What cannot be settled from here is the last mile. Godot compiles shaders at runtime in the
browser, not at export, so only an actual WebGL2 context proves it — and a Web export is
refused outright while the project is C#. Put it on item 9's checklist.

## A theme is a gradient

Four stops, deepest shade to highlight, matching the four bands the die was rendered with.
That is the whole of a theme — no code, no images.

Shipped as [`scripts/DiceTheme.cs`](../../scripts/DiceTheme.cs), which this lab reads rather
than copies: a bench that disagrees with what the player sees is worse than no bench. Three
things about it that are decisions rather than details:

- **Bone is no material at all**, not a material that reproduces the artwork. An unthemed
  die is then the original by construction, which is worth more than a close match and is
  one less shader to compile.
- **One material per theme, shared by every die wearing it.** The uniforms are identical, so
  there is nothing to vary; it also keeps a single-threaded web build to one program.
- **The palette, the drag ghost and the die-list thumbnails all take the same material.**
  They draw the same frames through plain `TextureRect`s, and without it a themed die would
  be the only thing in the window that is themed.
