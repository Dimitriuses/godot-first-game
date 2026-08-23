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
3. Nothing else.

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

So the mapping is **derived from the C# sources** — every `[Export]` is parsed out of
`scripts/*.cs` — rather than written down, and an exported key the tool cannot account
for is a hard error rather than a passthrough:

```
dice.tscn: exported key(s) SpinSpeed are not [Export]s of Dice.
```

Verify that guard still fires before trusting it; it is the only thing standing between
a renamed C# export and a silently broken web build.

## Progress

| | C# lines | state |
|---|---|---|
| `dice.gd` | 680 | ported, 14 checks passing |
| `sfx.gd` | 209 | ported |
| `mute_button.gd` | 96 | ported |
| `dice_theme.gd` | 92 | ported |
| `save_game.gd` | 86 | ported — a transliteration; see below |
| `group_drag_button.gd` | 83 | ported |
| `shake_gesture.gd` | 70 | ported |
| `ui_skin.gd` | 67 | ported |
| `dice_menu.gd` | 398 | ported |
| `dice_palette.gd` | 453 | **not started** |
| `dice_hud.gd` | 700 | **not started** |
| `game_manager.gd` | 1,531 | **not started** — most of the risk |

**1,781 of 4,465 lines ported.** Re-counted August 2026: 4,465 across twelve scripts,
against the 4,160 the ROADMAP recorded. It grew again between the estimate and the start,
exactly as that entry warned.

`game_manager.gd` cannot be written before the three UI scripts it builds and wires: it
references `DicePalette`, `DiceHud` and `DiceMenu` by type, so the file would not parse,
and an unparsed 900-line port is not a port. That is the order — palette, list, board.

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

`web/tests/dice_check.gd` drives the rewritten `dice.tscn` and asserts the same things
the C# harness does. The numbers match exactly — a release at idle frames 0/7/14/21/29
starts the roll at frames 0/4/7/11/15 in both trees — which is the check that matters,
because that mapping is `TumbleFrames`, the one piece of game logic the clip decimation
forced a change to.

Still to build, per 9d: the screenshot diff, and CI running both gates as **failures**
rather than warnings.

## Not yet proved: the export itself

Everything above *runs*; nothing has been *exported*, because no standard-engine web
templates are installed here. ROADMAP 9's suggested order puts proving the deployment
first, deliberately, because that is where the unknowns are — and that step now belongs to
the first CI run rather than to this machine.
