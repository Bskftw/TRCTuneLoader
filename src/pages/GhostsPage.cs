// A track's ghosts on the Tracks page: the list, naming, sharing a run, and runs shared on other maps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		// Renaming a ghost in the list.
		private Ghost renamingGhost_;
		private string ghostName_ = "";

		private Ghost sharingGhost_;
		private ulong shareGhostTo_;

		/// <summary>The ghosts button on a track's row, and its list when open.</summary>
		private void DrawGhostsButton(Course course) {
			int count = SavedGhostsFor(course.Code).Count + roomBoard_.SharedRuns.Count(r => r.Ghost.CourseCode == course.Code) +
				(lastGhost_ != null && lastGhost_.CourseCode == course.Code && lastGhost_.FilePath.Length == 0 ? 1 : 0);
			string label = racingGhost_ != null && racingGhost_.CourseCode == course.Code ? "Ghost on" : count > 0 ? $"Ghosts ({count})" : "Ghosts";
			if (NavButton(ghostsOpenFor_ == course.Code ? "Hide ghosts" : label, Theme.Button)) {
				ghostsOpenFor_ = ghostsOpenFor_ == course.Code ? "" : course.Code;
			}
		}

		private void DrawGhostList(Course course) {
			if (ghostsOpenFor_ != course.Code) {
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("Ghosts", Theme.Heading);
			GUILayout.Label("Race a ghost in a time attack: it's a see-through car with no collider or physics, so it can't push, tow or slipstream you. " +
				"The live delta and your sectors compare with it. " + (ghostMode_ == KeepEveryGhost ? "Every run you finish is kept." : ghostMode_ == KeepBestGhosts
					? "Your 3 best time attacks on each car are kept by themselves; Save keeps any other." : "Recording ghosts is off (Options)."), Theme.Small);
			if (racingGhost_ != null && racingGhost_.CourseCode == course.Code) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"Racing: {racingGhost_.Title}", Theme.PillGood);
				GUILayout.FlexibleSpace();
				if (NavButton("Ghost off", Theme.Button)) {
					StopGhost();
				}
				GUILayout.EndHorizontal();
			}
			var rows = new List<(Ghost Ghost, string From, RoomBoard.SharedItem Shared)>();
			if (lastGhost_ != null && lastGhost_.CourseCode == course.Code && lastGhost_.FilePath.Length == 0) {
				rows.Add((lastGhost_, $"your last {lastGhost_.Kind}", null));
			}
			rows.AddRange(roomBoard_.SharedRuns.Where(r => r.Ghost.CourseCode == course.Code).Select(r => (r.Ghost, $"shared by {r.FromName}", r)));
			rows.AddRange(SavedGhostsFor(course.Code).Select(g => (g, g.DriverId == MySteamId ? "yours, saved" : "saved", (RoomBoard.SharedItem)null)));
			if (rows.Count == 0) {
				GUILayout.Label("No ghosts on this course yet: drive a time attack, or ask someone to share a run.", Theme.Small);
			}
			foreach (var (ghost, from, shared) in rows) {
				GUILayout.BeginHorizontal();
				string sectors = ghost.Sectors.Count > 1 ? $" · sectors {string.Join(" ", ghost.Sectors.Select(s => TimeAttackRun.FormatTime(s)))}" : "";
				string name = ghost.Name.Length > 0 ? $"<b>{ghost.Name}</b>  " : "";
				GUILayout.Label($"{name}<b>{TimeAttackRun.FormatTime(ghost.Time)}</b> {ghost.Driver}  <color={Theme.MutedHex}>{ghost.Car} · {ghost.Rules} · {from} · " +
					$"{ghost.When.ToString("MMM d", CultureInfo.InvariantCulture)}{sectors}</color>", Theme.Small);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && course.Usable && hasPose_ && !InRace && !InBattle;
				if (NavButton("Race it", Theme.ButtonPrimary)) {
					RaceGhost(ghost, course);
				}
				GUI.enabled = enabled;
				if (ghost.FilePath.Length == 0 && NavButton("Save", Theme.Button)) {
					SaveGhost(ghost);
					if (shared != null) {
						roomBoard_.SharedRuns.Remove(shared);
					}
				}
				if (NavButton(sharingGhost_ == ghost ? "Done" : "Share...", Theme.Button)) {
					sharingGhost_ = sharingGhost_ == ghost ? null : ghost;
				}
				if (shared != null && NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedRuns.Remove(shared);
				}
				if (NavButton(renamingGhost_ == ghost ? "Cancel" : "Name", Theme.Button)) {
					renamingGhost_ = renamingGhost_ == ghost ? null : ghost;
					ghostName_ = ghost.Name;
				}
				if (ghost.FilePath.Length > 0 && NavButton("Delete", Theme.Button)) {
					DeleteGhost(ghost);
				}
				GUILayout.EndHorizontal();
				if (renamingGhost_ == ghost) {
					GUILayout.BeginHorizontal();
					ghostName_ = GUILayout.TextField(ghostName_ ?? "", 60, Theme.Field, GUILayout.Width(260));
					if (NavButton(ghost.FilePath.Length > 0 ? "Save name" : "Save with this name", Theme.ButtonPrimary)) {
						ghost.Name = ghostName_.Trim();
						SaveGhost(ghost);
						renamingGhost_ = null;
						if (shared != null) {
							roomBoard_.SharedRuns.Remove(shared);
						}
					}
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
				}
				if (sharingGhost_ == ghost) {
					DrawShareGhost(ghost);
				}
			}
			GUILayout.EndVertical();
		}

		/// <summary>Shares a run (its time, sectors and ghost) with the room or one player.</summary>
		private void DrawShareGhost(Ghost ghost) {
			List<RoomMessage> players = roomBoard_.Fresh(BattleNow).Where(s => s.From != MySteamId).ToList();
			if (shareGhostTo_ != 0 && !players.Any(p => p.From == shareGhostTo_)) {
				shareGhostTo_ = 0;
			}
			var who = new List<string> { "Everyone in the room" };
			who.AddRange(players.Select(p => BattleMessage.PlainName(p.Name)));
			int picked = Choice(who.ToArray(), shareGhostTo_ == 0 ? 0 : players.FindIndex(p => p.From == shareGhostTo_) + 1);
			shareGhostTo_ = picked <= 0 ? 0 : players[picked - 1].From;
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && roomSync_.IsAvailable;
			string target = shareGhostTo_ == 0 ? "the room" : who[picked];
			if (NavButton($"Send to {target}", Theme.ButtonPrimary)) {
				byte[] bytes = ghost.Encode();
				if (bytes.Length > Ghost.MaxBytes) {
					courseResult_ = $"That run is too long to share ({bytes.Length / 1024} KB, {Ghost.MaxBytes / 1024} KB at most).";
				} else {
					Course course = courses_.ByCode(ghost.CourseCode);
					var m = new RoomMessage {
						Kind = RoomKind.RunShare, From = MySteamId, Name = MyName(), To = shareGhostTo_, Ghost = ghost,
						MapKey = ghost.MapKey, MapName = map_ != null && map_.Key == ghost.MapKey ? map_.Name : course?.MapName ?? "",
					};
					int pieces = roomSync_.Send(m);
					courseResult_ = pieces > 0
						? $"Sending your {TimeAttackRun.FormatTime(ghost.Time)} on {ghost.CourseName} to {target} ({bytes.Length / 1024 + 1} KB, a few seconds). They need TRC Tune Loader 0.16 or newer."
						: "Couldn't send it: is the room's connection all right?";
					TrcLog.Info($"Shared a run ({ghost.CourseName}, {TimeAttackRun.FormatTime(ghost.Time)}, {bytes.Length} bytes) with {target}: {pieces} packet(s).");
				}
				sharingGhost_ = null;
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		/// <summary>Runs shared with you on courses that aren't on this map (the course's own list shows the rest).</summary>
		private void DrawSharedRunsElsewhere() {
			List<RoomBoard.SharedItem> elsewhere = roomBoard_.SharedRuns.Where(r => map_ == null || r.Ghost.MapKey != map_.Key).ToList();
			if (elsewhere.Count == 0) {
				return;
			}
			GUILayout.Label("Runs shared with you on other maps", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (RoomBoard.SharedItem item in elsewhere) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{TimeAttackRun.FormatTime(item.Ghost.Time)}</b> {item.Ghost.Driver} on {item.Ghost.CourseName}  <color={Theme.MutedHex}>{(item.MapName.Length > 0 ? item.MapName : item.MapKey)}</color>", Theme.Small);
				GUILayout.FlexibleSpace();
				if (NavButton("Save", Theme.Button)) {
					SaveGhost(item.Ghost);
					roomBoard_.SharedRuns.Remove(item);
				}
				if (NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedRuns.Remove(item);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}
	}
}
