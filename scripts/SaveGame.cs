using Godot;

/// <summary>
/// Reading and writing the one save file. Nothing here knows what a die is — it takes a
/// dictionary, puts it on disk, and hands it back.
///
/// **`user://`, deliberately, because of the browser.** Godot maps that path to IndexedDB
/// in a web export, so the same `FileAccess` calls that write a file on a desktop write to
/// browser storage in a tab, with no branch and no JavaScript. Anything that reached for
/// `localStorage` through `JavaScriptBridge` would work in the browser and nowhere else,
/// and would have to be ported twice — once to GDScript for the web build (ROADMAP 9) and
/// once back for the desktop.
///
/// The format is Godot's own `Json` over `Godot.Collections.Dictionary` rather than
/// `System.Text.Json`, for the same reason: it is the half of this that survives the
/// GDScript port untouched.
/// </summary>
public static class SaveGame
{
	private const string Path = "user://board.json";

	/// The schema this build writes. A file from the future is left alone rather than
	/// half-read: a save that loads wrong is worse than one that does not load.
	public const int Version = 1;

	/// Whether there is anything to restore. Whether the game *should* restore it is
	/// <see cref="GameManager.PersistBoard"/>'s business, not this class's.
	public static bool Exists() => FileAccess.FileExists(Path);

	/// <summary>
	/// The saved state, or null when there is none, it cannot be read, or it was written
	/// by a newer build.
	///
	/// Never throws. A corrupt save is a thing a player can end up with and it must cost
	/// them their board, not the game.
	/// </summary>
	public static Godot.Collections.Dictionary Load()
	{
		if (!FileAccess.FileExists(Path))
			return null;

		using FileAccess file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushWarning($"save exists but will not open: {FileAccess.GetOpenError()}");
			return null;
		}

		Variant parsed = Json.ParseString(file.GetAsText());
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			GD.PushWarning("save is not a JSON object; ignoring it");
			return null;
		}

		var data = parsed.AsGodotDictionary();
		int version = data.TryGetValue("version", out Variant v) ? (int)v : 0;
		if (version > Version)
		{
			GD.PushWarning($"save is version {version}, this build reads {Version}");
			return null;
		}
		return data;
	}

	public static void Store(Godot.Collections.Dictionary data)
	{
		data["version"] = Version;

		using FileAccess file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PushWarning($"cannot write the save: {FileAccess.GetOpenError()}");
			return;
		}
		file.StoreString(Json.Stringify(data, "  "));
		// Closed by the `using`, and the close is what matters on the web: that is when
		// Godot flushes the file into IndexedDB.
	}

	public static void Delete()
	{
		if (FileAccess.FileExists(Path))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
	}
}
