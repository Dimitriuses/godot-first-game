# Roadmap

This started as a first look at Godot 4 and stopped at "the die works". The board underneath
it was drawn for a board game that was never wired up — and **as of August 2026 it never will
be.** The project is a dice-rolling sandbox: throw dice, watch them tumble, read the numbers.
No players, no turns, no win condition. Items 3 and 4 are dropped for that reason. Two large
pieces of work remain, both planned rather than started: **item 8**, adding the rest of the
dice from the source pack, and **item 9**, getting a playable build onto GitHub Pages.

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

Still missing, and worth a follow-up: the value shown is the one `Dice.Roll()` drew from
`System.Random`, not one read off the die. Item 1 is what makes the number mean anything;
this item only makes it visible.

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

It grew from "an option we cannot take" into the largest single piece of work left,
so it lives at the end of the file now rather than in the middle.

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

## 7. Stop the die tunnelling through the walls — investigation

Thrown hard enough, especially into a corner, the die passes through a wall instead of
bouncing. Thickening the wall colliders was tried and barely helped. The teleport-and-zero-
velocity recovery works and should stay, but it is a safety net, not a fix.

Next things to try, in order: enable `continuous_cd` on the `RigidBody2D` (off by default,
and the engine's intended answer to tunnelling); clamp release velocity so a flick cannot
impart an unbounded impulse; raise the physics tick rate; and look hard at the corners
specifically, where four separate collision rectangles meet with no overlap and a diagonal
approach may be threading the seam between two shapes rather than passing through either.

Full detail and what has already been ruled out is in [KNOWNISSUES.md](KNOWNISSUES.md),
issue 4.

## 8. Add the rest of the dice from the pack — planned, not started

The CC0 source pack (`assets/Dice D20 D12 D8 D10 D8 D6 D4/`, Blend Swap #82440) holds eight
solids, of which one is done:

| Object | Faces | State |
|---|---|---|
| `D6 Dotted` | 6 | **done** — this is the die in the game |
| `D4` | 4 | not started |
| `D6 Numbered` | 6 | not started |
| `D8` | 8 | not started |
| `D10` | 10 | not started |
| `D10 Percentile` | 10 | not started, and needs its own value handling |
| `D12` | 12 | not started |
| `D20` | 20 | not started |

**76 faces in total.** That number is the whole problem, so start there.

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

That is **12× the current 3.61 MB**, in PNGs, in git forever. It would also
mean ~7,400 `AtlasTexture` sub-resources against the 606 in `dice.tscn` today. Render cost is
*not* the problem — roughly an hour of Blender plus half an hour of compositing, once.

**The obvious saving does not work.** The plausible fix is to share the fast opening frames
between the faces of one die, since a die blurred beyond recognition should look the same
whichever face it will land on. Measuring the six committed clips says otherwise: they differ
*most* at frame 0 (mean abs difference 47) and converge steadily to the end (2.2 at frame 90).
That is because `FACE` in `render.py` gives every face its own turn count, tumble axis and
drift, deliberately, so the six throws do not read as the same clip. They are six different
throws that happen to end differently, not one throw with six endings.

Sharing is therefore still possible but is **not free**: it needs the per-face variation
removed so that all faces of a die share one tumble and diverge only as the spin decays,
which means re-rendering the d6 as well and giving up some of the variety. The crossover
frame would then have to be measured rather than guessed — the same diff, run against the
re-rendered clips.

**This decision comes before any rendering.** Options, roughly:

1. **Ship a subset.** A d6 and a d20 is what most people actually want; 26 faces is ~15 MB.
2. **Share the tumble.** Re-render everything with one throw per die type, per-face only
   after the spin decays. Perhaps half the size, at the cost of variety.
3. **Shorten the clips.** The landing carries the weight; the long entry does not.
4. **Generate on demand.** Do not commit the sheets. Breaks clone-and-run, needs Blender.
5. **Accept ~44 MB.** Honest, and undoes the 2026 cleanup that took the repo from 2,741 files
   to 43 and the die art from 18.6 MB to 3.9 MB.

### 8b. Which numeral is on which face cannot be derived

The d6 was tractable because its faces carry *pips*: `pip_masks()` counts connected clusters
of recessed geometry and gets 1–6 for free, with opposite faces summing to 7 as a check.
Every other die in the pack is alphanumeric. You cannot count a glyph.

The good news is that the recessed-vertex trick still applies — the numerals are indented
exactly as the pips are, so the existing `DotMask` approach should colour them without
change. What is missing is only the mapping from face to value.

Proposed, and cheap: render every face flat-on into one indexed contact sheet, read it once
by eye, and commit the table beside the pipeline. Eight tables, 76 entries, done once. Watch
for the **6/9 ambiguity** — dice normally disambiguate with an underline or a full stop, and
whichever this model uses has to be recorded so the table is not silently wrong by two faces.

### 8c. The pipeline assumes a cube

`tools/dice-render/render.py` is written for an axis-aligned six-faced solid:

- `pip_masks()` classifies vertices by which of the six axis directions they face (`AXES`).
- `rest_quat()` looks up one of six axis-aligned Euler rotations.

Both need replacing with something that works off arbitrary face normals: group the mesh's
polygons into planes, take each plane's normal, and build `Quaternion(normal, +Z)` to present
that face to the camera. The camera, toon material, motion-blur accumulation and compositing
stages are all shape-agnostic and carry over untouched.

Two quirks to plan for:

- **The d4 has no up-face.** It rests on a face and the value is read at the apex, so
  "present value *v* to the camera" is a different construction from the other dice.
- **The percentile d10 shows 00–90**, not 1–10, so its animation names and its HUD value
  need a display concept the code does not currently have.

### 8d. Code that hardcodes six

Small and mechanical, but it is real work:

- `Dice.cs` — `WrapFace` is `% 6`, `ChooseResult` maps the release frame onto `* 6 /
  idleLength`, and `PlaceOnFace` bounds-checks `< 1 or > 6`. All need a face count, either
  exported or counted from the numeric animations in `SpriteFrames`.
- `GameManager.DiceScene` is a single `PackedScene`; it needs one per die type.
- `DicePalette` offers exactly one entry, `AddDieOption(list, "D6", ...)`, and one generated
  icon. It needs an entry and an icon per type.
- `DiceHud` labels dice `D{Id}` where `Id` is a sequence number, which collides confusingly
  with d6/d20 notation the moment there is more than one type. Rename before adding dice, not
  after. Its **Total** also needs a decision once a percentile d10 is in the mix.

### 8e. `dice.tscn` has to be generated

The scene holds 606 `AtlasTexture` sub-resources written out one per frame. A d20 alone would
need about 2,000, and the set about 7,400. Nobody maintains that by hand, and the existing
`retarget_tscn.py` only rescales what is already there.

A generator that writes a die scene from a directory of sheets is a **prerequisite**, not
cleanup afterwards. `tools/dice-render/validate.py` already checks a scene's regions against
the PNGs on disk and generalises to whatever the generator emits.

### Suggested order

1. Decide 8a. Nothing else is worth doing until the size question has an answer.
2. Write the scene generator (8e) and point `validate.py` at its output.
3. Generalise the geometry (8c) and prove it on `D6 Numbered` — six faces, known answers,
   directly comparable against the die already shipping.
4. Build the face-to-value tables (8b).
5. Generalise the code (8d), with the `DiceHud` rename first.
6. Render whichever dice 8a settled on.

## 9. A browser demo, by transpiling C# to GDScript — planned, not started

A playable GitHub Pages build is the single most valuable thing this repository could gain,
and it is still blocked. Godot's own documentation, checked August 2026, is blunt about it:

> Projects written in C# using Godot 4 currently cannot be exported to the web.

So waiting is not a plan. The route is to make a GDScript copy of the gameplay code and export
*that*, and the proposal here is to generate the copy rather than maintain it by hand.

One piece of good news up front, because it removes the trap most Pages deploys hit: since
Godot 4.3 the **single-threaded** web export is the default, and it does not need the
`Cross-Origin-Opener-Policy` / `Cross-Origin-Embedder-Policy` headers that `SharedArrayBuffer`
requires. GitHub Pages cannot set custom headers, so this project must stay on the
single-threaded export — and by default it already would.

### Is a translator the right call?

Honestly: **for cost alone, no.** The gameplay is about 940 lines across five scripts. Porting
it to GDScript by hand is roughly a day's work. Writing a C#-to-GDScript translator is weeks,
and a *general* one is a research project.

The translator wins on a different axis, and it is a real one: a hand port creates a second
copy that silently rots the moment either side changes. Automating it means the GDScript build
is never stale and is never something anyone has to remember.

Two things make it more attractive here than it would usually be:

- **The C# subset in use is small.** Measured across `scripts/`: 19 pattern matches, 14
  lambdas, 8 generic instantiations, 5 expression-bodied properties, 3 interpolated strings, 2
  C# `event`s, 2 `static readonly` fields — and **no LINQ, no `async`/`await`, no `enum`s, no
  interfaces, no inheritance beyond Godot base classes**. A translator does not have to
  understand C#; it has to understand *this* C#.
- **The project already has two deterministic oracles** (see 9d), which is unusual, and is what
  turns "does the translator work?" from a judgement call into a CI gate.

If the translator is wanted as its own artifact — which is a perfectly good reason — it is
feasible at this scope. If the only goal is a demo on Pages, hand-port once and spend the saved
weeks on item 8.

### 9a. The translator

The instinct to build an AST is right, and the tool is **Roslyn**
(`Microsoft.CodeAnalysis.CSharp`). One correction worth making early: parsing is the easy part.
Use the **semantic model**, not just the syntax tree — without type resolution you cannot tell
what `Play` means on `AnimatedSprite`, and name mapping degenerates into guesswork. That means
building a `Compilation` with references to the Godot assemblies, not just calling
`CSharpSyntaxTree.ParseText`.

**The trick that makes this tractable: do not hand-write the Godot API name table.** Godot can
dump its entire API with `godot --dump-extension-api`, producing an `extension_api.json` of
every class, method, property, signal and enum in **snake_case**. The C# bindings are generated
from that same data in PascalCase. Pairing the two gives an authoritative symbol map for the
whole engine, generated rather than maintained, and it regenerates when Godot updates.

What is left is the semantic gap, and that is where the work actually is:

| C# in this project | GDScript |
|---|---|
| `[Signal] delegate void DiceRolledEventHandler(int)` | `signal dice_rolled(result: int)` |
| `event Action<Dice> DeleteRequested` (`DiceHud`) | no equivalent — becomes a signal or a `Callable` |
| `body is not Dice other` | an explicit `is` test plus an assignment |
| `face is < 1 or > 6` | expanded to `face < 1 or face > 6` |
| `public bool IsHeld => ...` | no expression-bodied properties; a `func` or a getter block |
| `Dictionary<Dice, Vector2>`, `List<Dice>` | `Dictionary`, `Array` — untyped |
| `HashSet<Dice>` (`GameManager.deletingDice`) | **no equivalent**; a `Dictionary` used as a set |
| `$"Total: {total}"` | `"Total: %d" % total` |
| `static readonly StringName Idle0 = "idle0"` | `const IDLE0 := &"idle0"` |
| `partial class`, attributes, namespaces | dropped |
| closures capturing `die` in `RegisterDie` | GDScript lambdas, with different capture rules |

Scope it deliberately: **translate the subset, and fail loudly on anything outside it.** A
translator that refuses to emit is far better than one that emits something subtly wrong, and a
hard failure is what keeps CI honest.

### 9b. Scenes and project files — the part that gets forgotten

The scripts are not the only thing that names C#:

- Every `.tscn` points at a script by path and UID; those must be repointed at `.gd` files, and
  the `.uid` sidecars regenerated.
- **Exported property keys are stored under their C# names.** `dice.tscn` contains
  `AnimatedSprite = NodePath("AnimatedSprite2D")`; the GDScript export would be
  `animated_sprite`, so the key has to be renamed. Miss this and the scene loads with null
  exports and no error.
- `project.godot` needs `"C#"` dropped from `config/features` and its `[dotnet]` section
  removed.
- `export_presets.cfg` has only a Windows preset today. A `Web` preset is needed, on the
  single-threaded default.
- `dice.tscn` is ~4,300 lines of which 606 are `AtlasTexture` regions. The rewriter must leave
  them completely alone — `tools/dice-render/validate.py` already proves that and should run
  after the rewrite.

### 9c. The pipeline

A GitHub Actions workflow on `ubuntu-latest`:

1. Install the .NET 8 SDK (for the translator) and Godot **standard**, not .NET — the whole
   point is that the exported project contains no C#.
2. Fetch the matching web export templates.
3. Run the translator, then the scene/project rewriter (9b), into a scratch directory.
4. `godot --headless --import` to build the import cache, then
   `godot --headless --export-release "Web" build/index.html`.
5. `actions/upload-pages-artifact` + `actions/deploy-pages`.

Keep the generated GDScript **out of the repository**. Committing it recreates exactly the
stale second copy the translator exists to avoid.

### 9d. Proving it works

This is what makes the idea defensible, and the machinery already exists:

- **The headless test harness** (see [CLAUDE.md](CLAUDE.md)) drives real scenes and asserts on
  real engine state, currently 18 checks. Translate the harness too, run it against both
  builds, and require identical output. That is the correctness gate.
- **The screenshot pipeline** (`tools/screenshots/`) is byte-deterministic — two runs produce
  identical files. Render `docs/screenshot.png` from the C# build and from the GDScript build
  and diff them. They should match exactly, which catches rendering and scene-wiring
  differences the test harness cannot see.

Two independent oracles, both built for other reasons, both automatable.

### Suggested order

1. Add a `Web` export preset and confirm a **hand-stubbed** GDScript project exports and runs
   on Pages. Prove the deployment before building anything to deploy into it.
2. Build the scene/project rewriter (9b). It is small, and it is needed whichever route wins.
3. Generate the API name map from `--dump-extension-api` and check it against the calls that
   actually appear in `scripts/`.
4. Translate one script end to end — `Dice.cs` is the right one: the most self-contained, and
   the harness covers it hardest.
5. Only then decide whether to finish the translator or hand-port the remaining four.
