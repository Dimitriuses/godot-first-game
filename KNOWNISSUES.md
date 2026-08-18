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

## 4. The die tunnels through the walls on fast throws — ⚠️ needs investigation

**This is the real bug, and the out-of-bounds handler is the mitigation for it.**

Throw the die hard enough at a wall and it passes straight through instead of bouncing.
**Corners are the weak spot** — they fail noticeably more often than a flat wall does. When
it happens the die leaves `DiceArea`, `BodyExited` fires, and the console prints
`Кубик вилетів за межі!`. Reproduced by hand in the editor by the project's author.

This is classic **tunnelling**: a fast `RigidBody2D` moves further in one physics step than
the wall is thick, so no contact is ever generated. Things already tried:

- **Thickening the wall colliders** — helped a little, did not fix it. The walls are already
  ~149 px and ~141 px thick, which suggests thickness is not the limiting factor.
- **Teleport back to spawn on exit, zeroing the velocity** — this works, and is the right
  shape for a recovery. `OnSpawnButton` cancels `LinearVelocity` and `AngularVelocity`
  before repositioning, which is what stops the die from immediately flying out again.

**Still to try**, roughly in order of promise:

1. **Continuous collision detection.** `RigidBody2D.continuous_cd` is `CCD_MODE_DISABLED` by
   default in Godot; setting it to `CCD_MODE_CAST_RAY` (or `CAST_SHAPE`) is the intended
   engine-level answer to tunnelling and has not been tried here.
2. **Cap the speed.** Clamp `LinearVelocity` on release — the drag is a pin joint, so a
   fast flick can impart an arbitrarily large impulse with no upper bound anywhere.
3. **Raise the physics tick rate** (`physics/common/physics_ticks_per_second`) so each step
   advances the body less far.
4. **Look specifically at the corners.** Four separate `CollisionShape2D` rectangles under
   one `StaticBody2D` meet at the corners with no explicit overlap; a body arriving
   diagonally may be threading the seam between two shapes rather than passing through
   either one. Overlapping the rectangles, or replacing them with a single concave
   `WorldBoundaryShape2D` ring, would test that.

The recovery itself is worth keeping either way — it is the difference between a lost die
and a brief glitch.

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
- **`Dice.CollisionShape`** is an `[Export]` that nothing reads — it survives from a
  disable-collision-while-rolling idea that was commented out.
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
