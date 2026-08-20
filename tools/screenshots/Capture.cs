using Godot;
using System.Threading.Tasks;

/// <summary>
/// Renders the images used in README.md, deterministically.
///
/// Loads the real <c>game.tscn</c>, arranges a fixed board, and writes:
///   screenshot.png       the board at rest, palette and die list open
///   roll/f000..f090.png  one PNG per frame of a die's roll animation
///
/// Determinism comes from never letting anything free-run. Dice are placed at
/// literal coordinates and put down on chosen faces with <c>Dice.PlaceOnFace</c>
/// rather than rolled — the number is decided by physics now, so it cannot be
/// requested — and the roll frames are captured by setting
/// <c>AnimatedSprite2D.Frame</c> by hand with playback stopped. Nothing depends on
/// wall-clock time or on how fast the machine renders. The dice also have
/// <c>_PhysicsProcess</c> switched off for the duration, so their own state machine
/// cannot overwrite the frames set here.
///
/// Driven by capture.py; see the README in this directory. Needs a real window,
/// because --headless has no renderer.
/// </summary>
public partial class Capture : Node
{
	private const int Width = 1152;
	private const int Height = 648;
	private const string RollAnimation = "3";

	// Board layout: position -> the face that die should show, and which entry of
	// GameManager.DiceScenes it is. Literal, so the same picture comes out every time.
	// Entry 0 is the die game.tscn already contains, which is the d6, so its Scene must
	// be the d6's index.
	//
	// Five dice, not one of every type: the die list holds five rows before it scrolls,
	// and a shot of it opening onto a half-cut row is a poor advert. The numbered d6 is
	// the one left off, being the least distinct from the die already there. The palette
	// beside it still offers all six.
	private static readonly (Vector2 Pos, int Face, int Scene)[] Board =
	{
		(new Vector2(400, 272), 5, 1),      // d6
		(new Vector2(702, 258), 3, 0),      // d4
		(new Vector2(842, 402), 17, 5),     // d20
		(new Vector2(468, 474), 7, 4),      // d10
		(new Vector2(250, 232), 8, 3),      // d8
	};
	// Clear of the orange floor decoration and of the die-list panel.
	private static readonly Vector2 RollingDiePosition = new(599, 250);

	private string outDir = "res://tools/screenshots/build";

	public override async void _Ready()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
			if (arg.StartsWith("--out="))
				outDir = arg["--out=".Length..];

		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(outDir));
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(outDir + "/roll"));

		DisplayServer.WindowSetSize(new Vector2I(Width, Height));
		await Frames(4);

		var game = GD.Load<PackedScene>("res://scenes/game.tscn").Instantiate<GameManager>();
		AddChild(game);
		await Frames(10);           // let _Ready build the palette and the HUD

		await ArrangeBoard(game);
		await CaptureStill(game);
		await CaptureRoll(game);

		GD.Print("capture complete -> ", outDir);
		GetTree().Quit(0);
	}

	private async Task ArrangeBoard(GameManager game)
	{
		// One die already sits in game.tscn; add the rest through the same path the
		// palette uses, so each one is registered with the HUD.
		for (int i = 1; i < Board.Length; i++)
		{
			int k = Board[i].Scene;
			if (k >= game.DiceScenes.Count)
				GD.PushWarning($"Board entry {i} wants die scene {k}; only "
					+ $"{game.DiceScenes.Count} are configured");
			game.SpawnDie(game.DiceScenes[Mathf.Min(k, game.DiceScenes.Count - 1)],
				Board[i].Pos);
		}
		await Frames(4);

		var dice = new Godot.Collections.Array<Dice>();
		foreach (Node child in game.GetChildren())
			if (child is Dice die)
				dice.Add(die);

		if (dice.Count != Board.Length)
			GD.PushWarning($"expected {Board.Length} dice, found {dice.Count}");

		// Park the dice BEFORE moving any of them. This tool teleports dice, and a
		// kinematic frozen body moved that way registers as a hard contact, so the
		// collision re-roll starts clips playing behind the shot. Doing it afterwards
		// is too late: the roll has already begun, and AnimatedSprite2D keeps
		// advancing in _process even with _PhysicsProcess off.
		//
		// Switch off ContactMonitor rather than the collision layer. Zeroing the layer
		// also hides the dice from the bounds Area2D, whose BodyExited then fires for
		// every one of them and the out-of-bounds recovery teleports the whole board
		// back to the spawn point.
		// InputPickable off for the same reason, and it is not optional: a real window
		// opens for a few seconds, Godot picks against wherever the operator's cursor
		// actually is, and a cursor resting over a die raises the hover tag into the
		// shot. Clear any hover already raised too — mouse_exited does not fire for a
		// body that stops being pickable.
		var hud = game.GetNode<CanvasLayer>("GameUiLayer").GetNode<DiceHud>("DiceHud");
		foreach (Dice die in dice)
		{
			die.SetPhysicsProcess(false);
			die.ContactMonitor = false;
			die.InputPickable = false;
			hud.SetDieHovered(die, false);
		}

		for (int i = 0; i < dice.Count && i < Board.Length; i++)
		{
			Place(dice[i], Board[i].Pos);
			// PlaceOnFace is a put-down rather than a roll: it parks the sprite on
			// the face's resting frame and reports it, with no clip to wait out and
			// nothing random. It runs last so it clears any state the move left.
			dice[i].PlaceOnFace(Board[i].Face);
		}
		await Frames(4);

		game.GetNode<CanvasLayer>("GameUiLayer").GetNode<DicePalette>("DicePalette")
			.SetDrawerOpen(true, false);
		game.GetNode<CanvasLayer>("GameUiLayer").GetNode<DiceHud>("DiceHud")
			.SetOpen(true, false);
		await Frames(20);                        // tweens are instant, but let UI lay out
	}

	private static void Place(Dice die, Vector2 position)
	{
		die.Freeze = true;
		die.LinearVelocity = Vector2.Zero;
		die.AngularVelocity = 0;
		die.Rotation = 0;
		die.GlobalPosition = position;
	}

	private async Task CaptureStill(GameManager game)
	{
		await Save(outDir + "/screenshot.png");
		GD.Print("  screenshot.png");
	}

	private async Task CaptureRoll(GameManager game)
	{
		Dice roller = null;
		foreach (Node child in game.GetChildren())
			if (child is Dice die) { roller = die; break; }
		if (roller == null)
		{
			GD.PushError("no die to roll");
			return;
		}

		Place(roller, RollingDiePosition);
		AnimatedSprite2D sprite = roller.AnimatedSprite;
		int frames = sprite.SpriteFrames.GetFrameCount(RollAnimation);

		// Step the clip by hand with playback stopped: one output PNG per source
		// frame, independent of render speed.
		sprite.Stop();
		sprite.Animation = RollAnimation;

		for (int f = 0; f < frames; f++)
		{
			sprite.Frame = f;
			await Save($"{outDir}/roll/f{f:D3}.png");
		}
		GD.Print($"  roll/ {frames} frames");
	}

	private async Task Save(string path)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image image = GetViewport().GetTexture().GetImage();
		if (image.GetWidth() != Width || image.GetHeight() != Height)
			GD.PushWarning($"captured {image.GetWidth()}x{image.GetHeight()}, "
				+ $"expected {Width}x{Height} — check window scaling");
		image.SavePng(path);
	}

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
}
