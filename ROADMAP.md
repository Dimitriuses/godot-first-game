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

## 2. Show the result

`game.tscn` already has an unused `Label` in the bottom-right corner. Bind it to the
`DiceRolled` signal. Currently the result exists only in the console, so an exported build
reports nothing at all.

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
- Port all three scripts to GDScript (~160 lines) and keep a GDScript branch for the demo.
- Publish a downloadable Windows build under Releases instead — much less valuable than a
  link, but it works today and the export preset is already configured.

## 6. Regenerate the die animation from scratch

The die animation is the reason this toy is worth looking at, and it is also the one asset
the project does not have clean rights to — it was reworked from a custom Telegram sticker
pack of unknown authorship (see [docs/ASSETS.md](docs/ASSETS.md)). Both problems have the
same solution: **redraw it.**

Not a substitution. Dropping in a flat CC0 dice sprite would settle the licensing and throw
away the visual appeal in the same move. What the replacement needs to keep:

- a **3D look** — the die reads as a solid cube tumbling, not a flat icon spinning;
- **artistically exaggerated rotation**, faster and more emphatic than real physics would
  give, because that is what makes the throw feel good;
- the existing structure: six landing animations (`1`–`6`) plus two idle/spin loops
  (`idle0`, `idle1`), so nothing in `Dice.cs` has to change.

Likely route is to render the cube in 3D, export a frame sequence per face, and pack it to
spritesheets. Worth fixing the resolution while doing it: the current frames are 5120×5120
sheets of 512 px cells drawn on screen at 128 px — four times larger than needed, and
18.6 MB for one die.

**Deferred deliberately.** Doing this properly is a real chunk of art work, not a
find-and-replace, and it is too expensive to take on right now. Until it happens the
disclosure in `docs/ASSETS.md` stands.

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
