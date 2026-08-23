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

| | lines | state |
|---|---|---|
| `dice.gd` | 680 → 430 | ported, 14 checks passing |
| `dice_theme.gd` | 92 → 100 | ported |
| `sfx.gd` | 209 → 180 | ported, untested |
| `game_manager.gd` | 1,531 | not started — most of the risk |
| `dice_hud.gd` | 700 | not started |
| `dice_palette.gd` | 453 | not started |
| `dice_menu.gd` | 398 | not started |
| the other five | 393 | not started |

Re-counted August 2026: **4,465 lines across twelve scripts**, against the 4,160 the
ROADMAP recorded. It grew again between the estimate and the start, exactly as that
entry warned it would.

`shaders/dice_theme.gdshader` is not in the total and is copied verbatim: `.gdshader` is
language-agnostic and was the one part of this that cost nothing.

## Parity

`web/tests/dice_check.gd` drives the rewritten `dice.tscn` and asserts the same things
the C# harness does. The numbers match exactly — a release at idle frames 0/7/14/21/29
starts the roll at frames 0/4/7/11/15 in both trees — which is the check that matters,
because that mapping is `TumbleFrames`, the one piece of game logic the clip decimation
forced a change to.

Still to build, per 9d: the screenshot diff, and CI running both gates as **failures**
rather than warnings.

## Not yet proved: the export

Everything above *runs*; nothing has been *exported*. That needs the standard engine's
web export templates, and only the `.mono` ones are installed on this machine —
`%APPDATA%/Godot/export_templates/` has `4.4.1.stable.mono` and no `4.4.1.stable`. The
templates are a ~1 GB download, or CI fetches them itself.

ROADMAP 9's suggested order puts proving the deployment *first*, deliberately, because
that is where the unknowns are. That step is still outstanding, and the port being
testable without it is luck rather than design.
