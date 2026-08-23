# First Game — a physics dice sandbox in Godot 4 + C#

![Godot](https://img.shields.io/badge/Godot-4.4.1%20.NET-478cbf)
![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![License](https://img.shields.io/badge/license-MIT-green)
[![Play it](https://img.shields.io/badge/play%20it-in%20your%20browser-e6484f)](https://dimitriuses.github.io/godot-first-game/)

### ▶ [Play it in your browser](https://dimitriuses.github.io/godot-first-game/)

No install, no account. It runs on a phone too — drag with a finger, double-tap for the
menus, shake to throw the board.

Drag any of **eight dice** — d4, d6, d8, d10, d12, d20, a numbered d6 and a percentile d10 —
from the palette down the left, sling them across a pixel-art board, and let go: they tumble, land
and report a number. My first project in **Godot 4**, written in **C#** in May–June 2025 while
learning the engine's 2D physics: `RigidBody2D`, `PinJoint2D` mouse dragging, `Area2D` bounds
detection and `AnimatedSprite2D`.

![the die tumbling after a throw](docs/roll.gif)

## Controls

| Input | Action |
|---|---|
| **Tab** or **left-edge arrow** | open or close the dice palette |
| **Drag a die from the palette** | add another die to the board |
| **Hover a palette die** | name it — the two pairs that share a shape are told apart here |
| **Right-click a palette die** | choose the colour that kind of die comes out in from now on |
| **Double-tap** (touch) | the same as a right-click: opens whichever menu is under your finger |
| **Right-click a die** | open its menu: what it is, what it shows, Roll / Copy / Link / Delete, and seven colour schemes |
| **Right-click the board** | open the board menu: how many dice are out, and Throw all / Respawn / Delete all |
| **Link**, on either d10 | pick the other one; the pair then reads as one **d100**, 1–100% — and thereafter picks up, moves and throws as one |
| **R** with a die under the cursor | roll that one die where it stands |
| **C** with a die under the cursor | take a copy; the next left-click puts it down |
| **Shift** while placing a copy | keep it on the cursor and stamp another |
| **Escape** | close the menu, or drop a copy you have not placed |
| **Left-click and drag the die** | grab it — a pin joint follows the cursor |
| **Move a held die about** | spin it up: gentle movement plays `idle0`, sharp movement `idle1`. Once spinning it keeps tumbling in your hand until you let go |
| **Release the left button** | a die you spun rolls; a die you never agitated just sits where you dropped it |
| **Hold Shift, then drag a die** | select and throw every die together |
| **Four-squares button**, top right | the same thing as a permanent mode — the only way to reach it on a touchscreen |
| **Shake the device** | throw every die, where there is an accelerometer to read (see below) |
| **Space** | throw every die — they scatter across the board as well as rolling, including any still mid-roll, and the board takes a knock |
| **Hover a die on the board** | a small tag by the cursor names it and the number it is showing |
| **Total button** | open or close the die list, below the palette |
| **Hover a die-list row** | highlight its die on the board — both halves, for a d100 |
| **× in a die-list row** | remove that die; it appears when the row is pointed at |
| **Delete All** | remove every die from the board |
| **"Respawn" button** | arrange all dice near the centre and kill their velocity |
| **Speaker button**, top right, or **M** | turn the sound off and on |

Dice clack off each other and off the walls, in proportion to how hard they hit; the board
answers the space bar, and every action that changes something — a die spawned, deleted,
linked, themed — says so. All of it goes through one bus, which the speaker in the top-right
corner — or **M** — switches off. The seventeen sounds are **synthesised, not sampled**: see
[tools/audio-render/README.md](tools/audio-render/README.md).

Dice pop into existence and shrink away when deleted, both on eased curves rather than
plain fades.

The window scales: the board is drawn at a fixed 1152×648 and stretched to whatever it is
given, keeping its aspect, so the whole table is visible at any size rather than the window
revealing more empty world as it grows. Touch works too — Godot turns taps into clicks, and
a **double-tap** stands in for the right-click that a touchscreen has no way to make. The
Shift group drag has no modifier key to hold on a screen, so it is also a toggle button in
the corner. Shaking the device throws the board. Godot's web platform implements no sensor API at
all, so the browser build feeds the same gesture detector from the DOM's `devicemotion`
event instead — asked for from inside a real touch, because iOS grants motion permission
nowhere else.

A die you have not used before takes a moment to load — up to two thirds of a second for
the d20. You should never see it: the palette's icon is put down at once and plays the
arrival animation, and the real die takes over when it is ready, at the same size in the
same place.

The board and the settings **save themselves** — where every die is standing, which face it
shows, its colour, which dice are linked, the palette's colours, the panels, and whether the
sound is off. Close the game and reopen it and the table is as you left it. It writes to
`user://`, which Godot maps to IndexedDB in a web export — so the same code keeps the
browser build's board between visits, with no second code path and nothing added.

Dice come in seven colour schemes — bone, crimson, emerald, sapphire, amber, obsidian and
ivory. Nothing is re-rendered for them and there are no extra images: a small shader reads
each pixel's luminance and looks the colour up, so a themed die tumbles, blurs and lands
exactly as the original does. Colouring a die from its own menu paints that die; colouring
one in the palette sets what the *next* one of that kind comes out as, and leaves the board
alone. See [tools/theme-lab/README.md](tools/theme-lab/README.md) for how the schemes work
and what else was tried.

The die list is live rather than a snapshot: every row carries its own die and tumbles
along with it while a throw is in the air. A rolling die withholds its number — the
result is fixed the moment the throw starts, so printing it early would give the throw
away — and the total marks itself provisional until the last die has landed.

There is no score and no way to lose. It is a toy, not a game.

![the board at rest](docs/screenshot.png)

## Status

**It builds and runs.** Verified on 2026-08-18 against Godot 4.4.1 (.NET build) and the
.NET 8 SDK: `dotnet build` completes with **0 warnings, 0 errors**, and a 240-frame headless
run of `game.tscn` starts clean with no errors or warnings. The dice state machine was
checked by 20 assertions driven headlessly — see [CLAUDE.md](CLAUDE.md).

It is **a dice sandbox and nothing more**, by decision rather than by omission. The board art
is a board game's board, and a ring of eight cell coordinates in `game.tscn` survives to prove
that was once the plan — the `Player` script and scene that went with it were deleted once the
plan was dropped in August 2026. Throw dice, watch them tumble, read the numbers. See
[ROADMAP.md](ROADMAP.md) items 3 and 4.

**The number is no longer a bare random draw**, though it is not physical either. It is
taken from the frame of the tumble the die was let go on, nudged by a random factor: the idle
loop covers every face across its length, so the moment you release picks a base face and
the jitter decides how near it you land. The roll animation resumes from that same frame, so
the spin you were watching carries into the throw. A throw can be influenced but not aimed,
and the die stays fair.

What that does **not** do is read the number off where the die physically stops — that is
still the honest fix, and [ROADMAP.md](ROADMAP.md) item 1 records a full implementation that
was built, tested and then deliberately reverted as more machinery than this toy warrants.

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

### The browser demo

**[dimitriuses.github.io/godot-first-game](https://dimitriuses.github.io/godot-first-game/)** is built and published by CI from a
**hand-written GDScript port** of the gameplay, in `web/`.

That indirection is not a preference. Godot's own documentation is blunt — *"Projects
written in C# using Godot 4 currently cannot be exported to the web"* — and the editor
refuses before it starts. So the 4,465 lines of C# were ported by hand into a second tree,
and **C# stays canonical**: `web/` is a derived artifact, and when the two disagree the C#
one is right.

What keeps them from drifting is machinery rather than discipline. `scenes/` and `assets/`
are *shared*, never copied: a rewriter repoints the scripts and renames the exported
properties and signal connections, and refuses outright on anything it cannot account for.
Sixty headless checks run the ported tree before anything is exported, and a browser test
drives the published page afterwards and fails the build if it does not make a noise.

```sh
python tools/web-port/check.py              # assemble, parse, and run every harness
node tools/web-port/browser_check.mjs       # drive the live page
```

[tools/web-port/](tools/web-port/README.md) has the details, including the two bugs that
only a browser could have found: a glyph the desktop font had and the browser's did not,
and Godot quietly defaulting the web export to a different audio playback path than every
other platform.

## Layout

```
scenes/     game.tscn (board, walls, bounds area, UI), and one scene per die
scripts/    GameManager.cs   drag/release, respawn, bounds, copy placement
            Dice.cs          roll result + face animations
            DicePalette.cs   left-side drag-to-spawn dice menu
            DiceHud.cs       live die list, total and deletion controls
            DiceMenu.cs      the right-click menu, for a die, the board or the palette
            DiceTheme.cs     the colour schemes, and the materials that apply them
            Sfx.cs           the voice pool, and how often a sound may repeat
            MuteButton.cs    the speaker in the corner, icon drawn rather than typed
            SaveGame.cs      the one save file, read and written
            GroupDragButton.cs  toggles the Shift group drag, for touchscreens
            ShakeGesture.cs  decides what counts as shaking the device
            UiSkin.cs        the look and placement of the corner buttons
shaders/    dice_theme.gdshader  recolours a die without re-rendering it
assets/     dice/            die animation frames, a folder per die, plus
                             icons.png and pack.json (the palette's sheet and
                             the manifest that lists the pack):
                             d4 d6 d6n d8 d10 d10p d12 d20
                             (see docs/ASSETS.md)
            audio/           seventeen synthesised sound effects
            petixel-prototype/  tileset and figures
tools/      dice-render/     offline Blender pipeline that produces assets/dice/
            screenshots/     regenerates the two images below, deterministically
            audio-render/    synthesises assets/audio/ from nothing
            theme-lab/       compares the die-recolour shader's modes by eye
docs/       screenshot.png and roll.gif (both generated), asset provenance
```

The gameplay and runtime UI are implemented in C#.

## Known limitations

Kept in one place rather than two: **[KNOWNISSUES.md](KNOWNISSUES.md)** — every entry
reproduced by running the project, with the measurements, and with the fixed ones struck
through in place rather than deleted so the record of what was wrong survives.

The short version. Still open:

- **The result is not physical.** It comes from the tumble frame the die was released on
  plus a random factor, not from where the die actually stops ([ROADMAP.md](ROADMAP.md)
  item 1).
- **`BodyExited` fires once during scene teardown**, where its deferred recentre can never
  be flushed. Silent and harmless, still unguarded.
- **Nothing is committed as a test.** Every change is verified with a throwaway headless
  harness that is then deleted; the recipe is in [CLAUDE.md](CLAUDE.md).
- **The browser demo is a second, hand-written tree.** C# cannot be web-exported at all,
  so `web/` is a GDScript port that CI assembles, gates and publishes. It is checked
  against the C# tree rather than guaranteed identical to it — see KNOWNISSUES 9 for what
  that costs and what is still unproven.

Fixed, and worth knowing were once true: the die used to draw its number from
`System.Random`, roll itself unprompted, tunnel through the walls above 8,000 px/s, and be
dragged straight out of the board; the artwork was 18.6 MB of third-party sprites with
unknown terms. All measured, all fixed, all recorded.

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
and a value HUD, which closed ROADMAP 2. `Dice.cs` was rewritten as an explicit three-state
machine after the drag work left it freezing on a single frame — the cause was
`AnimatedSprite2D.Stop()` rewinding to frame 0 where `Pause()` was meant, and the workaround
code built on top of that had been fighting a stall it was itself causing.

Then the die-rendering pipeline was generalised off the d6 it had been written for, and
**every other die in the source pack** was added — a d4, d8, d10, d12, d20, a numbered d6 and a
percentile d10 (ROADMAP 8). Nothing in the game knows a face count, a die's name, or what a face
is worth: `Dice.cs` counts a die's faces from its own animation clips, the palette and the die
list name each one from the die itself, and a ninth would be an entry in
`tools/dice-render/dice_config.py`, two tables read off a contact sheet, and a render run.

## Licence

[MIT](LICENSE) for the code, and for the die frames under `assets/dice/`, which were
rendered for this repository from a CC0 model. **Not** for the rest of the artwork — see
[docs/ASSETS.md](docs/ASSETS.md), which still includes one asset set whose origin could not
be established.
