// Open races in game, fed from Update: which section a race shows in, hosting, joining and leaving, and teleporting to your spot.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Open races and tournaments in game: the Races and Tournaments sections of the Battles tab (open races to join,
	/// hosting one, the lobby), the race fed from Update, the tournament the host runs, knockout battles called by
	/// it, and what's drawn over the track. This file is the part fed from Update; the tournament is TournamentRunner.cs,
	/// the pages and HUD are in src/pages/ (RacesPage, HostRacePage, RaceLobbyPage, RaceHud), the race logic is
	/// RaceSession in src/racing/, and messages go through RaceSync.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>The Battles tab (its own tab since 0.15.0) and its sections down the left.</summary>
		private const int BattlesTab = 6;
		/// <summary>0.16 (bskftw): races and tournaments are sections of their own, to keep each one cleaner.</summary>
		private static readonly string[] BattleSections = { "1v1 battles", "Races", "Tournaments", "Fun games", "My ratings" };
		private const int RacesSection = 1;
		private const int TournamentsSection = 2;
		private const int GamesSection = 3;
		private const int RatingsSection = 4;

		/// <summary>The section a race lobby shows in: a fun game, a tournament (its host sends tournament lines), or a race.</summary>
		private static int SectionFor(RaceMessage lobby) =>
			lobby.Settings.Mode != RoomMode.Race ? GamesSection : lobby.Lines.Count > 0 ? TournamentsSection : RacesSection;

		private int SectionFor(RaceSession race) =>
			race.IsGame ? GamesSection : race.Lines.Count > 0 || (race.IsHost && hostTournament_ > 0) ? TournamentsSection : RacesSection;
		private Vector2 ratingsScroll_;
		private static readonly int[] RaceLapChoices = { 1, 2, 3, 5, 10 };
		private static readonly string[] GridOrders = { "Order joined", "Random", "Best time", "Qualifying" };
		private static readonly string[] PoleSides = { "Pole on the left", "Pole on the right" };
		private static readonly string[] Layouts = { "Course's own", "Two-wide grid", "Line-up" };
		private static readonly string[] JumpRules = { "+5 s penalty", "Disqualified", "Recall the start", "No penalty" };
		private static readonly JumpRule[] JumpRuleValues = { JumpRule.Penalty, JumpRule.Disqualify, JumpRule.Recall, JumpRule.None };

		/// <summary>No limit since 0.16 (custom servers): "No limit" is the most a lobby lists.</summary>
		private static readonly int[] MaxDriverChoices = { 4, 8, 12, 16, 24, 32, 64, RaceSettings.MaxDrivers };
		private static string MaxDriversText(int max) => max >= RaceSettings.MaxDrivers ? "No limit" : max.ToString(CultureInfo.InvariantCulture);
		private static readonly string[] TournamentKinds = { "None", "Points series", "Knockout", "Heats and final", "Shootout" };
		private static readonly string[] KnockoutFormats = { "Side by side", "Cat and mouse" };
		private static readonly int[] BestOfChoices = { 1, 3 };
		private static readonly int[] HeatSizes = { 4, 5, 6, 8 };
		private static readonly int[] AdvanceChoices = { 1, 2, 3, 4 };
		private static readonly int[] QualifyingMinuteChoices = { 5, 10, 15, 20 };
		private static readonly string[] QualifyingRunChoices = { "Unlimited", "1", "2", "3" };
		private static readonly string[] RoomModeNames = { "Race", "Cops and runners", "Tag", "Infected", "King of the hill", "Keep-up", "Convoy cruise", "Highway cut-up" };
		private static readonly string[] RoomModeRules = {
			"",
			"Cops catch runners: a runner who's stopped (under 25 km/h) next to a cop for 3 s is busted and joins the cops. Runners score every second they're free; cops score per bust.",
			"One driver is it: touch someone (within a few metres) to pass it on. You score every second you're not it.",
			"One driver is infected: touching spreads it. You score every second you're clean, and the last survivors win.",
			"The hill is where the host is when the game starts (25 m round). Score every second you're on it alone; first to the target wins.",
			"Follow the host: more than the gap behind for 10 s and you're out. You score every second you keep up; the last one with them wins.",
			"A cruise behind the host: no winner, just everyone's gap to the car ahead. The host ends it.",
			"Score close, fast passes on other players (over 80 km/h, at least 10 km/h faster, within 5 m to the side), with a combo for passes in a row. A hit loses the combo.",
		};
		private static readonly int[] RoundMinuteChoices = { 3, 5, 10, 15 };
		private static readonly int[] CopChoices = { 1, 2, 3 };
		private static readonly int[] HillSecondChoices = { 30, 60, 120 };
		private static readonly int[] KeepUpChoices = { 150, 250, 400 };

		/// <summary>An open race is listed this long (seconds) after its host last said so.</summary>
		private const float OpenRaceSeconds = 8f;

		/// <summary>The "open race" pop-up shows this long after a race first appears.</summary>
		private const float RaceInviteSeconds = 25f;

		private readonly RaceSync raceSync_ = new RaceSync();
		private readonly System.Random raceIds_ = new System.Random();
		private RaceSession race_;
		private readonly Dictionary<uint, (RaceMessage Lobby, float SeenAt, float FirstSeen)> openRaces_ = new Dictionary<uint, (RaceMessage, float, float)>();
		private int battleViewMode_;
		private Vector2 raceScroll_;
		private string raceResult_ = "";
		private string raceLoggedState_ = "";
		private int raceLoggedResults_ = -1;

		// The host form.
		private string hostCourse_ = "";
		private int hostLaps_;
		private int hostOrder_;
		private int hostPole_;
		private int hostLayout_;
		private int hostJump_;
		private int hostMode_;
		private string hostRule_ = "";
		private bool hostFun_;
		private bool hostDrives_ = true;
		private bool hostLights_;
		private bool hostNoPenalties_;
		private int hostMax_ = 3;
		private bool hostElimination_;
		private int hostQualMinutes_ = 1;
		private int hostQualRuns_;
		private int hostTournament_;
		private int hostGame_;
		private int hostRoundMinutes_ = 1;
		private int hostCops_;
		private int hostHill_ = 1;
		private int hostKeepUp_ = 1;
		private bool hostTrapScoring_;
		private int koFormat_;
		private int koBestOf_ = 1;
		private int koFinalFormat_ = 1;
		private int koFinalBestOf_ = 1;
		private bool koThird_ = true;
		private int heatSize_ = 2;
		private int heatAdvance_ = 2;
		private bool roundReverse_;
		private readonly List<TournamentRound> hostRounds_ = new List<TournamentRound>();

		// The tournament this PC runs (host only).
		private Tournament tournament_;
		private int tournamentRaceCounted_ = -1;
		private bool shootoutRunning_;
		private readonly HashSet<string> matchReportsSeen_ = new HashSet<string>(StringComparer.Ordinal);

		// Knockout battles this player was called into.
		private string matchChallenged_ = "";
		private string matchReported_ = "";

		/// <summary>In a race that's lining up or running, so time attack and challenges wait.</summary>
		private bool InRace => race_ != null && !race_.Ended && (race_.IAmRacing || race_.Phase == RacePhase.Qualifying);

		private void SetupRaces() {
			raceSync_.Start(MySteamId);
			TrcLog.Info(raceSync_.Status);
		}

		/// <summary>Called from Update every frame, after the battles.</summary>
		private void UpdateRaces() {
			double now = BattleNow;
			raceSync_.Pump(now);
			foreach (RaceMessage m in raceSync_.Drain()) {
				if (race_ != null && m.RaceId == race_.Id) {
					race_.Receive(m, now);
				} else if (m.Kind == RaceKind.Lobby && m.From != MySteamId) {
					if (m.Phase == RacePhase.Closed) {
						openRaces_.Remove(m.RaceId);
					} else {
						float first = openRaces_.TryGetValue(m.RaceId, out var seen) ? seen.FirstSeen : Time.unscaledTime;
						openRaces_[m.RaceId] = (m, Time.unscaledTime, first);
					}
				}
			}
			foreach (uint id in openRaces_.Where(p => Time.unscaledTime - p.Value.SeenAt > OpenRaceSeconds).Select(p => p.Key).ToList()) {
				openRaces_.Remove(id);
			}
			if (race_ == null) {
				UpdateLightStart(now);
				return;
			}

			bool? onRoad = null;
			if (race_.Phase == RacePhase.Running && race_.Course != null && race_.Course.RoadColliders.Count > 0 && hasPose_ &&
				adapter_.TryCheckOverRoad(race_.Course.RoadColliders, out bool overRoad)) {
				onRoad = overRoad;
			}
			UpdateConsoleRacers(now);
			UpdateEventElo();
			race_.Tick(now, pose_.Position, pose_.Forward, hasPose_ ? pose_.SpeedKmh : 0f, Time.timeScale, onRoad);
			UpdateLightStart(now);
			foreach (RaceMessage m in race_.TakeOutbox()) {
				raceSync_.Send(m);
			}
			if (race_.IsHost) {
				RunTournament(now);
			}
			CallMatchBattle();
			LogRace();
		}

		// ---- Hosting and joining ----

		/// <summary>Opens a race on <paramref name="course"/>, or (null) the room game picked in the form.</summary>
		private void HostRace(Course course) {
			if (battle_ != null && battle_.Active) {
				raceResult_ = "Finish or leave your battle first.";
				return;
			}
			if (run_ != null) {
				StopTimeAttack();
			}
			if (course != null && hostTrapScoring_ && course.Traps.Count == 0) {
				// A speed trap race on a course without traps of its own: the map's free-roam traps along it.
				List<CourseGate> along = TrapsAlong(course);
				if (along.Count == 0) {
					raceResult_ = "That course has no speed traps on it: place some along it in Time attack > Speed traps, or add them in the course's Edit.";
					return;
				}
				course = course.WithTraps(along);
			}
			var settings = new RaceSettings {
				Name = $"{BattleMessage.PlainName(MyName())}'s race",
				MaxPlayers = MaxDriverChoices[Math.Min(hostMax_, MaxDriverChoices.Length - 1)],
				Laps = course != null && course.IsCircuit ? RaceLapChoices[hostLaps_] : 1,
				Open = hostMode_ != 0 || course == null,
				Regulation = course != null && hostMode_ == 1 ? RegulationByCode(hostRule_)?.Copy() : null,
				Fun = hostFun_,
				HostDrives = course == null || hostDrives_,
				LightStart = course != null && hostLights_,
				NoPenalties = course != null && hostTournament_ > 0 && hostNoPenalties_,
				Order = (GridOrder)hostOrder_,
				PoleLeft = hostPole_ == 0,
				LayoutFromCourse = hostLayout_ == 0,
				Layout = hostLayout_ == 2 ? GridLayout.LineUp : GridLayout.Grid,
				SingleFile = course != null && MeasureRoad(course).SingleFile,
				Jump = JumpRuleValues[Math.Min(hostJump_, JumpRuleValues.Length - 1)],
				Elimination = course != null && hostElimination_ && course.IsCircuit,
				QualifyingMinutes = QualifyingMinuteChoices[hostQualMinutes_],
				QualifyingRuns = hostQualRuns_,
				SpeedTrapScoring = course != null && hostTrapScoring_ && course.Traps.Count > 0,
				Mode = course == null ? (RoomMode)hostGame_ : RoomMode.Race,
				RoundMinutes = RoundMinuteChoices[hostRoundMinutes_],
				Cops = CopChoices[hostCops_],
				HillSeconds = HillSecondChoices[hostHill_],
				KeepUpMetres = KeepUpChoices[hostKeepUp_],
			};
			if (course == null) {
				settings.Name = $"{BattleMessage.PlainName(MyName())}'s {RoomModeNames[hostGame_].ToLowerInvariant()}";
			}
			if (course != null && hostMode_ == 1 && settings.Regulation == null) {
				raceResult_ = "Pick a regulation first (or make one in the Regulations tab).";
				return;
			}
			if (settings.NoPenalties && settings.Jump == JumpRule.Penalty) {
				settings.Jump = JumpRule.None;
			}
			uint id = (uint)raceIds_.Next(1, int.MaxValue);
			race_ = RaceSession.Host(id, MySteamId, MyName(), course, settings, CheckBattleCar, () => course != null ? PersonalBestOn(course) : 0, BattleNow);
			tournament_ = null;
			tournamentRaceCounted_ = -1;
			matchReportsSeen_.Clear();
			if (hostTournament_ > 0 && course != null) {
				settings.Name = $"{BattleMessage.PlainName(MyName())}'s {TournamentKinds[hostTournament_].ToLowerInvariant()}";
				race_.SetSettings(settings);
				race_.SetLines(new List<string> { $"{settings.Name}: waiting for drivers. The host starts it when everyone's in." });
			}
			raceResult_ = "";
			TrcLog.Info($"Race {id}: opened {(course != null ? $"on {course.Name} ({course.Code})" : RoomModeNames[hostGame_])}, {settings.MaxPlayers} drivers max, {settings.Laps} lap(s), " +
				$"grid {GridOrders[hostOrder_]}, jump {JumpRules[Math.Min(hostJump_, JumpRules.Length - 1)]}, {RuleText(settings.Open, settings.Regulation, settings.Fun)}" +
				$"{(settings.Regulation != null ? $" ({settings.Regulation.Code})" : "")}{(settings.HostDrives ? "" : ", host not driving")}{(hostTournament_ > 0 ? $", {TournamentKinds[hostTournament_]}" : "")}.");
		}

		private void JoinRace(RaceMessage lobby) {
			if (battle_ != null && battle_.Active && battle_.Started) {
				raceResult_ = "Finish or leave your battle first.";
				return;
			}
			if (run_ != null) {
				StopTimeAttack();
			}
			Course course = lobby.CourseCode.Length > 0 ? courses_.ByCode(lobby.CourseCode) : null;
			race_ = RaceSession.Join(lobby, MySteamId, MyName(), course, CheckBattleCar, () => race_?.Course != null ? PersonalBestOn(race_.Course) : 0, BattleNow);
			openRaces_.Remove(lobby.RaceId);
			matchChallenged_ = "";
			matchReported_ = "";
			raceResult_ = "";
			TrcLog.Info($"Race {lobby.RaceId}: joining {BattleMessage.PlainName(lobby.HostName)}'s race on {lobby.CourseCode}.");
		}

		private void LeaveRace() {
			if (race_ == null) {
				return;
			}
			race_.Leave(BattleNow);
			foreach (RaceMessage m in race_.TakeOutbox()) {
				raceSync_.Send(m);
			}
			TrcLog.Info($"Race {race_.Id}: {race_.Message}.");
			race_ = null;
			tournament_ = null;
		}

		/// <summary>This car's best time on the course from before (for the "best time" grid order), in seconds, or 0.</summary>
		private double PersonalBestOn(Course course) {
			// Asked 5 times a second in a race: worked out again only when the course, the car or the saved times change (item 6).
			if (course.Code == bestOnCode_ && pose_.CarId == bestOnCar_ && ReferenceEquals(times_, bestOnTimes_) && times_.Count == bestOnCount_) {
				return bestOn_;
			}
			bestOnCode_ = course.Code;
			bestOnCar_ = pose_.CarId;
			bestOnTimes_ = times_;
			bestOnCount_ = times_.Count;
			TimeAttackRecord best = times_.Where(r => r.CourseCode == course.Code && r.CarId == pose_.CarId && !r.IsLap && !r.Flying)
				.OrderBy(r => r.TimeMs).FirstOrDefault();
			bestOn_ = best?.Seconds ?? 0;
			return bestOn_;
		}

		private string bestOnCode_;
		private int bestOnCar_ = int.MinValue;
		private object bestOnTimes_;
		private int bestOnCount_ = -1;
		private double bestOn_;

		private void TeleportToRaceSpot() {
			GridSpot? spot = race_?.MySpot();
			if (spot == null) {
				return;
			}
			if (Time.unscaledTime < teleportReadyAt_) {
				raceResult_ = "Wait a moment before teleporting again.";
				return;
			}
			if (!adapter_.TryMoveCarTo(spot.Value.Position, spot.Value.Forward, out string error)) {
				raceResult_ = $"Couldn't teleport: {error}";
				return;
			}
			teleportReadyAt_ = Time.unscaledTime + TeleportCooldownSeconds;
			raceResult_ = "";
			CloseWindow();
		}

		/// <summary>Qualifying: back to the start line for another run.</summary>
		private void TeleportToQualifyingStart() {
			Course course = race_?.Course;
			if (course == null || Time.unscaledTime < teleportReadyAt_) {
				return;
			}
			if (adapter_.TryMoveCarTo(course.Start.Center - course.Start.Forward * StartGridMetres, course.Start.Forward, out string error)) {
				teleportReadyAt_ = Time.unscaledTime + TeleportCooldownSeconds;
				CloseWindow();
			} else {
				raceResult_ = $"Couldn't teleport: {error}";
			}
		}
	}
}
