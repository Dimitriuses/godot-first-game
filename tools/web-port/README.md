# Web port

The GDScript tree that becomes the browser build, and the tooling that assembles it.
ROADMAP 9 has the reasoning; this is how to run it.

**C# is canonical.** `web/` is a derived artifact. When the two disagree the C# tree is
right and this one is stale — which is the whole risk of the arrangement, and why 9d
exists.

```sh
export GODOT_STANDARD=".../Godot_v4.4.1-stable_win64_console.exe"   # NOT the .mono one
python tools/web-port/assemble.py
"$GODOT_STANDARD" --headless --path build/web-project --import
"$GODOT_STANDARD" --headless --path build/web-project res://tests/dice_check.tscn
```

The engine here is the **standard** build, not the .NET one. The assembled project
contains no C# at all, which is the entire point.

## What is where

```
web/
  project.godot        no [dotnet], no "C#" in features, main_scene by path
  scripts/*.gd         the hand port
  scenes/smoke.tscn    proves the tree runs; depends on nothing
  tests/*.gd           the parity harnesses (9d)
tools/web-port/
  assemble.py          builds build/web-project from both trees
  rewrite_scenes.py    scenes/*.tscn -> their GDScript equivalents
```

`build/` is gitignored. **A second copy of `assets/` is never committed** — it is 32 MB
of spritesheets, and the assembler copies it in at build time instead.

## The scene rewriter

`scenes/*.tscn` are shared, not duplicated: 5,040 of their lines are `AtlasTexture`
regions, which are language-agnostic, and nobody maintains a second copy of that by hand.
The rewriter changes exactly three things and leaves everything else byte-identical.

1. Script `ExtResource`s repointed from `res://scripts/Dice.cs` to
   `res://scripts/dice.gd`, and the `uid=` dropped — Godot mints one for the `.gd` on
   import, and carrying the C# one over points at nothing.
2. Exported property keys renamed, `AnimatedSprite` → `animated_sprite`.
3. Signal connections repointed, `method="OnSpawnButton"` → `method="on_spawn_button"`.
4. Nothing else.

**The rename is the dangerous part, and it has two halves.** A scene that names a
property the script does not have loads *successfully*, with that export left null, and
reports nothing at all — a null `animated_sprite` is a die that never draws. The obvious
half is the assignment:

```
animated_sprite = NodePath("AnimatedSprite2D")
```

The half that is easy to miss, and that was missed here first time round, is the
declaration on the node itself:

```
[node name="Dice" type="RigidBody2D" node_paths=PackedStringArray("AnimatedSprite", ...)]
```

Rename the assignments and leave this and the scene still loads, still says nothing, and
binds neither path. It was caught by reading the rewriter's own output rather than by any
check, which is the argument for looking at generated files at least once.

**A scene names things in the script in three places, and each was found the hard way.**
The third is the connection:

```
[connection signal="button_down" from="Button" to="." method="OnSpawnButton"]
```

That one line is why the Respawn button did nothing in the first deploy: the scene loaded,
the button drew, and pressing it called a method GDScript does not have. It is the
quietest of the three — no null anything, just a control that ignores you.

So the mapping is **derived from the C# sources** — every `[Export]` and every method is
parsed out of `scripts/*.cs` — rather than written down, and a key or a method the tool
cannot account for is a hard error rather than a passthrough:

```
dice.tscn: exported key(s) SpinSpeed are not [Export]s of Dice.
game.tscn: connection(s) call OnRespawnPressed, which GameManager does not declare.
```

Verify that guard still fires before trusting it; it is the only thing standing between
a renamed C# export and a silently broken web build.

## Progress — the port is complete

All twelve scripts are ported: **4,465 C# lines**, and `game.tscn` loads and runs in the
GDScript tree. 29 checks pass across two harnesses.

| | C# lines | |
|---|---|---|
| `game_manager.gd` | 1,531 | the board — drag, menus, spawn, link, save, shudder |
| `dice_hud.gd` | 700 | the die list |
| `dice.gd` | 680 | the die and its state machine |
| `dice_palette.gd` | 453 | the drawer |
| `dice_menu.gd` | 398 | the three right-click menus |
| `sfx.gd` | 209 | |
| `mute_button.gd` `dice_theme.gd` `save_game.gd` | 274 | |
| `group_drag_button.gd` `shake_gesture.gd` `ui_skin.gd` | 220 | |

Re-counted August 2026 at 4,465 across twelve scripts, against the 4,160 the ROADMAP
recorded — it grew again between the estimate and the start, exactly as that entry warned.

Three things GDScript has not got, and how each was handled throughout:

- **No method overloads.** C#'s three `SpawnDie` became `spawn_die` (by path — the one
  everything outside the class should use, and the only one that keeps the pack lazy),
  `spawn_die_at_slot` and `spawn_die_scene`.
- **No tuples and no `HashSet`.** The pack is an Array of Dictionaries; `_deleting_dice`
  is a Dictionary used as a set.
- **No `load` as a method name.** `SaveGame.Load` became `load_board`, because `load` is
  a GDScript built-in.

Two traps worth knowing for anything ported later:

- **`Control` already has `visible`.** `DiceHud`'s list of visible entries had to become
  `_visible_entries`; shadowing it hides the panel and nothing says so.
- **A lambda captures by value at connect time.** `DiceMenu`'s item handlers close over
  the die the menu is *currently* open on, so they are named methods rather than lambdas —
  a lambda would have frozen the die the menu was built with.

`SaveGame` was the easiest file by a distance, and not by accident: the C# version was
written against Godot's own `Json` and `Godot.Collections.Dictionary` rather than
`System.Text.Json` *specifically* so this half would survive the crossing. It did.

`shaders/dice_theme.gdshader` is not in the total and is copied verbatim: `.gdshader` is
language-agnostic and was the one part of this that cost nothing.

## Checking it locally

```sh
export GODOT_STANDARD=".../Godot_v4.4.1-stable_win64_console.exe"
python tools/web-port/check.py
```

Assembles, imports, and runs every harness in `web/tests/`. **About twelve seconds**, and
it is the same command CI runs, so a red build there reproduces here with nothing to set
up. Exit code is 0 only if everything passed, so it works as a gate rather than a report.

It **refuses the .NET engine**. That build would run this project perfectly well, which is
exactly the problem: the point of the GDScript tree is that it does not need .NET, and a
check that passes only under the .NET build has not checked that.

`check.py --serve` serves an exported build out of `build/web/` for a look in a real
browser. It cannot produce one — see below — so that is for an artifact downloaded from
CI. Plain `http.server` is enough because the build is single-threaded and so needs no
COOP/COEP headers, which is the same property that lets it work on Pages at all.

## The export, and why it is CI's

`.github/workflows/web.yml` installs the **standard** engine and its web export
templates, runs `check.py` as a gate, exports, and deploys to Pages. The templates are the
reason it lives there: they are a ~1 GB download that CI caches and a workstation
generally lacks.

The preset is `web/export_presets.cfg`, and the one setting in it that matters is
`variant/thread_support=false`. A threaded export needs `SharedArrayBuffer`, which
browsers only grant to pages served with `Cross-Origin-Opener-Policy` and
`Cross-Origin-Embedder-Policy`. **GitHub Pages cannot set custom headers**, so a threaded
build would load and then fail at runtime. Single-threaded has been the default since
Godot 4.3; it is written down because it is the setting that breaks the deployment
quietly rather than loudly.

Verified locally as far as it can be without templates: the export gets past preset
validation and stops asking for `web_nothreads_release.zip` — the *nothreads* name being
the confirmation that the setting took.

## Parity

Two harnesses in `web/tests/`, both run by `check.py`:

- **`dice_check.gd`** drives the rewritten `dice.tscn`. Its numbers match the C# harness
  exactly — a release at idle frames 0/7/14/21/29 starts the roll at frames 0/4/7/11/15 in
  both trees. That mapping is `TumbleFrames`, the one piece of game logic the clip
  decimation forced a change to, so it is the check most likely to catch a bad port.
- **`board_check.gd`** builds `game.tscn` itself: the pack manifest, the board bounds off
  the wall colliders, all five panels, a spawn landing where it was asked for, the die
  list's per-type numbering, throw-all, the shudder moving the *view* and resetting, a
  delete, and the save serialising with rounded positions.

The second exists because the port's likely failures are silent. A signal connected to a
renamed method, an export the rewriter missed, a panel that builds but wires nothing: none
of them stop the scene loading, and several leave a board that looks right until touched.

Still to build, per 9d: the screenshot diff between the two trees.

## Not yet proved: the export itself

Everything above *runs*; nothing has been *exported*, because no standard-engine web
templates are installed here. ROADMAP 9's suggested order puts proving the deployment
first, deliberately, because that is where the unknowns are — and that step now belongs to
the first CI run rather than to this machine.
