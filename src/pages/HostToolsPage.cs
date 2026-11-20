// Host tools (Tools > Host tools, docs/archive/IMPROVEMENT_PLAN.md F13): one page for whoever runs a session, and the start-line invite.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// For whoever runs a session: everyone in the room at a glance (their version and what they'd need to update for, TRC
	/// spec or not, signed or an older copy, ready in your race lobby), and the session's buttons in one place: open a
	/// race, the regulations, traffic presets and traffic on or off. "Come to the start" sends an invite each player
	/// accepts or not (a room message, RoomKind.Invite): it never moves anyone's car without them pressing Go.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int HostToolsTab = 17;
		private Vector2 hostScroll_;
		private string hostResult_ = "";

		/// <summary>The last start-line invite to this player, and when it came (unscaled time); null when none.</summary>
		private RoomMessage hostInvite_;
		private float hostInviteAt_;
		private const float InviteKeepSeconds = 120f;

		private void DrawHostToolsTab() {
			hostScroll_ = GUILayout.BeginScrollView(hostScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			GUILayout.Label("For whoever runs the session: who's here and ready, and the session's buttons in one place.", Theme.PageLead);
			DrawHostPlayers();
			DrawHostButtons();
			DrawHostInvites();
			if (hostResult_.Length > 0) {
				GUILayout.Label(hostResult_, Theme.Body);
			}
			GUILayout.EndScrollView();
		}

		private void DrawHostPlayers() {
			GUILayout.Label("Players", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (!roomSync_.IsAvailable) {
				GUILayout.Label(roomSync_.Status, Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			List<RoomMessage> others = roomBoard_.Fresh(BattleNow).Where(s => s.From != MySteamId).ToList();
			if (others.Count == 0) {
				GUILayout.Label("Nobody else here runs TRC Tune Loader yet. Players without it show on the Players page.", Theme.Small);
			}
			foreach (RoomMessage s in others) {
				var parts = new List<string> { $"{s.Version}" };
				List<string> missing = RoomBoard.Missing(MyFeatures, s.Features);
				if (missing.Count > 0) {
					parts.Add($"<color={Theme.WarnHex}>{(RoomBoard.IsNewer(s.Version, Version) ? "you" : "they")} need to update for {string.Join(", ", missing)}</color>");
				}
				TrcSync.Report report = sync_?.ReportFrom(s.From);
				parts.Add(report == null ? $"<color={Theme.MutedHex}>no check yet</color>"
					: report.SetupOfficial && report.AllVerified ? $"<color={Theme.GoodHex}>TRC spec</color>"
					: $"<color={Theme.WarnHex}>not TRC spec</color>");
				if (report != null && !report.Signed) {
					parts.Add($"<color={Theme.MutedHex}>older version (not signed)</color>");
				}
				RaceDriver inRace = race_ != null && !race_.Ended ? race_.Drivers.FirstOrDefault(d => d.Id == s.From) : null;
				if (inRace != null) {
					parts.Add(inRace.Ready ? $"<color={Theme.GoodHex}>ready</color>" : $"<color={Theme.WarnHex}>not ready</color>");
				}
				GUILayout.Label($"<b>{BattleMessage.PlainName(s.Name)}</b>  <color={Theme.MutedHex}>{RoomBoard.ActivityText(s)}</color>  {string.Join(" · ", parts)}", Theme.Body);
			}
			GUILayout.EndVertical();
		}

		private void DrawHostButtons() {
			GUILayout.Space(6);
			GUILayout.Label("The session", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.BeginHorizontal();
			if (NavButton(race_ != null && !race_.Ended ? "Your race lobby" : "Open a race", Theme.ButtonPrimary)) {
				GoToBattle(RacesSection);
			}
			if (NavButton("Regulations", Theme.Button)) {
				tab_ = RegulationsTab;
				OnTabOpened(RegulationsTab);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(4);
			GUILayout.Label("Traffic: pick a preset, then switch it on (it's shared with the room; others press Join in Free roam > Traffic lab).", Theme.Small);
			GUILayout.BeginHorizontal();
			foreach (HudPresetText.Preset ready in ReadyTrafficPresets) {
				if (NavButton(ready.Name, Theme.Button)) {
					ApplyTrafficState(ready.Values);
					hostResult_ = $"Traffic preset: {ready.Name}.";
				}
			}
			GUILayout.FlexibleSpace();
			if (NavButton(TrafficRunning ? "Traffic off" : "Traffic on", Theme.ButtonPrimary)) {
				QuickTrafficSwitch();
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void DrawHostInvites() {
			GUILayout.Space(6);
			GUILayout.Label("Come to the start", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Asks everyone in the room to go to a track's start. Each player gets a Go button: nobody's car moves unless they press it.", Theme.Small);
			List<Course> here = map_ != null ? CoursesHere() : new List<Course>();
			if (here.Count == 0) {
				GUILayout.Label("No tracks on this map.", Theme.Small);
			}
			foreach (Course course in here) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{course.Name}</b>  <color={Theme.MutedHex}>{TimeAttackRun.Distance(course.Length)}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (NavButton("Invite everyone", Theme.Button)) {
					SendStartInvite(course);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void SendStartInvite(Course course) {
			if (!roomSync_.IsAvailable) {
				hostResult_ = roomSync_.Status;
				return;
			}
			var m = new RoomMessage {
				Kind = RoomKind.Invite, From = MySteamId, Name = MyName(), To = 0, MapKey = course.MapKey, MapName = map_?.Name ?? "", Label = course.Code, Detail = course.Name,
			};
			int pieces = roomSync_.Send(m);
			hostResult_ = pieces > 0 ? $"Asked everyone to come to the start of {course.Name}." : "Couldn't send it: is the room's connection all right?";
			TrcLog.Info($"Invited the room to the start of {course.Name} ({course.Code}).");
		}

		/// <summary>From UpdateHome: an invite to a start for this player.</summary>
		private void OnStartInvite(RoomMessage m) {
			if (m.From == MySteamId || (m.To != 0 && m.To != MySteamId)) {
				return;
			}
			hostInvite_ = m;
			hostInviteAt_ = Time.unscaledTime;
			ShowNotice($"<b>{BattleMessage.PlainName(m.Name)} asks you to the start of {m.Detail}</b>\n<color={Theme.MutedHex}>open the window to go</color>", 6f);
			TrcLog.Info($"{BattleMessage.PlainName(m.Name)} invited the room to the start of {m.Detail} ({m.Label}).");
		}

		/// <summary>At the top of the window: the invite, with Go (to the start, as Start track does) or No thanks.</summary>
		private void DrawStartInviteBanner() {
			if (hostInvite_ == null || Time.unscaledTime - hostInviteAt_ > InviteKeepSeconds || tourStep_ >= 0) {
				return;
			}
			RoomMessage m = hostInvite_;
			Course course = courses_.ByCode(m.Label);
			bool here = course != null && map_ != null && course.MapKey == map_.Key;
			GUILayout.BeginHorizontal(Theme.Guide);
			GUILayout.Label($"<b>{BattleMessage.PlainName(m.Name)} asks you to the start of {m.Detail}</b>" +
				(here ? "" : $"  <color={Theme.MutedHex}>{(course == null ? "you don't have this track" : $"it's on {m.MapName}")}</color>"), Theme.Body);
			GUILayout.FlexibleSpace();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && here && !InBattle && !InRace;
			if (NavButton("Go", Theme.ButtonPrimary)) {
				hostInvite_ = null;
				StartTimeAttack(course);
			}
			GUI.enabled = enabled;
			if (NavButton("No thanks", Theme.ButtonQuiet)) {
				hostInvite_ = null;
			}
			GUILayout.EndHorizontal();
		}
	}
}
