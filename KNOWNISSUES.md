# Known issues

Everything below was **reproduced by running the project** on Godot 4.4.1 (.NET) in July
2026, not inferred from reading the source. None of it was fixed by the cleanup pass, which
deliberately changed no behaviour.

---

## 1. The die's number is not decided by the physics

`Dice.Roll()`:

```csharp
currentResult = random.Next(1, 7);
AnimatedSprite.Play(currentResult.ToString());
```

The result is drawn from `System.Random` and the matching face animation is then played.
The tumble is a pre-rendered sprite sequence, and the body's real orientation when it comes
to rest has nothing to do with the number reported. Rolling five times in a scripted session
returned 4, 3, 3, 3, 2 — all from the RNG, none from the simulation.

This is the most interesting thing in the project and the biggest gap between what it looks
like and what it is. **Fixing it properly** means giving the die a real orientation, reading
the up-face when angular velocity drops below a threshold, and only then reporting — which
is the single change that would turn this from a toy into a dice roller.

## 2. The result never reaches the screen

`game.tscn` contains a `Label` node positioned at the bottom-right of the board. No code
references it — the only `GetNode` call in `GameManager` fetches the die. The result goes to
`GD.Print`, which is invisible in an exported build.

## 3. The die can roll itself

`Dice._PhysicsProcess`:

```csharp
else if (!isDragging && AnimatedSprite.IsPlaying()
    && (AnimatedSprite.Animation == "idle0" || AnimatedSprite.Animation == "idle1"))
{
    Roll();
}
```

Any frame in which an idle animation is playing and the die is not held starts a new roll.
The player is not required.

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

Harmless in itself — the game is closing — but it means every session ends with a spurious
"the die flew out of bounds!" in the console, which is misleading when you are trying to
reproduce the *real* tunnelling bug above. Guard it with an `IsQueuedForDeletion()` or
`GetTree() is not null` check.

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

- **`CollisionShape2D` is scaled 6×** in `dice.tscn` instead of being authored at its
  intended radius. Godot advises against scaling collision shapes.
- **`Dice.CollisionShape`** is an `[Export]` that nothing reads — it survives from a
  disable-collision-while-rolling idea that was commented out.
- **`Dice.GetResult()`** is public and called by nothing.
- **Console output is Ukrainian** while identifiers are English.
- **The export preset uses `export_filter="all_resources"`**, so every file under `assets/`
  ships in the binary whether a scene uses it or not. That was 12.8 MB of unreferenced
  Kenney artwork until the July 2026 cleanup.
- ~~**The die artwork is 18.6 MB**~~ — eight 5120×5120 spritesheets for one six-sided die,
  at four times the resolution it was drawn at. Fixed in August 2026 by re-rendering the
  animation at 128px cells drawn at 1:1: 3.9 MB for the same on-screen result, and a CC0
  source into the bargain. See [docs/ASSETS.md](docs/ASSETS.md).

## 7. Nothing is tested

There is no test project and no CI. With ~160 lines of engine glue and no pure logic beyond
`random.Next`, there is little worth pinning — but item 1 above (read the up-face from the
body's orientation) *would* be testable, and should arrive with tests if it is ever built.
