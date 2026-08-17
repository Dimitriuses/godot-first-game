# First Game — a physics dice sandbox in Godot 4 + C#

![Godot](https://img.shields.io/badge/Godot-4.4.1%20.NET-478cbf)
![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![License](https://img.shields.io/badge/license-MIT-green)

Drag dice from the right-side palette, sling them across a pixel-art board, and let go —
they tumble, land, and report a number. My first project in **Godot 4**, written in **C#** in May–June
2025 while learning the engine's 2D physics: `RigidBody2D`, `PinJoint2D` mouse dragging,
`Area2D` bounds detection and `AnimatedSprite2D`.

![the die tumbling after a throw](docs/roll.gif)

## Controls

| Input | Action |
|---|---|
| **Tab** or **right-edge arrow** | open or close the dice palette |
| **Drag D6 from the palette** | add another die to the board |
| **Left-click and drag the die** | grab it — a pin joint follows the cursor, so you can fling or spin it |
| **Release the left button** | let go and roll |
| **Hold Shift, then drag a die** | select and throw every die together |
| **Space** | roll every die once, simultaneously |
| **Total button** | open or close the bottom-left die list |
| **Hover a die-list row** | highlight its die on the board |
| **× in a die-list row** | remove that die |
| **Delete All** | remove every die from the board |
| **"Respawn" button** | arrange all dice near the centre and kill their velocity |

There is no score and no way to lose. It is a toy, not a game.

![the board at rest](docs/screenshot.png)

## Status

**It builds and runs.** Verified on 2026-07-29 against Godot 4.4.1 (.NET build) and the
.NET 8 SDK: `dotnet build` completes with **0 warnings, 0 errors**, the game launches, the
drag-and-roll path works, and the Respawn button recentres the die.

It is also **unfinished, and finished-looking in a misleading way.** The board is a board
game's board, and there is a `Player` script and a ring of eight cell coordinates in the
history to prove that was the plan — but nothing ever moves around it. What exists is the
die.

Two things a reviewer should know before reading the code, because neither is visible from
the screenshot:

- **The physics does not decide the number.** `Dice.Roll()` calls `random.Next(1, 7)` and
  then plays the animation for that face. The tumble you see is a pre-rendered sprite
  animation chosen *after* the result. The die's actual orientation when it stops is
  unrelated to the number reported.
- **You cannot see the result in-game.** The roll is written to the console with
  `GD.Print`. There is a `Label` node sitting in the scene for it, but no code ever writes
  to it — so in an exported build the number goes nowhere.

Neither was changed during the 2026 cleanup; see *Known limitations*.

## Running it

Requires the **.NET / Mono build** of Godot — the standard build cannot run C# projects.

```bash
# Godot 4.4.1 (.NET), with the .NET 8 SDK on PATH
godot --path .            # or: open project.godot from the Godot project manager
```

To build the C# assembly on its own:

```bash
dotnet build FirstGame.csproj
```

Exporting a Windows binary uses the committed preset, which writes to `builds/`
(gitignored).

**There is no browser demo, and there cannot be one yet.** Godot's .NET flavour does not
support the Web platform — the installed `4.4.1.stable.mono` export-template set contains
Windows, Linux, macOS, Android and iOS templates and **no web templates at all**. A
playable Pages demo would mean porting the C# gameplay scripts to GDScript.

## Layout

```
scenes/     game.tscn (board, walls, bounds area, UI), dice.tscn, Player.tscn
scripts/    GameManager.cs   drag/release, respawn, bounds handling
            Dice.cs          roll result + face animations
            DicePalette.cs   right-side drag-to-spawn dice menu
            DiceHud.cs       sorted die values, total and deletion controls
            Player.cs        board-piece stub, not instantiated
assets/     dice/            die animation frames  (see docs/ASSETS.md)
            petixel-prototype/  tileset and figures
            kenney-boardgame/   chip and piece sprites (CC0)
docs/       screenshot, GIF, asset provenance
```

The gameplay and runtime UI are implemented in C#.

## Known limitations

Everything here was reproduced by running the project, not inferred from reading it.

- **The result is random, not physical** (above). A die whose face is decided by
  `System.Random` is a slot machine with extra steps.
- **The result is invisible in-game.** The `Label` in `game.tscn` is never written to; the
  only output is `GD.Print`.
- **The die can re-roll itself.** `_PhysicsProcess` calls `Roll()` whenever an idle
  animation is playing and the die is not being dragged, so a roll can start without the
  player doing anything.
- **The die tunnels through the walls on fast throws**, and corners are noticeably the worst
  spot. Thickening the wall colliders barely helped; continuous collision detection
  (`continuous_cd`, off by default in Godot) has not been tried yet. The out-of-bounds
  handler that teleports the die back and zeroes its velocity is the mitigation, and it
  works. Under investigation — see `KNOWNISSUES.md`.
- **Every session ends with a spurious out-of-bounds message.** Separately from the above,
  `BodyExited` also fires once during scene teardown, on the same frame as `_ExitTree`,
  where the deferred recentre can never be flushed. Harmless, but it muddies the console
  while you are chasing the real bug.
- **Nothing moves until you touch it.** Gravity is disabled (top-down board), so the opening
  scene is completely static: 1,800 consecutive rendered frames are byte-identical.
- **The board game was never built.** `Player.cs` is not instantiated by anything.
- **No tests, no CI.** The engine-facing gameplay code is currently verified manually.
- **Console output is in Ukrainian**, mixed with English identifiers.
- **`CollisionShape2D` is scaled 6×** in `dice.tscn` rather than being authored at its real
  size — something Godot explicitly advises against.
- ~~**The die artwork is third-party with unknown terms.**~~ Fixed in August 2026: the
  animation was re-rendered from a CC0 model and is now 3.9 MB instead of 18.6 MB — see
  [docs/ASSETS.md](docs/ASSETS.md).

`KNOWNISSUES.md` carries the same list with the measurements; `ROADMAP.md` records what the
project was heading towards.

## History

One commit, `import to github` (2025-06-01) — the project was developed locally and pushed
in a single dump, so there is no incremental history to read.

A cleanup pass in July 2026 touched packaging only and **changed no behaviour** (proved by
re-rendering: 1,800 frames byte-identical before and after). It removed 2,698 committed
asset files that no scene referenced — two whole Kenney packs, of which exactly two files
were in use — along with a stray editor temp file, 26 lines of commented-out code and three
dead fields; renamed every path containing a space or a `!` (`First Game.csproj` →
`FirstGame.csproj`, `assets/Petixel Prototype 48x48/!Prototype_Characters.png` →
`assets/petixel-prototype/prototype-characters.png`); fixed the `ide0`/`ide1` animation
names to `idle0`/`idle1`; and pointed the export preset at `builds/` instead of a
mistyped `../Relises/` outside the repository. The tree went from **2,741 tracked files to
43**.

The pre-cleanup state is tagged **`v0.1-original`**.

## Licence

[MIT](LICENSE) for the code. **Not** for the artwork — see
[docs/ASSETS.md](docs/ASSETS.md), which includes one asset set whose origin could not be
established and one that is third-party with unknown terms.
