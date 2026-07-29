using Godot;
using System;

public partial class Dice : RigidBody2D
{
	[Export] public AnimatedSprite2D AnimatedSprite;
	[Export] public CollisionShape2D CollisionShape;

	private Random random = new Random();
	private int currentResult = 1;

	public bool isDragging = false;
	private bool isRolling = false;

	[Signal]
	public delegate void DiceRolledEventHandler(int result);

	public override void _Ready()
	{
		AnimatedSprite.AnimationFinished += OnAnimationFinished;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Keep the face upright while the body itself spins.
		AnimatedSprite.Rotation = -Rotation;

		if (AngularVelocity > 5)
		{
			AnimatedSprite.Play("idle0");
		}
		else if (!isDragging && AnimatedSprite.IsPlaying()
			&& (AnimatedSprite.Animation == "idle0" || AnimatedSprite.Animation == "idle1"))
		{
			Roll();
		}

		if (isDragging && AngularVelocity > 10)
		{
			AnimatedSprite.Play("idle1");
		}
	}

	public void Roll()
	{
		currentResult = random.Next(1, 7);
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
}
