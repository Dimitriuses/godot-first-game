# Roadmap

This started as a first look at Godot 4 and stopped at "the die works". The board underneath
it was drawn for a board game that was never wired up. What follows is the order the
remaining work naturally falls into, roughly cheapest-first.

## 1. Make the physics decide the number

The one change that would make the project honest, and the reason to come back to it. Give
the die a real up-face, wait for angular velocity to fall below a threshold, read the face,
*then* report. See issue 1 in [KNOWNISSUES.md](KNOWNISSUES.md).

Worth doing with tests: the "which face is up for this orientation" function is pure, and
is the only part of this project that would benefit from any.

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

## 3. Finish the board game the board implies

The scaffolding that survives from the original attempt:

- `scripts/Player.cs` — a `Node2D` with a `CellIndex` and a `MoveToCell(Vector2)`. Never
  instantiated by anything.
- `scenes/Player.tscn` — the piece, using a Kenney black playing piece.
- Removed in the July 2026 cleanup but preserved in `v0.1-original`: a `cellPositions`
  array of eight `Vector2`s in `GameManager`, and the commented-out `_Ready` block that
  would have spawned the player onto `cellPositions[0]`.

Worth knowing before reusing that array: **its coordinates do not match the board that
exists.** The eight cells form a ring inside a 100–300 px box, while the drawn board spans
roughly 1150 × 670 px. They are placeholders from an earlier prototype, not measurements of
the tilemap.

So the work is: lay out real cell coordinates against `TileMapLayer`, instantiate the
player, move it by the rolled number, and decide what happens when it lands.

## 4. Turn order and a second player

Nothing here is written. `Player` has no concept of ownership and `GameManager` tracks no
turn.

## 5. A browser demo — blocked, and not by laziness

A playable GitHub Pages build is the single most valuable thing this repository could gain,
and it is **not currently possible**. Godot's .NET flavour does not support the Web
platform: the installed `4.4.1.stable.mono` export-template set ships Windows, Linux, macOS,
Android and iOS templates and **no web templates at all**.

The options, none of them cheap:

- Wait for .NET web export to land in a future Godot release.
- Port the gameplay scripts to GDScript and keep a GDScript branch for the demo. This got
  considerably more expensive during 2026: it was three scripts and ~160 lines, and the
  multi-dice work took it to five scripts and ~940, most of it runtime UI.
- Publish a downloadable Windows build under Releases instead — much less valuable than a
  link, but it works today and the export preset is already configured.

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

### 6b. The other dice — the cheap part now

The CC0 pack also contains a D4, D8, D10, D10-percentile, D12, D20 and a numbered D6, all
manifold and all under the same terms, and the whole render pipeline is parameterised by
which object it points at. Adding them is mostly deciding what the *game* does with a d20:
`Dice.Roll()` hardcodes `Random.Shared.Next(1, 7)`, `dice.tscn` names its animations
`"1"`…`"6"`, and `DicePalette` offers exactly one die type to drag out. Worth doing **after**
item 1 — once the physics decides the number, "which face is up" generalises to any solid,
and adding dice becomes a data change rather than six more hardcoded animations.

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
