using Godot;
using System;

/// <summary>
/// A die, and the small state machine that drives its sprite.
///
/// Three visual states, and every transition sets the sprite explicitly, so
/// nothing ever has to poll for or repair a stalled animation:
///
///   Resting  one static frame — the last frame of <see cref="currentResult"/>'s
///            clip, which is the die sitting still showing that face.
///   Held     idle0 / idle1 loop while the player agitates it. These loops play
///            *only* while the die is held, so a free die is never left looping.
///   Rolling  one of the numbered clips. It ends on its own last frame, which is
///            the resting pose, and emits <see cref="DiceRolledEventHandler"/>.
///
/// The number is not a bare random draw. It is taken from **where the die was in
/// its tumble at the moment it was let go**, nudged by a random factor — see
/// <see cref="ChooseResult"/>. The roll clip also picks up from that same frame,
/// so the spin the player was watching carries into the throw instead of cutting.
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

	/// How far the random factor may move the face away from the one the release
	/// frame picked. 0 makes the throw fully aimable; 3 makes the frame irrelevant.
	[Export] public int ResultJitter = 2;

	/// What one face is worth when the die reports its result. 10 on a percentile d10,
	/// which is printed in tens and whose "00" face is the tenth; 1 on everything else.
	/// The stored result stays 1..FaceCount either way, because that is what names the
	/// animation clips — this only changes the number the HUD is told.
	[Export] public int ValueStep = 1;

	/// What the palette calls this die. Empty means "work it out from the face count",
	/// which is right for every die until two of them have the same one -- a pipped and
	/// a numbered d6 would both come out "D6".
	[Export] public string DieLabel = "";

	/// How hard a held die must be moved to spin it up to idle0, then to idle1.
	[Export] public float SpinOnSpeed = 120f;
	[Export] public float FastSpinOnSpeed = 600f;
	/// The same, for a die swung hard enough to rotate about the mouse pin.
	[Export] public float SpinOnAngular = 1.5f;
	[Export] public float FastSpinOnAngular = 8f;

	/// Impulse Throw() gives a die, so the Space key scatters them across the board
	/// rather than animating them on the spot.
	[Export] public float ThrowSpeedMin = 220f;
	[Export] public float ThrowSpeedMax = 400f;
	[Export] public float ThrowSpinMax = 9f;

	private int currentResult = 1;
	private int faceCount;              // 0 until first read; see FaceCount
	private bool isHeld;
	private bool isRolling;
	private int spinLevel;              // 0 resting, 1 idle0, 2 idle1
	private float moveSpeed;
	private Vector2 lastPosition;
	private ulong lastCollisionRollMs;

	public bool IsHeld => isHeld;
	public bool IsRolling => isRolling;
	/// Which face is up: 1..FaceCount, and the name of that face's clip.
	public int GetResult() => currentResult;

	/// What that face is worth — what a player reads off the die. The same number for
	/// every die but the percentile d10, which shows 10 through 90 and then 00 for 100.
	public int Value => currentResult * ValueStep;

	/// <summary>
	/// How many numbered faces this die has, counted from its own clips rather than
	/// exported, so a d20 scene needs no extra wiring. Resolved on first use, which
	/// may be before `_Ready` — the palette reads it off a scene it has only
	/// instantiated.
	/// </summary>
	public int FaceCount => faceCount > 0 ? faceCount : (faceCount = CountFaces());

	private int CountFaces()
	{
		SpriteFrames frames = AnimatedSprite?.SpriteFrames;
		if (frames == null)
			return 6;

		var numbered = new System.Collections.Generic.HashSet<int>();
		foreach (StringName name in frames.GetAnimationNames())
			if (int.TryParse(name.ToString(), out int v))
				numbered.Add(v);

		// They have to be 1..N with nothing missing, or the mapping from a release
		// frame onto a face would silently point at a clip that is not there.
		for (int i = 1; i <= numbered.Count; i++)
			if (!numbered.Contains(i))
			{
				GD.PushWarning($"{Name}: numbered clips are not 1..{numbered.Count}");
				return 6;
			}
		return numbered.Count > 0 ? numbered.Count : 6;
	}

	/// Radius of the die's collider, and where that collider sits relative to the body
	/// origin — it is deliberately offset to line up with the drawn die. GameManager
	/// reads both to work out how far a dragged die may go before it touches a wall.
	public float CollisionRadius =>
		CollisionShape?.Shape is CircleShape2D circle ? circle.Radius : 32f;

	public Vector2 CollisionOffset => CollisionShape?.Position ?? Vector2.Zero;

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

	/// <summary>
	/// Throw the die across the board and roll it. This is what the Space key does:
	/// the dice scatter as well as animating, rather than tumbling on the spot.
	///
	/// A die already in the air is thrown again rather than skipped. Sharing
	/// <see cref="Roll"/>'s guard meant a board of dice split into two groups that
	/// could never be brought back together: every press threw whichever group was
	/// resting and passed over whichever was mid-clip, so they alternated forever.
	/// The guard is still right for a collision, which should not restart a roll that
	/// is already running; it is wrong for someone asking for a throw.
	/// </summary>
	public void Throw()
	{
		float angle = Random.Shared.NextSingle() * Mathf.Tau;
		float speed = Random.Shared.NextSingle() * (ThrowSpeedMax - ThrowSpeedMin)
			+ ThrowSpeedMin;

		Freeze = false;
		LinearVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
		AngularVelocity = (Random.Shared.NextSingle() * 2f - 1f) * ThrowSpinMax;
		Roll(restart: true);
	}

	/// <summary>
	/// Play a roll. The face comes from where the die was in its tumble when this
	/// was called, plus a random nudge; the clip picks up from that same frame.
	/// </summary>
	/// <param name="restart">
	/// Roll a die that is already rolling, from the top. Off by default, so a die
	/// knocked about mid-clip keeps the throw it is in the middle of; on for an
	/// explicit throw, which has to reach every die or the board drifts out of phase.
	/// </param>
	public void Roll(bool restart = false)
	{
		if (isRolling && !restart)
			return;

		float releasePos = CurrentIdlePosition();
		currentResult = ChooseResult(releasePos);
		ClearHeld();
		isRolling = true;

		StringName clip = currentResult.ToString();
		SpriteFrames frames = AnimatedSprite.SpriteFrames;
		int start = 0;
		if (releasePos >= 0f && frames != null && frames.HasAnimation(clip))
			start = Mathf.Min((int)releasePos, frames.GetFrameCount(clip) - 1);

		// Play first, then place the frame: Play() only rewinds when it actually
		// changes clip, so rolling the same number twice would otherwise resume
		// wherever the previous one left off.
		AnimatedSprite.SpeedScale = 1f;
		AnimatedSprite.Play(clip);
		AnimatedSprite.Frame = start;       // setting Frame also clears FrameProgress
	}

	/// How far through the idle loop the tumble had got, or -1 if the die is not
	/// tumbling — a die at rest has no release moment to read.
	///
	/// Continuous, not the bare frame index. With six faces over a thirty-frame loop
	/// each face owned exactly five frames, so the integer worked out even; twenty
	/// faces over thirty frames would give some faces two frames and some one, biasing
	/// the die 2:1. FrameProgress makes the position continuous and the split even for
	/// any face count.
	private float CurrentIdlePosition()
	{
		StringName animation = AnimatedSprite.Animation;
		if (animation != Idle0 && animation != Idle1)
			return -1f;
		return AnimatedSprite.Frame + AnimatedSprite.FrameProgress;
	}

	/// <summary>
	/// Pick the face. Where the tumble had got to when the die was let go chooses a
	/// base face — the idle loop covers every face across its length — and a random
	/// offset decides how close to that it actually lands, so the throw can be
	/// influenced but not aimed. A die that was not tumbling has no release moment to
	/// read and falls back to a plain draw.
	/// </summary>
	private int ChooseResult(float releasePos)
	{
		SpriteFrames frames = AnimatedSprite.SpriteFrames;
		int idleLength = frames != null && frames.HasAnimation(Idle0)
			? frames.GetFrameCount(Idle0) : 0;
		int faces = FaceCount;

		int baseFace = releasePos >= 0f && idleLength > 0
			? Mathf.Clamp((int)(releasePos * faces / idleLength), 0, faces - 1) + 1
			: Random.Shared.Next(1, faces + 1);

		if (ResultJitter <= 0)
			return WrapFace(baseFace);

		int offset = Random.Shared.Next(-ResultJitter, ResultJitter + 1);
		return WrapFace(baseFace + offset);
	}

	private int WrapFace(int face)
	{
		int n = FaceCount;
		return ((face - 1) % n + n) % n + 1;
	}

	private void OnAnimationFinished()
	{
		// Only the numbered clips can reach this; idle0/idle1 loop instead.
		if (isRolling)
			FinishRoll();
	}

	private void FinishRoll()
	{
		isRolling = false;
		ShowResting();
		EmitSignal(SignalName.DiceRolled, currentResult);
	}

	// ---------------------------------------------------------------- appearance

	/// Park the sprite on the resting pose: the roll clips end on the die sitting
	/// still, so that last frame *is* the idle picture for the current face.
	private void ShowResting()
	{
		StringName clip = currentResult.ToString();
		SpriteFrames frames = AnimatedSprite.SpriteFrames;
		if (frames == null || !frames.HasAnimation(clip))
			return;

		// Stop() rewinds to frame 0 and Animation= does the same, so both have to
		// happen before the frame is placed.
		AnimatedSprite.Stop();
		AnimatedSprite.Animation = clip;
		AnimatedSprite.Frame = frames.GetFrameCount(clip) - 1;
		AnimatedSprite.FrameProgress = 1f;
	}

	/// <summary>
	/// Put the die down showing a chosen face, without rolling for it. This is how
	/// the screenshot tool gets a reproducible board; it is also what anything
	/// restoring a saved board would want.
	/// </summary>
	public void PlaceOnFace(int face)
	{
		if (face < 1 || face > FaceCount)
			return;

		currentResult = face;
		isRolling = false;
		ClearHeld();
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0f;
		lastPosition = GlobalPosition;
		AnimatedSprite.SpeedScale = 1f;
		ShowResting();
		EmitSignal(SignalName.DiceRolled, face);
	}

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
