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
| **Left-click and drag the die** | grab it — a pin joint follows the cursor |
| **Move a held die about** | spin it up: gentle movement plays `idle0`, sharp movement `idle1`. Once spinning it keeps tumbling in your hand until you let go |
| **Release the left button** | a die you spun rolls; a die you never agitated just sits where you dropped it |
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

**It builds and runs.** Verified on 2026-08-18 against Godot 4.4.1 (.NET build) and the
.NET 8 SDK: `dotnet build` completes with **0 warnings, 0 errors**, and a 240-frame headless
run of `game.tscn` starts clean with no errors or warnings. The dice state machine was
checked by 20 assertions driven headlessly — see [CLAUDE.md](CLAUDE.md).

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
- **The number shown is not the number rolled physically.** As above — the HUD faithfully
  reports whatever `System.Random` picked, which is not what the die did.

The first point is untouched and is the headline item in [ROADMAP.md](ROADMAP.md); see
*Known limitations* for the rest.

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
tools/      dice-render/     offline Blender pipeline that produces assets/dice/
docs/       screenshot, GIF, asset provenance
```

The gameplay and runtime UI are implemented in C#.

## Known limitations

Everything here was reproduced by running the project, not inferred from reading it.

- **The result is random, not physical** (above). A die whose face is decided by
  `System.Random` is a slot machine with extra steps.
- ~~**The result is invisible in-game.**~~ Fixed: `DiceHud` lists every die with its value
  and a running total. The spare `Label` in `game.tscn` is still unused, though — the HUD
  was built instead of binding it.
- ~~**The die can re-roll itself.**~~ Fixed: `idle0`/`idle1` now play **only while a die is
  held**, so a free die is never left in an idle state for anything to observe and act on.
  Rolls start from exactly three places: releasing a die you spun, a hard enough die-to-die
  collision, and Space.
- **The die tunnels through the walls on fast throws**, and corners are noticeably the worst
  spot. Thickening the wall colliders barely helped; continuous collision detection
  (`continuous_cd`, off by default in Godot) has not been tried yet. The out-of-bounds
  handler that teleports the die back and zeroes its velocity is the mitigation, and it
  works. Under investigation — see `KNOWNISSUES.md`.
- **`BodyExited` still fires once during scene teardown**, on the same frame as `_ExitTree`,
  where the deferred recentre can never be flushed. It is now silent — the handler prints
  nothing, so it no longer muddies the console — but the event is still unguarded.
- **Nothing moves until you touch it.** Gravity is disabled (top-down board), so the opening
  scene is completely static: 1,800 consecutive rendered frames are byte-identical.
- **The board game was never built.** `Player.cs` is not instantiated by anything.
- **No tests, no CI.** Nothing is committed. The dice rewrite was verified with a throwaway
  headless harness that was then deleted; the recipe is in [CLAUDE.md](CLAUDE.md), and
  making it permanent is the cheapest real improvement available here.
- ~~**Console output is in Ukrainian.**~~ No longer true: there is no non-ASCII text left in
  `scripts/`, and the one remaining `GD.Print` is `"Rolled: " + result`.
- ~~**`CollisionShape2D` was scaled 6×.**~~ Fixed: the die now uses an unscaled 32 px-radius
  collider that follows the visible body more closely.
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

August 2026 was the first pass that **did** change behaviour. The die animation was
re-rendered from a CC0 model, settling the one licensing question and cutting the artwork
from 18.6 MB to 3.9 MB (ROADMAP 6). The board then grew multiple dice, a drag-out palette
and a value HUD, which closed ROADMAP 2. Finally `Dice.cs` was rewritten as an explicit
three-state machine after the drag work left it freezing on a single frame — the cause was
`AnimatedSprite2D.Stop()` rewinding to frame 0 where `Pause()` was meant, and the workaround
code built on top of that had been fighting a stall it was itself causing.

## Licence

[MIT](LICENSE) for the code, and for the die frames under `assets/dice/`, which were
rendered for this repository from a CC0 model. **Not** for the rest of the artwork — see
[docs/ASSETS.md](docs/ASSETS.md), which still includes one asset set whose origin could not
be established.
