# Known issues

Everything below was **reproduced by running the project** on Godot 4.4.1 (.NET), not
inferred from reading the source. The July 2026 cleanup pass deliberately changed no
behaviour and fixed none of it; the August 2026 work on the dice did fix several, and those
are struck through or marked *fixed* in place rather than deleted, so the record of what was
wrong survives.

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

**The `Label` this item was written about is still in `game.tscn` and still unused** — at
(1075, 582), fetched by nothing. The HUD was built instead of binding it, because one label
cannot show eight dice. It is dead scene furniture; delete it or repurpose it.

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

## 5. `BodySetState` needs the freeze dance around it

`OnSpawnButton` freezes the body, teleports it via
`PhysicsServer2D.BodySetState(..., BodyState.Transform, ...)`, then unfreezes. That looks
like superstition; it is not. Calling `BodySetState` **without** the freeze/unfreeze on a
resting body was measured to have no effect at all — the die stayed where it was. With the
freeze dance, a die displaced to (852.5, 531.1) returns to exactly (694, 432). Leave it
alone.

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
  Kenney artwork until the July 2026 cleanup.
- ~~**The die artwork is 18.6 MB**~~ — eight 5120×5120 spritesheets for one six-sided die,
  at four times the resolution it was drawn at. Fixed in August 2026 by re-rendering the
  animation at 128px cells drawn at 1:1: 3.9 MB for the same on-screen result, and a CC0
  source into the bargain. See [docs/ASSETS.md](docs/ASSETS.md).

## 7. Nothing is tested

**Still true: there is no test project, no committed test scene and no CI.** What changed in
August 2026 is that there is now a *repeatable way* to test, and the argument for not
bothering has weakened.

The old argument was that ~160 lines of engine glue with no pure logic is not worth pinning.
The gameplay code is now ~940 lines across five scripts and includes a real state machine
(`Dice`: resting / held / rolling), which is exactly the kind of thing that breaks silently
— and did, twice, before it was fixed.

Godot runs headless and the .NET build runs C# in that mode, so a throwaway `Node` scene
driven by `await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame)` can step the die
through pickup, spin-up, release and landing and assert on `AnimatedSprite2D.Animation`,
`.Frame` and `.IsPlaying()`. Twenty such checks were used to verify the August 2026 dice
rewrite and all passed; the harness was deleted afterwards rather than committed. The recipe
is written up in [CLAUDE.md](CLAUDE.md).

Making that permanent is the cheapest real improvement available to this repository, and
item 1 above (read the up-face from the body's orientation) is a pure function that would
need no harness at all.
