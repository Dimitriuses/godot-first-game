# Known issues

Everything below was **reproduced by running the project** on Godot 4.4.1 (.NET), not
inferred from reading the source. The July 2026 cleanup pass deliberately changed no
behaviour and fixed none of it; the August 2026 work on the dice did fix several, and those
are struck through or marked *fixed* in place rather than deleted, so the record of what was
wrong survives.

This is the **only** list. `README.md` used to carry an abbreviated copy of it, which is the
kind of thing that quietly goes out of date; it now points here instead.

Re-audited against the build in August 2026, before the web deploy was planned. Everything
still marked open below was re-checked at that point rather than assumed — including which
of the eight die scenes carry the fixes, since seven of them were generated after the fixes
were written.

---

## 1. The die's number is not decided by the physics — still true, but softened

It used to read:

```csharp
currentResult = random.Next(1, 7);
AnimatedSprite.Play(currentResult.ToString());
```

— the number drawn from `System.Random`, the matching animation played afterwards, and the
body's real state having nothing to do with it.

The bare draw is gone. `Dice.Roll()` now takes the face from **the frame of the idle tumble
the die was released on**, nudged by a random offset (`ResultJitter`). The idle loop covers
all six faces across its 30 frames, so the release moment picks a base face and the jitter
decides how near it the throw lands — influenceable, not aimable. The roll clip resumes from
that same frame, so the tumble carries over instead of cutting.

Measured: with the jitter off the mapping is exact (frames 0/5/10/15/20/25 give faces 1-6),
every jittered result stays within two faces of the frame's face over 600 rolls, and 12,000
rolls stay fair (1,953-2,049 per face against 2,000 expected).

**What is still wrong:** the number has no connection to where the die physically comes to
rest. A full physics implementation was built and reverted — see [ROADMAP.md](ROADMAP.md)
item 1 for what it took and why it was dropped.

## 2. The result never reaches the screen — fixed

`DiceHud`, built in code and added to a `CanvasLayer` by `GameManager._Ready`, lists every
die on the board with its current value and a running **Total**. `GameManager.OnDiceRolled`
updates it from the `DiceRolled` signal, so the number survives into an exported build.
`GD.Print("Rolled: " + result)` is still there alongside it.

~~**The `Label` this item was written about is still in `game.tscn` and still unused**~~ —
**removed, August 2026.** It sat at (1075, 582) and was fetched by nothing; the HUD had been
built instead of binding it, because one label cannot show eight dice. Deleting it changed
`docs/screenshot.png` not at all, which is the proof it was drawing nothing.

## 3. The die can roll itself — fixed

`Dice._PhysicsProcess`:

```csharp
else if (!isDragging && AnimatedSprite.IsPlaying()
    && (AnimatedSprite.Animation == "idle0" || AnimatedSprite.Animation == "idle1"))
{
    Roll();
}
```

This transition was removed, and with it the whole class of bug: `Dice` is now a three-state
machine (resting on one static frame / held and looping an idle / playing a roll clip) whose
transitions all set the sprite explicitly.

The rule that makes self-rolling impossible is that **`idle0` and `idle1` play only while the
die is held.** A free die is either playing a numbered clip or parked on a single frame, so
there is no idle state for `_PhysicsProcess` to observe and act on. Rolls now start from
exactly three places: releasing a die that was being agitated, a hard enough die-to-die
collision, and the Space key.

**One of those three had to stop sharing the guard, August 2026.** A roll in flight refuses to
be restarted, which is right for a collision — a die bumped mid-clip should finish the throw it
is in, not stutter — and wrong for the Space key, which is somebody asking for a throw. With
several dice on the board it split them into two groups that could never be brought back
together: each press threw whichever group was resting and passed over whichever was mid-clip,
so they alternated indefinitely. Reproduced in a harness before it was touched — Space skipping
the one die at frame 32, then skipping the other four at frame 72 — and `Throw()` now restarts
a roll rather than declining. `Roll()` on its own still declines, and the collision path still
calls it that way.

## 4. The die tunnels through the walls on fast throws — fixed, August 2026

**Fixed by one line:** `continuous_cd = 1` (`CCD_MODE_CAST_RAY`) on the `RigidBody2D` in
`dice.tscn`. This was the first thing the old notes suggested trying, and it turned out to be
the whole answer.

### What was measured

Dice were fired from the centre of the board at each of the four walls and the four corners,
eight directions per speed, with the out-of-bounds recovery disabled so escapes could not be
hidden. Counts are escapes out of 8:

| speed px/s | baseline | `CastRay` | `CastShape` | 120 Hz tick |
|---|---|---|---|---|
| 4,000 | 0 | 0 | 0 | 0 |
| 8,000 | 3 | **0** | 3 | 0 |
| 12,000 | 2 | **0** | 2 | 0 |
| 16,000 | 3 | **0** | 4 | 0 |
| 20,000 | 6 | **0** | 6 | 4 |
| 30,000 | 7 | **0** | 7 | 5 |

`CastRay` was then pushed further and held at **0 escapes up to 400,000 px/s** — about 33× the
speed at which the untreated body starts leaking, and far past anything a mouse can produce.

Three of those columns are worth reading carefully:

- **`CastShape` does nothing.** It is the more expensive and intuitively more thorough of the
  two CCD modes, and in this scene it performed identically to no CCD at all. If it is ever
  reached for, measure it first.
- **Raising the tick rate only moves the threshold.** Doubling to 120 Hz doubles the distance
  budget per step and buys about one speed bracket, then fails the same way. It is not a fix.
- **Normal play is unchanged.** The same firm throw travels to x=1076 — exactly the wall
  contact point, 1108.5 minus the 32 px collider — and comes to rest at (1074, 545) with or
  without CCD, to the pixel.

### Two of the old hypotheses were wrong

- **It was not the corner seams.** The old note suspected that four separate wall rectangles
  meeting with no overlap let a diagonal approach thread between two shapes. Computing the
  slabs from the committed scene shows **all four corners overlap**, so there is no seam to
  thread. Whether they always did or whether a later wall repositioning closed them, the
  hypothesis does not hold now.
- **Geometry under-predicted the onset.** A 32 px circle against a 149 px slab has a 213 px
  detection band, which at 60 Hz predicts leaking above ~12,780 px/s. Escapes actually start
  at **8,000 px/s**, so the practical threshold is lower and more reachable than the arithmetic
  suggests. Worth remembering before trusting a similar calculation elsewhere.

### It applies to all eight dice, not just the d6 — measured, August 2026

The fix landed on `dice.tscn` when that was the only die. The other seven scenes were
generated afterwards by `tools/dice-render/make_scene.py`, so whether they inherited it is a
real question, and grepping for the property name is not an answer.

All eight carry `continuous_cd = 1` and `center_of_mass_mode = 1`. (Grepping for
`center_of_mass = Vector2(0, 0)` finds **nothing**, which looks alarming and is not: Godot
omits a property sitting at its default when it saves a scene, and the default is the
`(0, 0)` that was wanted.)

Then the behaviour, rather than the file: every die fired at the board from the centre, eight
directions each, at two speeds, measuring how far outside the wall face it ever reached.

| speed | shots | escapes | worst excursion |
|---|---|---|---|
| 20,000 px/s | 64 | **0** | 0.0 px |
| 60,000 px/s | 64 | **0** | 30.2 px |

30 px of momentary penetration at 60,000 px/s is the collider being pushed back out within
the step, against a 149 px wall — nowhere near leaving. The template is what carries this, so
a ninth die would get it too, but re-measure rather than trust if the generator is rewritten.

### What this does not change

The teleport-and-zero-velocity recovery stays. With CCD on it never fires — measured at 0
across 8,000 to 60,000 px/s with the handler enabled — but it costs nothing idle and it is the
difference between a lost die and a brief glitch if anything ever does get out.

Release velocity is still unclamped. `dragVelocity` is `(mouse - lastMousePosition) / delta`
with no upper bound, so a fast flick can still impart an arbitrarily large impulse; CCD simply
means the die now bounces off the wall instead of leaving through it. Clamping it is a
question about how a throw should *feel*, not a correctness fix any more.

### 4b. The drag itself could push dice out — fixed, August 2026

Two ways, both reported from play and both since measured:

- **The pin joint chased the cursor off the board.** `MousePin.GlobalPosition` was set to the
  raw mouse position, so moving the cursor past a wall hauled the die into it and the solver
  fought back hard enough to squeeze it out — worst in the bottom-right corner, where two
  walls push at once. Measured with the cursor held 1,300px past the corner: the die reached
  **421px past the wall face.**
- **Shift-dragging was noclip.** The group drag froze each body and assigned `GlobalPosition`
  directly. A frozen body moved that way ignores walls and other dice completely, so the whole
  selection walked straight out of the board.

The fixes:

- `GameManager` now works out the playable rectangle **from the wall colliders themselves**
  (`ComputeBoardBounds`, exposed as `BoardBounds`) rather than from hardcoded numbers, so
  nudging a wall in the editor moves the drag limit with it. It reads back as
  x 40.5–1108.5, y 64.5–626.5.
- The pin is clamped into that rectangle, inset by the die's collider radius and corrected for
  the collider's `(1, 12)` offset. The cursor may leave the board; the pin may not. Measured
  again: **0.00px past the wall face**, resting against it.
- The group drag no longer freezes anything. Each die is **steered by velocity**, so walls and
  the other dice actually stop it instead of being passed through.
- It also **stops preserving the arrangement the dice were picked up in.** Holding the original
  offsets meant that with the cursor past a wall every die was fighting to reach a slot inside
  that wall, and the pile visibly strained against it. Dice are now simply gathered to the
  cursor, into a clump whose radius grows with the square root of the count, and once a die is
  inside the clump it stops being pushed at all and settles against its neighbours. Measured
  against the formation-holding version, crushing three dice into a corner improved on every
  count: worst overshoot 24.8px → **14.4px**, resting gaps 37/53/44px → **62/44/59px** (free
  dice sit 59px apart), and peak speed once settled **0px/s** instead of continuous shoving.

This is also what finally made `Dice.CollisionShape` earn its keep; it was listed under
*Smaller things* as an `[Export]` nothing read. `CollisionRadius` and `CollisionOffset` are
read off it now.

**One residual, deliberately accepted.** Steering by setting `LinearVelocity` overrides the
contact solver's response, so a die crushed between another die and a wall can be extruded
past the face. A containment step puts it back each frame, holding the worst case to
**14.4px under a sustained three-die squeeze into a corner with the cursor held far
off-board** — bounded, cosmetic, and nowhere near the 181px needed to leave through a
149px wall. Lowering `MaxDragSpeed` does not reliably help: sweeping it gave 8.7 / 14.4 / 8.7 /
2.4px at 4000 / 2000 / 1000 / 500px per second, so it was left at 4000 for responsiveness.
Steering with forces instead was tried and is worse — 461px of lag behind a 900px/s cursor,
and unstable at high gain.

### 4a. A separate, smaller bug in the same handler

Independently of the above: `BodyExited` **also** fires once during **scene teardown**, and
there the recovery cannot run. Measured over 1,800- and 6,000-frame idle sessions:

| Observation | Result |
|---|---|
| Frame the teardown event lands on | the **final** frame, immediately before `_ExitTree` |
| `HasMethod("OnSpawnButton")` at that moment | `True` |
| `OnSpawnButton` actually invoked | **no** |
| A `SceneTreeTimer` created in the same handler | **never fired** |

Freeing the tree removes the die from the area, which emits `body_exited` while the tree is
shutting down, so the message queue never flushes the deferred call. The same
`CallDeferred(nameof(OnSpawnButton))` works perfectly from `_Ready`, which rules out a
method-binding problem.

Harmless in itself — the game is closing — and **as of August 2026 it is also silent**: the
handler no longer prints anything, so a session that ends this way produces no console
output at all (confirmed by a 240-frame headless run, which emitted nothing). The teardown
event itself still fires and its deferred recentre still never flushes; only the misleading
message is gone. A guard on `IsQueuedForDeletion()` would make the intent explicit.

*Note: an earlier draft of this file claimed the recovery had "never executed" at all. That
was wrong. It came from measuring only idle sessions with no input — the die never moves on
its own, so it never left the area. The bug needs someone to actually throw the die.*

## 5. ~~`BodySetState` needs the freeze dance around it~~ — re-measured and replaced

This used to say the freeze/unfreeze around `PhysicsServer2D.BodySetState` looked like
superstition and was not, because bare `BodySetState` on a resting body had been measured to
do nothing at all.

**That no longer reproduces.** Re-measured in August 2026 on dice that had genuinely fallen
asleep (asserted, not assumed), moving each from (500, 300) to (800, 450) and reading back
the error:

| how | awake | asleep |
|---|---|---|
| freeze / `BodySetState` / unfreeze | 0.0 px | 0.0 px |
| **`GlobalPosition = ...`** | **341.8 px** | **341.8 px** |
| `BodySetState`, no freeze | 0.0 px | 0.0 px |
| `_IntegrateForces` + `state.Transform` | 0.0 px | 0.0 px |

Three of the four work; only assigning `GlobalPosition` fails, and it fails completely —
the die does not move at all, awake or asleep. Whatever made the bare call fail when the
original note was written is gone, and the dance was load-bearing for nobody.

`ResetDie` now calls **`Dice.TeleportTo`**, which applies the move from inside
`_IntegrateForces` and clears both velocities with it. That is the engine's own documented
answer to moving a rigid body, it needs no freeze, and it keeps the knowledge of how a die
moves inside `Dice` rather than in the board reaching past the node into the physics server.

**What has not changed: do not assign `GlobalPosition` to move a die.** That is still the
one approach that silently does nothing.

## 6. Smaller things

- ~~**`CollisionShape2D` was scaled 6×.**~~ Fixed with an unscaled 32 px-radius shape,
  offset to `(1, 12)` so it sits under the drawn die — which is 13.5 px below the centre of
  its own 128 px sprite cell. That offset then became the body's **centre of mass**, because
  `center_of_mass_mode` defaults to Auto and Auto derives it from the collision shapes. A
  spinning die therefore orbited a point 12 px below its own origin, measured at 24 px of
  wander with no linear velocity at all. Pinning `center_of_mass` to `(0, 0)` fixed it; the
  same measurement now reads 0.
- ~~**`Dice.CollisionShape`** is an `[Export]` that nothing reads.~~ It does now: `Dice`
  exposes `CollisionRadius` and `CollisionOffset` off it, and `GameManager` uses both to work
  out how far a dragged die may travel before it touches a wall (issue 4b).
- **`Dice.GetResult()`** supplies each newly registered die's initial HUD value.
- ~~**Console output is Ukrainian**~~ while identifiers are English. No longer true: there is
  no non-ASCII text left in `scripts/`, and the single remaining `GD.Print` is
  `"Rolled: " + result`. The Ukrainian out-of-bounds message quoted in issue 4 above is
  historical.
- **The export preset uses `export_filter="all_resources"`**, so every file under `assets/`
  ships in the binary whether a scene uses it or not. That was 12.8 MB of unreferenced
  Kenney artwork until the July 2026 cleanup. It matters again now for a different reason:
  `assets/dice/` is **47 MB**, and all of it would go into a browser download. See item 8.
- ~~**`scenes/Player.tscn`, `scripts/Player.cs` and the two Kenney sprites are dead
  weight.**~~ **Deleted, August 2026**, along with `assets/kenney-boardgame/` entirely. The
  board game they belonged to was dropped (ROADMAP 3); nothing referenced `Player.tscn`, and
  `Player.tscn` was the only thing referencing `piece-black-border04.png`. `docs/screenshot.png`
  is byte-identical afterwards. `assets/petixel-prototype/` stays — `game.tscn` draws the
  board and the figures from it.
- **Nothing moves until you touch it.** Gravity is disabled — it is a top-down board — and
  nothing animates on its own, so the opening scene is completely static. Measured in July
  2026 as 1,800 consecutive byte-identical rendered frames; the mechanism has not changed
  since, though the frame count has not been re-measured.
- ~~**The die artwork is 18.6 MB**~~ — eight 5120×5120 spritesheets for one six-sided die,
  at four times the resolution it was drawn at. Fixed in August 2026 by re-rendering the
  animation at 128px cells drawn at 1:1: 3.9 MB for the same on-screen result, and a CC0
  source into the bargain. See [docs/ASSETS.md](docs/ASSETS.md).

## 7. Nothing is tested

**Still true: there is no test project, no committed test scene and no CI.** What changed in
August 2026 is that there is now a *repeatable way* to test, and the argument for not
bothering has weakened.

The old argument was that ~160 lines of engine glue with no pure logic is not worth pinning.
The gameplay code is now **~4,160 lines across twelve scripts** and includes a real state
machine (`Dice`: resting / held / rolling), a save format, an audio mixer and a shader — the
kind of thing that breaks silently, and has, repeatedly.

The throwaway harnesses have earned their keep since: they are what caught the die list
claiming deleted dice, the delete cross vanishing under the cursor, `body_entered` reporting
post-impact velocity so every collision was inaudible, `SpawnDie` ignoring the theme it was
handed, the copy's arrival animation being killed by `PlaceOnFace`, and dice spawning twelve
pixels below the cursor. Every one of those was found by a harness that was then deleted.

Godot runs headless and the .NET build runs C# in that mode, so a throwaway `Node` scene
driven by `await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame)` can step the die
through pickup, spin-up, release and landing and assert on `AnimatedSprite2D.Animation`,
`.Frame` and `.IsPlaying()`. Twenty such checks verified the August 2026 dice rewrite; later
features ran 8 to 34 apiece. Every harness was deleted afterwards rather than committed. The
recipe, and the traps that cost a run each, are written up in [CLAUDE.md](CLAUDE.md).

Making that permanent is the cheapest real improvement available to this repository. Note
that it is also a prerequisite for the web port (ROADMAP 9d): the C# tree and the GDScript
tree will drift the moment either changes, and the harness is the only thing that could tell
you they had.

---

## 8. Two found by playing it, August 2026 — both fixed

Neither showed up in any harness, because both are about *where* the UI is rather than what
it does, and a harness that spawns a die by calling `SpawnDie` never asks.

### 8a. A fifth of the board refused dropped dice

`DicePalette` decided whether a drop counted by comparing the release against the drawer's
**width**: `mouse.X > DrawerLeft + DrawerWidth`. That is a vertical strip 255px wide running
the full height of the window — about **20% of the board** — and it refused every drop inside
it whether or not a panel was actually there, and whether or not the die list underneath was
even open. Dropping a die into the empty space below a closed die list silently did nothing.

It was wrong in the other direction too, which nobody had noticed: x=1124 passes the test, so
a die could be dropped **onto the mute and group-drag buttons** in the opposite corner.

Now it asks `GetViewport().GuiGetHoveredControl()` — the same question `GameManager._Input`
already asks to tell a click on the board from a click on a panel. That covers the drawer,
the die list and the corner buttons at once, only where they really are, and follows them if
they ever move. Verified mid-drag: the pressed slot button does not hold the hover over the
board, so the answer is null exactly where a drop should land.

### 8b. Respawn built a column off the bottom of the board

`OnSpawnButton` laid the dice out four to a row, 54px apart, from the spawn point:

```csharp
ResetDie(dice[i], spawnPosition + new Vector2((i % 4) * 54f, (i / 4) * 54f));
```

Four wide however many there are, so the block only ever grew downward. Measured with 40
dice: **12 of them ended outside the board, the worst 105.7px past the edge.** The spawn
point sits 283px above the floor and ten rows is 540px.

The block is now sized to the board — roughly square, spaced no wider than fits, centred on
the spawn point unless that would hang it over an edge, and each die clamped individually by
`ResetDie` afterwards. Same measurement: **0 outside, 0.0px past the edge.** No cap on the
number of dice was needed; the arithmetic was the bug, not the count.

---

## 9. Before a web deploy — what is not ready

The browser build is [ROADMAP](ROADMAP.md) item 9. None of the following is a bug in the
game; they are the things that stand between it and a Pages URL, listed here so that
"is it ready?" has one answer.

- **C# cannot be exported to the web at all.** Re-checked in August 2026 by adding a Web
  preset and running the export: Godot refuses before it starts, with *"Export to Web is
  currently not supported in Godot 4 when using C#/.NET."* This is the blocker, and the
  reason item 9 is a hand-written GDScript port rather than a build setting.
- **47 MB of dice artwork would go into the download — and it used to be 727 MB in
  memory.** The memory half is **fixed**: the pack is now a manifest of paths
  (`assets/dice/pack.json`) and a die's sheets load only when one is spawned, so startup
  went from 727 MB of texture memory to 58 MB. See CLAUDE.md and
  [tools/clip-lab/](tools/clip-lab/README.md). The download half is still open.
  `export_filter="all_resources"` ships everything under `assets/`, and `assets/dice/`
  alone is 47 MB across eight dice.
  That is fine for a desktop binary and not fine for a browser. The options are now
  **measured** rather than estimated — see [tools/clip-lab/](tools/clip-lab/README.md).
  The 47 MB is the size of the *sources*, which do not ship: Godot's import re-encodes
  them, and what a build actually carried was **32.62 MB**.

  **Two of the measured options are now applied.** The lossy WebP import, which needs no
  re-render at all: 32.62 MB → **16.29 MB**, for no memory cost (the GPU format is `Rgba8`
  either way). And the roll clips thinned from 91 frames to 60, which takes it to
  **11.07 MB — a 66% cut overall** — and unlike the import change also buys memory back:
  startup 79 → 58 MB, a resident d20 180 → 110 MB, all eight kinds 727 → 462 MB. A roll
  now lasts 2.00 s rather than 3.03 s, which is a gameplay change as much as a size one. The 84 sheets outside `assets/dice/d6/` are `compress/mode=1` at quality 0.85;
  the pipped d6 stays lossless, because WebP subsamples chroma whatever the quality and its
  red pip is the one small saturated feature in the pack. Measured on the README
  screenshot: the d6 is pixel-identical to the lossless build and no other die differs by
  more than 48 of 765.

  Still open: the shared-prefix redesign, worth a further 27% but needing a real re-render
  in `tools/dice-render/` — the eight dice were rendered as independent per-face
  trajectories, so no prefix can be shared without one. **VRAM compression was measured and rejected** — it cuts memory
  fourfold but multiplies the download by four and a half, and memory stopped being the
  constraint when the pack went lazy.
- **The save has never been run in a browser.** `user://` maps to IndexedDB in a web export,
  which is why the save was written with plain `FileAccess` and no JavaScript, but that path
  has only been exercised on a desktop. The flush happens when the file is closed.
- **Shake-to-throw will not work in a browser as written.** Godot's web platform does not
  implement the sensor APIs, so `Input.GetAccelerometer()` returns zero there.
  `ShakeGesture` is fed samples rather than polling precisely so the port can hand it values
  from the DOM's `devicemotion` event — see ROADMAP 9b-touch, including the iOS permission
  gesture that has to go with it.
- **Touch has not been tried on a device.** The double-tap menu and the group-drag toggle
  are covered by harnesses driving real `InputEventScreenTouch` events, and the scene scales
  to any window, but nobody has held a phone and used it. Feel — tap targets, whether a
  double-tap conflicts with a quick double-throw — cannot be settled headless.
