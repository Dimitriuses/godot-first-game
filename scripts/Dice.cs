using Godot;
using System;

public partial class Dice : RigidBody2D
{
	[Export] public AnimatedSprite2D AnimatedSprite;
	[Export] public CollisionShape2D CollisionShape;
	[Export] public float CollisionRollSpeed = 140f;

	private int currentResult = 1;
	private ulong lastCollisionRollMs;

	public bool isDragging = false;
	private bool isRolling = false;

	[Signal]
	public delegate void DiceRolledEventHandler(int result);

	public override void _Ready()
	{
		AnimatedSprite.AnimationFinished += OnAnimationFinished;
		ContactMonitor = true;
		MaxContactsReported = 4;
		BodyEntered += OnBodyEntered;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Keep the face upright while the body itself spins.
		AnimatedSprite.Rotation = -Rotation;

		if (!isRolling && AngularVelocity > 5)
		{
			AnimatedSprite.Play("idle0");
		}

		if (isDragging && AngularVelocity > 10)
		{
			AnimatedSprite.Play("idle1");
		}
	}

	public void Roll()
	{
		if (isRolling)
			return;

		currentResult = Random.Shared.Next(1, 7);
		isRolling = true;
		AnimatedSprite.Play(currentResult.ToString());
	}

	private void OnAnimationFinished()
	{
		if (isRolling)
		{
			isRolling = false;
			EmitSignal(SignalName.DiceRolled, currentResult);
		}
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
		if (!isRolling && impactSpeed >= CollisionRollSpeed && now - lastCollisionRollMs >= 250)
		{
			lastCollisionRollMs = now;
			Roll();
		}
	}
}
