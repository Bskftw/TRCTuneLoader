// The Home page: what's waiting for you, the room board, last visit's summary, and the cards.
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
		private string homeResult_ = "";

		private Vector2 homeScroll_;

		private void DrawOtherCopies() {
			if (otherCopies_.Count == 0) {
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("TRC Tune Loader is installed twice", Theme.Heading);
			GUILayout.Label($"The one running is <b>{Location}</b>. Close CarX and delete:", Theme.Body);
			foreach (string copy in otherCopies_) {
				GUILayout.Label($"• {copy}", Theme.Body);
			}
			GUILayout.Label("An old version's update put the mod in a second place. Your times, courses and backups are safe: they're in kino\\mods\\TRCTuneLoader.", Theme.Small);
			GUILayout.EndVertical();
		}

		/// <summary>Someone in the room runs a newer version: this one didn't update (KSL can treat an update as optional).</summary>
		private void DrawNewerVersion() {
			if (newerVersion_.Length == 0 || !RoomBoard.IsNewer(newerVersion_, Version)) {
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"TRC Tune Loader {newerVersion_} is out (you have {Version})", Theme.Heading);
			GUILayout.Label("Someone in this room already has it. Turn on automatic updates for TRC Tune Loader in KSL, then restart CarX: KSL installs it as the game starts. " +
				"Battles, races and the room board work best when everyone's on the same version.", Theme.Body);
			GUILayout.EndVertical();
		}

		private void DrawHomeTab() {
			RefreshCarIfDue();
			RefreshPlayersIfDue();
			// Up and down only (bskftw, Nov 3: no sideways scroll bar on Home).
			homeScroll_ = GUILayout.BeginScrollView(homeScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			DrawOtherCopies();
			DrawNewerVersion();
			if (whatsNewPending_) {
				GUILayout.BeginVertical(Theme.Guide);
				GUILayout.Label($"What's new in {WhatsNewVersion}", Theme.Heading);
				foreach (string line in WhatsNewLines) {
					GUILayout.Label($"• {line}", Theme.Body);
				}
				GUILayout.BeginHorizontal();
				if (NavButton("Got it", Theme.ButtonPrimary)) {
					DismissWhatsNew();
				}
				if (NavButton("Take the new tour", Theme.Button)) {
					DismissWhatsNew();
					StartTour();
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			if (homeResult_.Length > 0) {
				GUILayout.Label(homeResult_, Theme.Body);
			}
			DrawHomeSearch();

			// A card for each part of the mod, in its colour (bskftw, Nov 3: the "hub tiles" picture).
			DrawHomeCards();
			GUILayout.Space(6);

			// What the last visit to a map added up to (IMPROVEMENT_PLAN.md F5), until it's hidden or the next one ends.
			if (lastSession_ != null) {
				GUILayout.Label("Your last session", Theme.SectionLabel);
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b>{lastSession_[0]}</b>", Theme.Body);
				for (int i = 1; i < lastSession_.Count; i++) {
					GUILayout.Label($"• {lastSession_[i]}", Theme.Small);
				}
				GUILayout.BeginHorizontal();
				bool hide = NavButton("Hide", Theme.Chip);
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
				GUILayout.Space(6);
				if (hide) {
					lastSession_ = null;
				}
			}

			// What's waiting: challenges and open races.
			List<RaceMessage> races = race_ == null ? openRaces_.Values.Select(v => v.Lobby).Where(l => l.Phase == RacePhase.Lobby || l.Phase == RacePhase.Finished).ToList() : new List<RaceMessage>();
			if (invites_.Count > 0 || races.Count > 0) {
				GUILayout.Label("Waiting for you", Theme.SectionLabel);
				GUILayout.BeginVertical(Theme.Guide);
				foreach (var (challenge, _) in invites_.ToList()) {
					GUILayout.BeginHorizontal();
					GUILayout.Label($"<b>{BattleMessage.PlainName(challenge.FromName)} challenges you</b>  <color={Theme.MutedHex}>{BattleFormats[(int)challenge.Format]}</color>", Theme.Body);
					GUILayout.FlexibleSpace();
					if (NavButton("Answer", Theme.ButtonPrimary)) {
						GoToBattle(0);
					}
					GUILayout.EndHorizontal();
				}
				foreach (RaceMessage lobby in races) {
					Course course = lobby.CourseCode.Length > 0 ? courses_.ByCode(lobby.CourseCode) : null;
					string where = lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode] : course?.Name ?? lobby.CourseCode;
					GUILayout.BeginHorizontal();
					GUILayout.Label($"<b>{BattleMessage.PlainName(lobby.HostName)}'s {(lobby.Settings.Mode != RoomMode.Race ? "game" : "race")}</b>  <color={Theme.MutedHex}>{where} · {lobby.Entries.Count}/{lobby.Settings.MaxPlayers}</color>", Theme.Body);
					GUILayout.FlexibleSpace();
					bool enabled = GUI.enabled;
					GUI.enabled = enabled && lobby.Entries.Count < lobby.Settings.MaxPlayers;
					if (NavButton("Join", Theme.ButtonPrimary)) {
						JoinRace(lobby);
						GoToBattle(SectionFor(lobby));
					}
					GUI.enabled = enabled;
					GUILayout.EndHorizontal();
				}
				GUILayout.EndVertical();
			}

			DrawRoomBoard();
			DrawSharedCourses();
			GUILayout.EndScrollView();
			DividerLine();
			GUILayout.Space(4);
			DrawToolsRow();
		}
	}
}
