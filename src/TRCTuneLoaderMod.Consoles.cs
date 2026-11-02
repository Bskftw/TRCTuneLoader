using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Console players in races (0.16, bskftw: "we can manually add console players to a race, tourny or 1v1"). They
	/// can't run the mod, so the PC host adds them, its mod times them from where CarX shows their car (RaceSession
	/// does the timing), checks their car from what CarX shares, and shows them the start on the host's headlights.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private readonly List<RoomCarPose> roomPoses_ = new List<RoomCarPose>();
		private float nextConsoleCheck_;
		private bool lightStartOn_;
		private bool lightsBefore_;
		private float lastLightSwitch_;
		private bool lightFailureLogged_;

		/// <summary>Host, every frame before the race ticks: where each console player's car is, and (every few seconds) their car checked.</summary>
		private void UpdateConsoleRacers(double now) {
			if (race_ == null || race_.Ended || !race_.IsHost || !race_.Drivers.Any(d => d.Console)) {
				return;
			}
			if (adapter_.TryGetRoomCarPoses(roomPoses_)) {
				foreach (RoomCarPose pose in roomPoses_.Where(p => p.IsConsole)) {
					race_.SetConsolePose(RaceSession.ConsoleId(pose.Key), pose.Pose.Position, pose.Pose.Forward, pose.Pose.SpeedKmh, now);
				}
			}
			if (Time.unscaledTime < nextConsoleCheck_) {
				return;
			}
			nextConsoleCheck_ = Time.unscaledTime + 3f;
			if (!adapter_.TryGetRoomPlayers(out List<RoomPlayer> players, out _)) {
				return;
			}
			foreach (RoomPlayer player in players.Where(p => p.IsConsole)) {
				ulong id = RaceSession.ConsoleId(player.Key);
				if (race_.Drivers.Any(d => d.Id == id)) {
					race_.SetConsoleCar(id, ConsoleCar(player));
				}
			}
		}

		/// <summary>A console player's car as a race sees it: TRC spec (the same check as the Players page), and the race's regulation.</summary>
		private BattleCar ConsoleCar(RoomPlayer player) {
			PlayerRow row = CheckPlayer(player);
			bool spec = row.Setup != null && row.Setup.Matches && player.EngineId == 0 && !CarNotSpec(player) && !UnknownWorkshop(player);
			Regulation rule = race_?.Settings.Regulation;
			return new BattleCar {
				Spec = spec,
				TuneSet = spec ? row.Official.Name : "",
				Car = player.CarName,
				RuleOk = rule != null && rule.Check(FactsFor(player)).Passes,
			};
		}

		/// <summary>The host's list of console players in the room who aren't in the race yet, to add.</summary>
		private void DrawConsolePlayersToAdd(RaceSession r) {
			RefreshPlayersIfDue();
			List<PlayerRow> consoles = players_.Where(p => p.Player.IsConsole && !r.Drivers.Any(d => d.Id == RaceSession.ConsoleId(p.Player.Key))).ToList();
			GUILayout.Label("Console players", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Players on PlayStation, Xbox or Switch can't run the mod: add them and your mod times them from where their car is. " +
				"They see the start on your headlights (on, off at 3, 2 and 1, off at GO), so they need CarX's synced headlights on (the default). " +
				"Stay in the room until the race is over.", Theme.Small);
			if (consoles.Count == 0) {
				GUILayout.Label("No console players in the room to add.", Theme.Small);
			}
			foreach (PlayerRow c in consoles) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{c.Player.Name}</b>  <color={Theme.MutedHex}>{c.Player.Platform} · {c.Player.CarName} · {c.SetupText}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && r.Drivers.Count < r.Settings.MaxPlayers;
				if (GUILayout.Button("Add", Theme.ButtonPrimary)) {
					AddConsoleRacer(r, c.Player);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void AddConsoleRacer(RaceSession r, RoomPlayer player) {
			ulong id = RaceSession.ConsoleId(player.Key);
			if (r.AddConsole(id, player.Name, player.Platform, BattleNow)) {
				r.SetConsoleCar(id, ConsoleCar(player));
				nextConsoleCheck_ = 0;
				TrcLog.Info($"Race {r.Id}: added the console player {player.Name} ({player.Platform}, {player.Key}).");
			}
		}

		/// <summary>A 1v1 with a console player: a two-car race this PC runs and times, on the rules picked in the 1v1 form.</summary>
		private void RaceConsolePlayer(PlayerRow console, Course course) {
			int tournament = hostTournament_;
			int mode = hostMode_;
			string rule = hostRule_;
			bool fun = hostFun_;
			bool drives = hostDrives_;
			hostTournament_ = 0;
			hostMode_ = pickedMode_;
			hostRule_ = pickedRule_;
			hostFun_ = pickedFun_;
			hostDrives_ = true;
			HostRace(course);
			hostTournament_ = tournament;
			hostMode_ = mode;
			hostRule_ = rule;
			hostFun_ = fun;
			hostDrives_ = drives;
			if (race_ == null || !race_.IsHost) {
				battleResult_ = raceResult_;
				return;
			}
			RaceSettings s = race_.Settings.Copy();
			s.Name = $"{BattleMessage.PlainName(MyName())} vs {console.Player.Name}";
			s.MaxPlayers = 2;
			race_.SetSettings(s);
			AddConsoleRacer(race_, console.Player);
			battleViewMode_ = RacesSection;
			raceResult_ = $"Race against {console.Player.Name} open: press Start race, line up, and your headlights show them the start.";
		}

		/// <summary>
		/// The host's headlights count the start down for console players (or always, when the host asked): every frame
		/// of a countdown, switched like the light key would, then put back as they were a moment after GO.
		/// </summary>
		private void UpdateLightStart(double now) {
			bool? want = null;
			if (race_ != null && !race_.Ended && race_.NeedsLightStart && race_.GoAt > 0 && hasPose_) {
				want = LightStart.LightsAt(race_.GoAt - now);
			}
			if (want == null) {
				if (lightStartOn_) {
					lightStartOn_ = false;
					adapter_.TrySetHeadlights(lightsBefore_, out _);
				}
				return;
			}
			if (!lightStartOn_) {
				lightStartOn_ = true;
				lightsBefore_ = adapter_.HeadlightsOn;
			}
			if (adapter_.HeadlightsOn != want.Value && Time.unscaledTime - lastLightSwitch_ >= 0.05f) {
				lastLightSwitch_ = Time.unscaledTime;
				if (!adapter_.TrySetHeadlights(want.Value, out string error) && !lightFailureLogged_) {
					lightFailureLogged_ = true;
					TrcLog.Info($"Headlight start: couldn't switch the lights: {error}");
				}
			}
		}

		/// <summary>The Elo each driver sends in a race or battle: their TRC spec Elo, or the regulation's.</summary>
		private void UpdateEventElo() {
			if (ratings_ == null) {
				return;
			}
			if (race_ != null && !race_.Ended && race_.Course != null) {
				race_.MyElo = (float)ratings_.EloFor(race_.Settings.Regulation?.Code ?? "", race_.Course.Kind);
			}
			if (battle_ != null && battle_.Active && battle_.Course != null) {
				CourseKind kind = battle_.IsHighway ? CourseKind.Highway : battle_.Course.Kind;
				battle_.MyElo = (float)ratings_.EloFor(battle_.Regulation?.Code ?? "", kind);
			}
		}
	}
}
