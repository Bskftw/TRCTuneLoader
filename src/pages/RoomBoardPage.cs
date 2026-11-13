// The room board on Home: what each TRC Tune Loader player in the room is doing, and courses shared with the room (and saving one).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private bool upForBattle_;

		private void DrawRoomBoard() {
			GUILayout.Label("In this room", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (!roomSync_.IsAvailable) {
				GUILayout.Label(roomSync_.Status, Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			double now = BattleNow;
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>You</b>  <color={Theme.MutedHex}>{RoomBoard.ActivityText(MyStatus())}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			bool up = GUILayout.Toggle(upForBattle_, " I'm up for a battle");
			if (up != upForBattle_) {
				upForBattle_ = up;
			}
			GUILayout.EndHorizontal();

			List<RoomMessage> others = roomBoard_.Fresh(now).Where(s => s.From != MySteamId).ToList();
			if (others.Count == 0) {
				GUILayout.Label("Nobody else here runs TRC Tune Loader 0.14 or newer yet. Players on older versions show on the Players tab.", Theme.Small);
			}
			foreach (RoomMessage s in others) {
				var parts = new List<string> { RoomBoard.ActivityText(s) };
				if (s.Car.Length > 0) {
					parts.Add(s.Car);
				}
				string rating = RatingOf(s.From, CourseKind.Touge);
				if (rating.Length > 0) {
					parts.Add(rating);
				}
				if (map_ != null && s.MapKey.Length > 0 && s.MapKey != map_.Key) {
					parts.Add("another map");
				}
				GUILayout.BeginHorizontal();
				string up4 = s.UpForBattle ? $"  <color={Theme.GoodHex}>up for a battle</color>" : "";
				GUILayout.Label($"<b>{BattleMessage.PlainName(s.Name)}</b>{up4}  <color={Theme.MutedHex}>{string.Join(" · ", parts)}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				if (s.OpenRaceId != 0 && openRaces_.TryGetValue(s.OpenRaceId, out var open) && race_ == null) {
					if (NavButton("Join", Theme.ButtonPrimary)) {
						JoinRace(open.Lobby);
						GoToBattle(SectionFor(open.Lobby));
					}
				}
				bool canBattle = RoomBoard.Free(s) && System.Version.TryParse(s.Version, out System.Version theirs) && theirs >= BattleVersion;
				GUI.enabled = enabled && canBattle && (battle_ == null || !battle_.Active) && !InRace;
				if (NavButton("Challenge", Theme.Button)) {
					pickedOpponent_ = s.From;
					GoToBattle(0);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		/// <summary>The Battles tab at a section: 0 1v1 battles, 1 races, 2 fun games, 3 ratings.</summary>
		private void GoToBattle(int section) {
			tab_ = BattlesTab;
			OnTabOpened(BattlesTab);
			battleViewMode_ = section;
		}

		private void DrawSharedCourses() {
			if (roomBoard_.SharedCourses.Count == 0) {
				return;
			}
			GUILayout.Label("Courses shared with the room", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (RoomBoard.Shared shared in roomBoard_.SharedCourses.ToList()) {
				Course c = shared.Course;
				bool have = courses_.ByCode(c.Code) != null;
				GUILayout.BeginHorizontal();
				string map = c.MapName.Length > 0 ? c.MapName : c.MapKey;
				GUILayout.Label($"<b>{c.Name}</b>  <color={Theme.MutedHex}>from {shared.FromName} · {c.KindName} · {TimeAttackRun.Distance(c.Length)} · {map}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && !have;
				if (NavButton(have ? "Saved" : "Save", Theme.ButtonPrimary)) {
					SaveSharedCourse(shared);
				}
				GUI.enabled = enabled;
				if (NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedCourses.Remove(shared);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void SaveSharedCourse(RoomBoard.Shared shared) {
			try {
				Course copy = Copy(shared.Course, c => c.Author = $"{shared.FromName} (shared)");
				string path = CourseLibrary.SaveCustom(CoursesFolder, copy);
				courses_.LoadCustom(CoursesFolder);
				RefreshTraps();
				homeResult_ = $"Saved {copy.Name} to your courses: it's in Time attack, Tracks, when you're on {(copy.MapName.Length > 0 ? copy.MapName : "its map")}.";
				TrcLog.Info($"Saved the shared course {copy.Name} ({copy.Code}) from {shared.FromName}: {path}");
			} catch (Exception e) {
				homeResult_ = $"Couldn't save the course: {e.Message}";
				TrcLog.Error(e);
			}
		}
	}
}
