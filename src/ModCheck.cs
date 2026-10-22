using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;

namespace TRC.TuneLoader {
	/// <summary>
	/// Snapshot of the mods KSL has loaded. This is the leaderboard "mod list check": a run done with
	/// an inspector tool or an unknown legacy mod loaded can be flagged for review. The same rules are
	/// used for the lists other TRC Tune Loader users share in a room.
	/// </summary>
	public sealed class ModCheck {
		// Name fragments of tools that can edit the game while it runs.
		private static readonly string[] FlaggedNames = { "unityexplorer", "runtimeunityeditor", "cheat", "trainer" };

		public bool AllVerified { get; private set; }
		public bool ProtectedMode { get; private set; }
		public List<TrcSync.ModEntry> Entries { get; } = new List<TrcSync.ModEntry>();
		public List<string> Mods => Entries.Select(Line).ToList();
		public List<string> Flags { get; private set; } = new List<string>();

		public void Refresh() {
			Entries.Clear();
			Flags = new List<string>();

			try {
				AllVerified = Kino.Loader.IsAllModsVerified();
				ProtectedMode = Kino.Loader.IsProtectedModeActive();

				foreach (var mod in Kino.Loader.GetLoadedMods()) {
					Entries.Add(new TrcSync.ModEntry {
						Name = mod.Name ?? "",
						Version = mod.Version?.ToString() ?? "",
						Author = mod.Author ?? "",
						Type = mod.Type.ToString(),
					});
				}

				Flags = FlagsFor(Entries);
			} catch (Exception e) {
				Flags.Add($"Mod check failed: {e.Message}");
			}
		}

		public static string Line(TrcSync.ModEntry mod) => $"{mod.Name} v{mod.Version} by {mod.Author} ({mod.Type})";

		public static List<string> FlagsFor(IEnumerable<TrcSync.ModEntry> mods) {
			var flags = new List<string>();
			foreach (var mod in mods) {
				string lower = (mod.Name ?? "").ToLowerInvariant();
				if (FlaggedNames.Any(lower.Contains)) {
					flags.Add($"{mod.Name}: name looks like a game-editing tool");
				}
				if (mod.Type == nameof(ModType.Unknown) || mod.Type == nameof(ModType.UnknownMod)) {
					flags.Add($"{mod.Name}: unknown mod type");
				}
			}
			return flags;
		}
	}
}
