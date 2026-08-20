# Screenshot pipeline

Regenerates the two images `README.md` embeds:

| | |
|---|---|
| `docs/screenshot.png` | 1152×648 — the board at rest with four dice, the die list and the palette both open |
| `docs/roll.gif` | 640×360, 46 frames — a die playing a full roll animation |

They were previously hand-captured, so they drifted out of date every time the
scene changed and could not be reproduced. These are generated, and **two runs
produce byte-identical files** (verified by hash).

## Running it

```sh
python tools/screenshots/capture.py
```

That builds the C# assembly, runs the capture scene, writes both images into
`docs/`, and deletes its intermediates. Useful flags:

```sh
--godot <path>   point at a Godot .NET build if it is not auto-detected
--keep           leave tools/screenshots/build/ in place to inspect the frames
--no-run         re-assemble the GIF from frames already captured, without
                 re-running Godot — what you want when tuning CROP or STRIDE
```

**A real window opens for a few seconds.** `--headless` has no renderer, so it
cannot be used here.

## How it stays deterministic

Nothing is allowed to free-run:

- **Dice are placed at literal coordinates** and frozen, not dropped and left to
  settle. Gravity is off in this project anyway, so no physics runs during capture.
- **Faces are placed, not rolled.** `Dice.PlaceOnFace(n)` parks the die on face
  `n` and reports it — a put-down rather than a roll, with no clip to wait out and
  nothing random. The capture places 5, 2, 6 and 4, which is why the HUD always
  reads `Total: 17`, and `DiceRolled` still fires so the HUD populates itself the
  way it does in play.
- **The dice are parked before anything is moved**, with `_PhysicsProcess` and
  `ContactMonitor` both off. This tool teleports dice, `freeze_mode` is Kinematic
  so that reads as a hard contact, and the collision re-roll then starts clips
  playing behind the shot. Parking them afterwards is too late — the roll has
  already begun, and `AnimatedSprite2D` keeps advancing in `_process` even with
  `_PhysicsProcess` off.
- **`ContactMonitor`, not `CollisionLayer`.** Zeroing the layer also hides the dice
  from the bounds `Area2D`, whose `BodyExited` then fires for every one of them and
  the out-of-bounds recovery teleports the whole board back to the spawn point.
- **The roll frames are stepped by hand.** Playback is stopped and
  `AnimatedSprite2D.Frame` is set to each index in turn, so one output PNG maps to
  exactly one source frame regardless of how fast the machine renders. Nothing
  depends on wall-clock time.
- **The GIF uses one shared palette** for every frame. Left to itself PIL picks a
  new adaptive palette per frame, which both dithers inconsistently and destroys
  inter-frame compression.

Every step is now either an explicit assignment or a fixed frame count. The
earlier version had to wait out four 3.1-second landing animations before taking
the still, which was only deterministic in its *end* state; `PlaceOnFace` put the
dice down instantly and removed that wait entirely.

## Tuning

Composition constants live at the top of the two files:

- `Capture.cs` — `Board` (position and face per die), `RollingDiePosition`,
  `RollAnimation`, and the capture size.
- `capture.py` — `CROP`, `STRIDE`, `FRAME_MS`, `END_HOLD_MS`.

`CROP` is in capture-space pixels. Its top edge threads a narrow gap: the Respawn
button ends at y=78 and the banner starts at y≈84, so a top much below 80 clips
the button into shot and much above it clips the banner.

After changing anything in `capture.py` alone, use `--no-run --keep` to iterate
without re-running Godot each time.

## Note on the API this uses

`Capture.cs` calls four things that exist as `public` partly for its benefit:
`GameManager.SpawnDie`, `DiceHud.SetOpen`, `DicePalette.SetDrawerOpen` and
`Dice.PlaceOnFace`. All four are reasonable public operations in their own right —
`PlaceOnFace` in particular is what anything restoring a saved board would want.
