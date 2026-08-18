using Godot;
using System;

/// <summary>
/// A die, and the small state machine that drives its sprite.
///
/// There are exactly three visual states, and every transition sets the sprite
/// explicitly, so nothing ever has to poll for or repair a stalled animation:
///
///   Resting  one static frame — the last frame of <see cref="currentResult"/>'s
///            clip, which is the die sitting still showing that face.
///   Held     idle0 / idle1 loop while the player agitates it. These loops play
///            *only* while the die is held, so a free die is never left looping.
///   Rolling  one of the "1".."6" clips. It ends on its own last frame, which is
///            the resting pose, and emits <see cref="DiceRolledEventHandler"/>.
/// </summary>
public partial class Dice : RigidBody2D
{
	private static readonly StringName Idle0 = "idle0";
	private static readonly StringName Idle1 = "idle1";

	[Export] public AnimatedSprite2D AnimatedSprite;
	[Export] public CollisionShape2D CollisionShape;

	/// Relative impact speed at which one die knocks another into a roll.
	[Export] public float CollisionRollSpeed = 140f;
	/// Speed a release must exceed to roll even if the die never span up.
	[Export] public float ReleaseRollSpeed = 180f;

	/// How hard a held die must be moved to spin it up to idle0, then to idle1.
	[Export] public float SpinOnSpeed = 120f;
	[Export] public float FastSpinOnSpeed = 600f;
	/// The same, for a die swung hard enough to rotate about the mouse pin.
	[Export] public float SpinOnAngular = 1.5f;
	[Export] public float FastSpinOnAngular = 8f;

	private int currentResult = 1;
	private bool isHeld;
	private bool isRolling;
	private int spinLevel;              // 0 resting, 1 idle0, 2 idle1
	private float moveSpeed;
	private Vector2 lastPosition;
	private ulong lastCollisionRollMs;

	public bool IsHeld => isHeld;
	public bool IsRolling => isRolling;

	[Signal]
	public delegate void DiceRolledEventHandler(int result);

	public override void _Ready()
	{
		AnimatedSprite.AnimationFinished += OnAnimationFinished;
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyEntered += OnBodyEntered;
		lastPosition = GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Keep the face upright while the body itself spins.
		AnimatedSprite.Rotation = -Rotation;

		// Measured off the node rather than LinearVelocity, because the two drag
		// styles move the die differently: the single drag pulls it with a pin and
		// physics carries it, while the Shift group drag freezes the body and moves
		// it by hand — and a frozen body reports no velocity at all.
		Vector2 position = GlobalPosition;
		moveSpeed = delta > 0 ? (position - lastPosition).Length() / (float)delta : 0f;
		lastPosition = position;

		if (isHeld)
			UpdateHeldSpin();
	}

	// ------------------------------------------------------------------ dragging

	public void StartDragging()
	{
		// Grabbing a die mid-roll settles it now, so the value the HUD reports
		// always matches the face left on screen.
		if (isRolling)
			FinishRoll();

		isHeld = true;
		spinLevel = 0;
		lastPosition = GlobalPosition;
		ShowResting();
	}

	/// <param name="releaseSpeed">Speed the die is being let go at, in px/s.</param>
	public void ReleaseFromDrag(float releaseSpeed)
	{
		if (!isHeld)
			return;

		bool spinning = spinLevel > 0 || releaseSpeed >= ReleaseRollSpeed;
		ClearHeld();

		if (spinning)
			Roll();
		else
			ShowResting();      // released without spinning: it just sits there
	}

	/// Drops the die without rolling it — used when a drag is interrupted rather
	/// than finished (deleted, respawned, knocked out of bounds).
	public void CancelDragging()
	{
		if (!isHeld)
			return;

		ClearHeld();
		ShowResting();
	}

	private void ClearHeld()
	{
		isHeld = false;
		spinLevel = 0;
	}

	private void UpdateHeldSpin()
	{
		int wanted = 0;
		float spin = Mathf.Abs(AngularVelocity);
		if (moveSpeed >= FastSpinOnSpeed || spin >= FastSpinOnAngular)
			wanted = 2;
		else if (moveSpeed >= SpinOnSpeed || spin >= SpinOnAngular)
			wanted = 1;

		// The level only ever ratchets up. Once the player has set the die going it
		// keeps tumbling in their hand until they let go, even if they then hold it
		// perfectly still — letting it fall back would park a spinning die on a
		// static frame mid-hold, which reads as the animation having broken.
		if (wanted > spinLevel)
			spinLevel = wanted;

		if (spinLevel == 0)
			return;         // never agitated yet: still on the resting frame

		StringName wantedAnimation = spinLevel == 2 ? Idle1 : Idle0;
		if (AnimatedSprite.Animation != wantedAnimation || !AnimatedSprite.IsPlaying())
			AnimatedSprite.Play(wantedAnimation);
	}

	// ------------------------------------------------------------------- rolling

	/// <param name="forced">
	/// 1-6 to land on a chosen face instead of a random one. Used by the screenshot
	/// tool in tools/screenshots so the generated images are reproducible; 0, the
	/// default, rolls normally.
	/// </param>
	public void Roll(int forced = 0)
	{
		if (isRolling)
			return;

		currentResult = forced is >= 1 and <= 6 ? forced : Random.Shared.Next(1, 7);
		ClearHeld();
		isRolling = true;

		// Play first, then rewind: Play() only rewinds when it actually changes clip,
		// so rolling the same number twice would otherwise resume mid-tumble.
		StringName animation = currentResult.ToString();
		AnimatedSprite.SpeedScale = 1f;
		AnimatedSprite.Play(animation);
		AnimatedSprite.Frame = 0;               // setting Frame also clears FrameProgress
	}

	private void OnAnimationFinished()
	{
		// Only the "1".."6" clips can reach this; idle0/idle1 loop instead.
		if (isRolling)
			FinishRoll();
	}

	private void FinishRoll()
	{
		isRolling = false;
		ShowResting();
		EmitSignal(SignalName.DiceRolled, currentResult);
	}

	/// Park the sprite on the resting pose: the roll clips end on the die sitting
	/// still, so that last frame *is* the idle picture for the current face.
	private void ShowResting()
	{
		StringName animation = currentResult.ToString();
		SpriteFrames frames = AnimatedSprite.SpriteFrames;
		if (frames == null || !frames.HasAnimation(animation))
			return;

		// Stop() rewinds to frame 0 and Animation= does the same, so both have to
		// happen before the frame is placed.
		AnimatedSprite.Stop();
		AnimatedSprite.Animation = animation;
		AnimatedSprite.Frame = frames.GetFrameCount(animation) - 1;
		AnimatedSprite.FrameProgress = 1f;
	}

	public int GetResult() => currentResult;

	public void SetHovered(bool hovered)
	{
		AnimatedSprite.Scale = hovered ? Vector2.One * 1.12f : Vector2.One;
		AnimatedSprite.ZIndex = hovered ? 20 : 0;
	}

	private void OnBodyEntered(Node body)
	{
		if (body is not Dice other)
			return;

		float impactSpeed = (LinearVelocity - other.LinearVelocity).Length();
		ulong now = Time.GetTicksMsec();
		if (!isHeld && !isRolling && impactSpeed >= CollisionRollSpeed
			&& now - lastCollisionRollMs >= 250)
		{
			lastCollisionRollMs = now;
			Roll();
		}
	}
}
