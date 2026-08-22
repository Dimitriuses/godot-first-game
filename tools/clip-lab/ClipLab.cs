using Godot;
using System.Threading.Tasks;

/// <summary>
/// A prototype of the two-part roll: one shared blurred prefix, then a per-face tail,
/// with the prefix stretched to whatever duration the throw wants.
///
///     godot --headless --path . res://tools/clip-lab/clip_lab.tscn --quit-after 2000
///
/// It splices existing clips — the prefix comes from face 1 and the tail from whichever
/// face was rolled — so the *join is visibly wrong* (see out/seam.png; the clips are
/// separate trajectories). That is deliberate: this is here to answer whether the
/// playback machinery works and what durations it can produce, not to look right. The
/// art question is settled by `analyse.py` and needs a re-render.
/// </summary>
public partial class ClipLab : Node
{
	/// The frame the shared part hands over on. 30 is where the pictures say the die is
	/// still an unreadable smear; see analyse.py.
	private const int Handoff = 30;
	private const int RollFrames = 91;
	private const int Fps = 30;

	private AnimatedSprite2D sprite;

	public override async void _Ready()
	{
		var die = GD.Load<PackedScene>("res://scenes/dice.tscn").Instantiate<Dice>();
		AddChild(die);
		die.Freeze = true;
		sprite = die.AnimatedSprite;

		GD.Print("Two-part roll: shared prefix 0..", Handoff - 1,
			" then the face's own tail ", Handoff, "..", RollFrames - 1);
		GD.Print($"A whole clip at 1x is {RollFrames / (float)Fps:0.00}s.\n");
		GD.Print($"{"stretch",8}{"prefix s",10}{"tail s",9}{"total s",9}"
			+ $"{"handoff hit",14}{"landed on",11}");

		foreach (float stretch in new[] { 0.5f, 1.0f, 1.75f, 3.0f })
			await RollOnce(6, stretch);

		GD.Print("\nThe tail is never stretched: it is the part with the answer in it,");
		GD.Print("and slowing it down reads as the die hesitating. Only the blur");
		GD.Print("stretches, which is what makes a long throw cost no extra frames.");
		GetTree().Quit(0);
	}

	/// <summary>
	/// Play one roll and report what it did.
	///
	/// The prefix runs at a reduced SpeedScale to stretch it; the tail always runs at
	/// 1x. Switching clip mid-roll is the same trick `Dice.Roll` already uses to resume
	/// a tumble — `Play` only rewinds when the clip name changes, so the frame has to be
	/// set afterwards.
	/// </summary>
	private async Task RollOnce(int face, float stretch)
	{
		ulong began = Time.GetTicksMsec();

		sprite.SpeedScale = 1f / stretch;
		sprite.Play("1");
		sprite.Frame = 0;
		while (sprite.Frame < Handoff - 1)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		ulong handedOver = Time.GetTicksMsec();
		int hitAt = sprite.Frame;

		sprite.SpeedScale = 1f;
		sprite.Play(face.ToString());
		sprite.Frame = Handoff;
		while (sprite.Frame < RollFrames - 1)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		ulong ended = Time.GetTicksMsec();
		sprite.Stop();

		GD.Print($"{stretch,8:0.00}{(handedOver - began) / 1000f,10:0.00}"
			+ $"{(ended - handedOver) / 1000f,9:0.00}{(ended - began) / 1000f,9:0.00}"
			+ $"{hitAt,14}{sprite.Animation,11}");
	}
}
