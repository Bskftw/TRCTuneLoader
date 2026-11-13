// Sharing speed traps: your own traps to the room or one player, and adding traps others shared.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		// Sharing (0.15.2).
		private bool sharingTraps_;
		private ulong shareTrapsTo_;
		private string shareTrapsGroup_ = "";

		/// <summary>Your own traps on this map, to the whole room or one player: all of them, or one group.</summary>
		private void DrawShareTraps() {
			List<SpeedTrap> mine = mapTraps_.Where(t => t.Source == CourseSource.Custom).ToList();
			List<RoomMessage> players = roomBoard_.Fresh(BattleNow).Where(s => s.From != MySteamId).ToList();
			if (shareTrapsTo_ != 0 && !players.Any(p => p.From == shareTrapsTo_)) {
				shareTrapsTo_ = 0;
			}
			List<string> groups = mine.Select(t => t.Group).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
			if (shareTrapsGroup_.Length > 0 && !groups.Contains(shareTrapsGroup_, StringComparer.OrdinalIgnoreCase)) {
				shareTrapsGroup_ = "";
			}

			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("Share your speed traps", Theme.Heading);
			GUILayout.Label("Send the speed traps you placed on this map. The other player gets them in their Speed traps section and adds them with one click.", Theme.Small);
			GUILayout.Label("With", Theme.SectionLabel);
			var who = new List<string> { "Everyone in the room" };
			who.AddRange(players.Select(p => BattleMessage.PlainName(p.Name)));
			int pickedWho = Choice(who.ToArray(), shareTrapsTo_ == 0 ? 0 : players.FindIndex(p => p.From == shareTrapsTo_) + 1);
			if (pickedWho >= 0) {
				shareTrapsTo_ = pickedWho == 0 ? 0 : players[pickedWho - 1].From;
			}
			if (players.Count == 0) {
				GUILayout.Label("No other TRC Tune Loader players in the room right now: everyone who joins later won't get them.", Theme.Small);
			}
			if (groups.Count > 0) {
				GUILayout.Label("Which", Theme.SectionLabel);
				var what = new List<string> { $"All ({mine.Count})" };
				what.AddRange(groups.Select(g => $"{g} ({mine.Count(t => string.Equals(t.Group, g, StringComparison.OrdinalIgnoreCase))})"));
				int pickedWhat = Choice(what.ToArray(), shareTrapsGroup_.Length == 0 ? 0 : groups.FindIndex(g => string.Equals(g, shareTrapsGroup_, StringComparison.OrdinalIgnoreCase)) + 1);
				if (pickedWhat >= 0) {
					shareTrapsGroup_ = pickedWhat == 0 ? "" : groups[pickedWhat - 1];
				}
			}
			List<SpeedTrap> sending = shareTrapsGroup_.Length == 0 ? mine : mine.Where(t => string.Equals(t.Group, shareTrapsGroup_, StringComparison.OrdinalIgnoreCase)).ToList();
			if (sending.Count > RoomMessage.MaxTraps) {
				GUILayout.Label($"That's {sending.Count} traps: the first {RoomMessage.MaxTraps} go in one share. Share a group at a time for the rest.", Theme.PillWarn);
			}
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && sending.Count > 0 && roomSync_.IsAvailable;
			string target = shareTrapsTo_ == 0 ? "the room" : who[pickedWho < 0 ? 0 : pickedWho];
			if (NavButton($"Send {Math.Min(sending.Count, RoomMessage.MaxTraps)} trap(s) to {target}", Theme.ButtonPrimary)) {
				ShareTraps(sending, shareTrapsGroup_.Length == 0 ? "All their traps" : shareTrapsGroup_, shareTrapsTo_, target);
			}
			GUI.enabled = enabled;
			if (NavButton("Done", Theme.Button)) {
				sharingTraps_ = false;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!roomSync_.IsAvailable) {
				GUILayout.Label(roomSync_.Status, Theme.Small);
			}
			GUILayout.EndVertical();
		}

		private void ShareTraps(List<SpeedTrap> traps, string label, ulong to, string target) {
			var m = new RoomMessage {
				Kind = RoomKind.TrapShare, From = MySteamId, Name = MyName(), To = to, MapKey = map_.Key, MapName = map_.Name, Label = label,
				Traps = traps.Take(RoomMessage.MaxTraps).ToList(),
			};
			int pieces = roomSync_.Send(m);
			courseResult_ = pieces > 0
				? $"Sent {m.Traps.Count} speed trap(s) to {target}. They need TRC Tune Loader 0.16.0 or newer, and can add them in Time attack, Speed traps."
				: "Couldn't send them: is the room's connection all right?";
			TrcLog.Info($"Shared {m.Traps.Count} speed trap(s) ({label}) on {map_.Key} with {(to == 0 ? "the room" : to.ToString(CultureInfo.InvariantCulture))}: {pieces} packet(s).");
		}

		/// <summary>Speed traps others shared: add them to your own for that map (any map, not only this one), or dismiss.</summary>
		private void DrawSharedTraps() {
			if (roomBoard_.SharedTrapSets.Count == 0) {
				return;
			}
			GUILayout.Label("Speed traps shared with you", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (RoomBoard.SharedTraps shared in roomBoard_.SharedTrapSets.ToList()) {
				GUILayout.BeginHorizontal();
				string map = shared.MapName.Length > 0 ? shared.MapName : shared.MapKey;
				string here = map_ != null && map_.Key == shared.MapKey ? "" : " (another map)";
				GUILayout.Label($"<b>{shared.Label}</b>  <color={Theme.MutedHex}>from {shared.FromName}{(shared.JustForMe ? ", just for you" : "")} · {shared.Traps.Count} trap(s) · {map}{here}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (NavButton("Add them", Theme.ButtonPrimary)) {
					AddSharedTraps(shared);
				}
				if (NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedTrapSets.Remove(shared);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void AddSharedTraps(RoomBoard.SharedTraps shared) {
			try {
				string path = TrapsFileFor(shared.MapKey);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				List<SpeedTrap> mine = SpeedTrapFile.Load(path, shared.MapKey);
				// The map's own trap objects count as there already, when the shared traps are for this map.
				IEnumerable<SpeedTrap> mapOwn = map_ != null && map_.Key == shared.MapKey ? mapTraps_.Where(t => t.Source != CourseSource.Custom) : null;
				int added = RoomBoard.MergeTraps(mine, shared.Traps, mapOwn);
				if (added > 0) {
					SpeedTrapFile.Save(path, mine);
				}
				roomBoard_.SharedTrapSets.Remove(shared);
				if (map_ != null && map_.Key == shared.MapKey) {
					RefreshTraps();
					string group = SpeedTrap.GroupOf(shared.Label);
					if (group.Length > 0) {
						trapGroupsOpen_.Add(group);
					}
				}
				int skipped = shared.Traps.Count - added;
				courseResult_ = $"Added {added} speed trap(s) from {shared.FromName}{(skipped > 0 ? $" ({skipped} were already there)" : "")}" +
					$"{(map_ != null && map_.Key == shared.MapKey ? "." : $": they show when you're on {shared.MapName}.")}";
				TrcLog.Info($"Added {added} of {shared.Traps.Count} speed trap(s) {shared.FromName} shared ({shared.Label}) on {shared.MapKey}.");
			} catch (Exception e) {
				courseResult_ = $"Couldn't add them: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
