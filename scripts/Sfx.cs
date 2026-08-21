using Godot;
using System.Collections.Generic;

/// <summary>
/// Every sound the game makes, and the small amount of policy that keeps them bearable.
///
/// The clips are generated, not recorded — see <c>tools/audio-render/make_sounds.py</c>,
/// which synthesises all seventeen from nothing and is the only place their character is
/// decided. Nothing here knows what a die sounds like; it knows how loud, how often and
/// how many at once.
///
/// Reached through a static instance rather than passed down through the UI. That is not
/// the pattern the rest of this project uses — panels raise events and the board decides —
/// but sound is the one thing every layer wants and no layer owns, and threading a
/// reference through five classes to play a click would be worse than this. The instance
/// is created and cleared by <see cref="GameManager"/>, and every entry point no-ops when
/// it is missing, so nothing here can break a scene that has no audio in it.
/// </summary>
public partial class Sfx : Node
{
	/// Its own bus, made at runtime so the project needs no bus layout resource. One
	/// place to put a volume slider when there is one.
	public const string BusName = "Sfx";

	/// Enough for a board of dice rattling without cutting each other off. Voices are
	/// reused oldest-first once they are all busy.
	private const int Voices = 14;

	private const string AudioDir = "res://assets/audio/";

	/// The shortest gap between two plays of the same sound. Without it a die skittering
	/// along a wall fires a clack a frame and it turns into a buzz.
	private static readonly Dictionary<string, ulong> MinGapMs = new()
	{
		["die_hit"] = 40,
		["ui_click"] = 45,
		["die_land"] = 60,
		// R is a key someone will hold down. Roll() restarts the clip each time, and
		// without this the rattle restarts with it, forty times a second.
		["die_roll"] = 220,
		// Mass operations go through the same call once per die. The gap is what makes
		// "Delete all" one sound rather than eight landing on the same frame.
		["delete"] = 130,
		["spawn"] = 90,
	};

	public static Sfx Instance { get; private set; }

	/// Everything is mixed here rather than at each call site, so the relative levels
	/// baked into the WAVs are what actually reaches the speakers.
	[Export] public float VolumeDb = -7f;

	private readonly List<AudioStreamPlayer> voices = new();
	private readonly Dictionary<string, AudioStream> streams = new();
	private readonly Dictionary<string, ulong> lastPlayed = new();
	private readonly Dictionary<AudioStreamPlayer, ulong> startedMs = new();
	private ulong dieHitsMutedUntil;
	private int hitVariant;
	private bool muted;

	public override void _Ready()
	{
		Instance = this;

		if (AudioServer.GetBusIndex(BusName) < 0)
		{
			int index = AudioServer.BusCount;
			AudioServer.AddBus(index);
			AudioServer.SetBusName(index, BusName);
			AudioServer.SetBusSend(index, "Master");
		}
		AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(BusName), VolumeDb);

		for (int i = 0; i < Voices; i++)
		{
			var player = new AudioStreamPlayer { Name = $"Voice{i}", Bus = BusName };
			AddChild(player);
			voices.Add(player);
			startedMs[player] = 0;
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	/// Whether sound is off. False when there is no Sfx at all, because a scene with no
	/// audio in it is not muted — it is silent, which is a different thing to report.
	public static bool Muted => Instance != null && Instance.muted;

	/// <summary>
	/// Turn everything off or on.
	///
	/// Muting the bus rather than skipping the calls: voices carry on being allocated and
	/// recycled exactly as before, so unmuting cannot land in a state the mixer has not
	/// been keeping up to date, and a sound already sounding stops rather than finishing
	/// under a mute the player has just asked for.
	/// </summary>
	public static void SetMuted(bool muted)
	{
		if (Instance == null)
			return;
		Instance.muted = muted;
		AudioServer.SetBusMute(AudioServer.GetBusIndex(BusName), muted);
	}

	/// <summary>
	/// Play a clip by name, in dB relative to how it was mixed, with optional pitch
	/// scatter so repeats do not sound mechanical.
	/// </summary>
	public static void Play(string name, float volumeDb = 0f, float pitchSpread = 0f)
	{
		Instance?.PlayNow(name, volumeDb, pitchSpread, name);
	}

	/// <summary>
	/// A die struck something, at `speed` px/s.
	///
	/// Loud in proportion, and quiet enough below a threshold to be dropped entirely —
	/// dice jostling as they settle would otherwise chatter for as long as the physics
	/// takes to go to sleep.
	/// </summary>
	public static void DieHit(float speed)
	{
		if (Instance == null || speed < QuietestHit)
			return;
		if (Time.GetTicksMsec() < Instance.dieHitsMutedUntil)
			return;

		float loudness = Mathf.Clamp(
			(speed - QuietestHit) / (LoudestHit - QuietestHit), 0f, 1f);
		// Compressive, not expansive. Measured rather than guessed: a die thrown at the
		// board's own ThrowSpeedMax arrives at a wall doing 90 to 350 px/s, so the useful
		// range is narrow, and anything that curves the other way leaves every ordinary
		// tap inaudible.
		float db = Mathf.Lerp(-18f, 0f, Mathf.Pow(loudness, 0.8f));

		Instance.hitVariant = (Instance.hitVariant + 1) % HitVariants;
		// One shared gap key for all four variants: they are the same event, and letting
		// each keep its own let four clacks land on the same frame.
		Instance.PlayNow($"die_hit_{Instance.hitVariant}", db, 0.12f, "die_hit");
	}

	/// The whole board going at once. It has its own clatter baked in, so the individual
	/// impacts are held off for as long as that lasts — and a die teleported by Throw()
	/// registers a contact it did not really have (see CLAUDE.md), which would otherwise
	/// fire a clack for every die on the board in the same frame.
	public static void ThrowAll()
	{
		if (Instance == null)
			return;
		Instance.dieHitsMutedUntil = Time.GetTicksMsec() + 260;
		Play("throw_all");
	}

	private const float QuietestHit = 40f;
	private const float LoudestHit = 330f;
	private const int HitVariants = 4;

	private void PlayNow(string name, float volumeDb, float pitchSpread, string gapKey)
	{
		ulong now = Time.GetTicksMsec();
		if (MinGapMs.TryGetValue(gapKey, out ulong gap)
			&& lastPlayed.TryGetValue(gapKey, out ulong last) && now - last < gap)
			return;

		AudioStream stream = StreamFor(name);
		if (stream == null)
			return;

		AudioStreamPlayer voice = FreeVoice();
		voice.Stream = stream;
		voice.VolumeDb = volumeDb;
		voice.PitchScale = pitchSpread > 0f
			? 1f + (float)GD.RandRange(-pitchSpread, pitchSpread) : 1f;
		voice.Play();

		lastPlayed[gapKey] = now;
		startedMs[voice] = now;
	}

	/// A voice that is idle, or the one that has been going longest. Stealing is better
	/// than dropping: the sound that gets cut is the one already fading out.
	private AudioStreamPlayer FreeVoice()
	{
		AudioStreamPlayer oldest = voices[0];
		foreach (AudioStreamPlayer voice in voices)
		{
			if (!voice.Playing)
				return voice;
			if (startedMs[voice] < startedMs[oldest])
				oldest = voice;
		}
		return oldest;
	}

	private AudioStream StreamFor(string name)
	{
		if (streams.TryGetValue(name, out AudioStream cached))
			return cached;
		var loaded = GD.Load<AudioStream>($"{AudioDir}{name}.wav");
		if (loaded == null)
			GD.PushWarning($"no sound called {name}");
		streams[name] = loaded;
		return loaded;
	}
}
