using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Open races and tournaments in game: the "Races &amp; tournaments" part of the Battle view (open races to join,
	/// hosting one, the lobby), the race fed from Update, the tournament the host runs, knockout battles called by
	/// it, and what's drawn over the track. The race and tournament logic is in src/Races/; messages go through RaceSync.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private static readonly string[] BattleModesView = { "1v1 battles", "Races & tournaments" };
		private static readonly int[] RaceLapChoices = { 1, 2, 3, 5, 10 };
		private static readonly string[] GridOrders = { "Order joined", "Random", "Best time", "Qualifying" };
		private static readonly string[] PoleSides = { "Pole on the left", "Pole on the right" };
		private static readonly string[] Layouts = { "Course's own", "Two-wide grid", "Line-up" };
		private static readonly string[] JumpRules = { "+5 s penalty", "Disqualified" };
		private static readonly int[] MaxDriverChoices = { 4, 8, 12, 16 };
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
				return;
			}

			bool? onRoad = null;
			if (race_.Phase == RacePhase.Running && race_.Course != null && race_.Course.RoadColliders.Count > 0 && hasPose_ &&
				adapter_.TryCheckOverRoad(race_.Course.RoadColliders, out bool overRoad)) {
				onRoad = overRoad;
			}
			race_.Tick(now, pose_.Position, pose_.Forward, hasPose_ ? pose_.SpeedKmh : 0f, Time.timeScale, onRoad);
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
			var settings = new RaceSettings {
				Name = $"{BattleMessage.PlainName(MyName())}'s race",
				MaxPlayers = MaxDriverChoices[hostMax_],
				Laps = course != null && course.IsCircuit ? RaceLapChoices[hostLaps_] : 1,
				Open = hostMode_ == 1,
				Order = (GridOrder)hostOrder_,
				PoleLeft = hostPole_ == 0,
				LayoutFromCourse = hostLayout_ == 0,
				Layout = hostLayout_ == 2 ? GridLayout.LineUp : GridLayout.Grid,
				Jump = hostJump_ == 1 ? JumpRule.Disqualify : JumpRule.Penalty,
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
				$"grid {GridOrders[hostOrder_]}, jump {JumpRules[hostJump_]}, {(settings.Open ? "Open" : "TRC spec")}{(hostTournament_ > 0 ? $", {TournamentKinds[hostTournament_]}" : "")}.");
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
			TimeAttackRecord best = times_.Where(r => r.CourseCode == course.Code && r.CarId == pose_.CarId && !r.IsLap && !r.Flying)
				.OrderBy(r => r.TimeMs).FirstOrDefault();
			return best?.Seconds ?? 0;
		}

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

		// ---- The tournament (host) ----

		/// <summary>Host, every frame: counts finished races and knockout reports into the tournament, and keeps its lines up to date.</summary>
		private void RunTournament(double now) {
			if (tournament_ == null) {
				return;
			}
			bool changed = false;
			if (race_.Phase == RacePhase.Finished && race_.Results != null && race_.ResultsFor == race_.RaceNumber && tournamentRaceCounted_ != race_.RaceNumber) {
				tournamentRaceCounted_ = race_.RaceNumber;
				if (tournament_.Settings.Kind == TournamentKind.Points) {
					tournament_.AddRaceResult(race_.Results);
				} else if (tournament_.Settings.Kind == TournamentKind.Heats) {
					tournament_.AddHeatResult(race_.Results);
				}
				changed = true;
			}
			if (shootoutRunning_ && race_.Phase == RacePhase.Lobby) {
				shootoutRunning_ = false;
				tournament_.SetShootout(race_.Drivers.Select(d => (d.Id, d.Name, d.BestQualifying)));
				changed = true;
			}
			foreach (RaceMessage report in race_.Reports.ToList()) {
				race_.Reports.Remove(report);
				string key = $"{report.MatchId}:{report.MatchGame}";
				if (report.MatchId != race_.MatchId || report.MatchGame != race_.MatchGame || !matchReportsSeen_.Add(key)) {
					continue;
				}
				tournament_.Report(report.MatchId, report.Winner);
				KnockoutMatch m = tournament_.Matches.FirstOrDefault(x => x.Id == report.MatchId);
				if (m != null && m.Winner == 0) {
					// Not decided yet: the next game of the match.
					race_.CallMatch(m.Id, m.A, m.B, m.Format, race_.MatchGame + 1, tournament_.Settings.Laps);
				} else {
					race_.CallMatch(0, 0, 0, BattleFormat.Race, 0, 1);
				}
				changed = true;
			}
			if (changed) {
				race_.SetLines(tournament_.Lines());
				foreach (string line in tournament_.Lines().Take(4)) {
					TrcLog.Info($"Tournament: {line}");
				}
			}
		}

		/// <summary>Host: starts the tournament with everyone in the lobby, seeded by the race's grid order (qualifying, best time...).</summary>
		private void StartTournament() {
			var settings = new TournamentSettings {
				Kind = (TournamentKind)(hostTournament_ - 1),
				Name = race_.Settings.Name,
				Rounds = hostRounds_.Select(r => new TournamentRound { CourseCode = r.CourseCode, CourseName = r.CourseName, Laps = r.Laps, ReverseGrid = r.ReverseGrid }).ToList(),
				Format = (BattleFormat)koFormat_,
				BestOf = BestOfChoices[koBestOf_],
				FinalFormat = (BattleFormat)koFinalFormat_,
				FinalBestOf = BestOfChoices[koFinalBestOf_],
				ThirdPlace = koThird_,
				Laps = race_.Settings.Laps,
				HeatSize = HeatSizes[heatSize_],
				Advance = AdvanceChoices[heatAdvance_],
			};
			if (settings.Kind == TournamentKind.Points && settings.Rounds.Count == 0) {
				settings.Rounds.Add(new TournamentRound { CourseCode = race_.CourseCode, CourseName = race_.Course?.Name ?? "", Laps = race_.Settings.Laps });
			}
			List<RaceDriver> seeded = race_.Settings.Order == GridOrder.Qualifying
				? race_.Drivers.OrderBy(d => d.BestQualifying > 0 ? d.BestQualifying : double.MaxValue).ToList()
				: race_.Settings.Order == GridOrder.BestTime
				? race_.Drivers.OrderBy(d => d.PersonalBest > 0 ? d.PersonalBest : double.MaxValue).ToList()
				: race_.Settings.Order == GridOrder.Random ? race_.Drivers.OrderBy(_ => raceIds_.Next()).ToList()
				: race_.Drivers.ToList();
			tournament_ = new Tournament(settings, seeded.Select(d => (d.Id, d.Name)).ToList());
			tournamentRaceCounted_ = race_.RaceNumber;
			race_.SetLines(tournament_.Lines());
			TrcLog.Info($"Tournament started: {TournamentKinds[hostTournament_]}, {seeded.Count} drivers.");
		}

		/// <summary>Host: the tournament's next step (a points round, a heat or the final, the next battle, the shootout).</summary>
		private void NextTournamentStep() {
			double now = BattleNow;
			switch (tournament_.Settings.Kind) {
				case TournamentKind.Points: {
					TournamentRound round = tournament_.CurrentRound;
					Course course = round != null ? courses_.ByCode(round.CourseCode) ?? courses_.ForMap(map_?.Key ?? "").FirstOrDefault(c => c.Code == round.CourseCode) : null;
					if (course == null) {
						raceResult_ = "This round's course isn't on this map.";
						return;
					}
					race_.SetCourse(course);
					RaceSettings s = race_.Settings.Copy();
					s.Laps = course.IsCircuit ? round.Laps : 1;
					race_.SetSettings(s);
					race_.StartRace(now, null, tournament_.NextGrid());
					break;
				}
				case TournamentKind.Heats:
					race_.StartRace(now, tournament_.NextHeatRacers(), tournament_.NextHeatRacers());
					break;
				case TournamentKind.Knockout: {
					KnockoutMatch m = tournament_.NextMatch();
					if (m == null) {
						raceResult_ = "No battle to call right now.";
						return;
					}
					race_.CallMatch(m.Id, m.A, m.B, m.Format, m.Games + 1, tournament_.Settings.Laps);
					break;
				}
				case TournamentKind.Shootout:
					shootoutRunning_ = true;
					race_.StartQualifying(now);
					break;
			}
			race_.SetLines(tournament_.Lines());
		}

		// ---- Knockout battles ----

		/// <summary>
		/// A knockout battle the host called with this player in it: the higher seed's mod challenges by itself (the
		/// other one accepts by itself, in RouteChallenge), and when it's over the winner goes back to the host.
		/// </summary>
		private void CallMatchBattle() {
			if (race_.MatchId == 0 || race_.Course == null) {
				return;
			}
			string key = $"{race_.Id}:{race_.MatchId}:{race_.MatchGame}";
			if (race_.MatchA == MySteamId && matchChallenged_ != key && (battle_ == null || !battle_.Active) && hasPose_) {
				matchChallenged_ = key;
				RaceDriver other = race_.Drivers.FirstOrDefault(d => d.Id == race_.MatchB);
				StartBattle(race_.MatchB, other?.Name ?? "", race_.Course, race_.MatchFormat, race_.Settings.Open, race_.MatchLaps, race_.MatchId, race_.MatchGame);
			}
		}

		/// <summary>A tournament battle ended: who won goes to the host (both drivers send it; the host counts it once).</summary>
		private void ReportMatchBattle(BattleSession b) {
			if (race_ == null || b.MatchId == 0) {
				return;
			}
			string key = $"{b.MatchId}:{b.MatchGame}";
			if (matchReported_ == key) {
				return;
			}
			matchReported_ = key;
			ulong winner = b.Winner != 0 ? b.Winner : b.PeerLeft ? b.Me : b.ILeft ? b.Peer : 0;
			var report = new RaceMessage { Kind = RaceKind.MatchReport, RaceId = race_.Id, From = MySteamId, RaceNumber = race_.RaceNumber, MatchId = b.MatchId, MatchGame = b.MatchGame, Winner = winner };
			if (race_.IsHost) {
				race_.Reports.Add(report);
			} else {
				raceSync_.Send(report);
			}
			TrcLog.Info($"Race {race_.Id}: tournament battle {b.MatchId} game {b.MatchGame}, {(winner == 0 ? "no winner (run again)" : winner == b.Me ? "you won" : $"{b.PeerName} won")}.");
		}

		/// <summary>True when this challenge is the knockout battle the host called, from the right driver: it's accepted by itself.</summary>
		private bool IsCalledMatch(BattleMessage challenge) =>
			race_ != null && challenge.MatchId != 0 && challenge.MatchId == race_.MatchId && challenge.MatchGame == race_.MatchGame &&
			challenge.From == race_.MatchA && race_.MatchB == MySteamId;

		// ---- The log ----

		private void LogRace() {
			string state = $"{race_.Id} {race_.Phase} {race_.RaceNumber}";
			if (state != raceLoggedState_) {
				raceLoggedState_ = state;
				TrcLog.Info($"Race {race_.Id}: {race_.Phase}, race {race_.RaceNumber}, {race_.Drivers.Count} driver(s). {race_.Message}");
			}
			if (race_.Results != null && race_.ResultsFor != raceLoggedResults_) {
				raceLoggedResults_ = race_.ResultsFor;
				foreach (RaceResultRow r in race_.Results) {
					TrcLog.Info($"  P{r.Position} {r.Name}: {RowText(r)}");
				}
				SaveRaceResults();
				RateRace();
			}
			if (race_.Ended) {
				TrcLog.Info($"Race {race_.Id}: {race_.Message}.");
				raceResult_ = race_.Message;
				race_ = null;
			}
		}

		private string RowText(RaceResultRow r) =>
			race_ != null && race_.IsGame ? $"{r.Points} pts{(r.State == RacerState.Out ? $" ({r.Reason})" : "")}"
			: r.State == RacerState.Finished && race_ != null && race_.Settings.SpeedTrapScoring ? $"{r.TrapTotal:0} km/h at the traps · {TimeAttackRun.FormatTime(r.Time)}"
			: r.State == RacerState.Finished ? $"{TimeAttackRun.FormatTime(r.Time)}{(r.Penalty > 0 ? $" (+{r.Penalty:0} s)" : "")}"
			: r.State == RacerState.Disqualified ? $"disqualified ({r.Reason})"
			: r.State == RacerState.Eliminated ? $"eliminated{(r.Reason.Length > 0 ? $" ({r.Reason})" : "")}"
			: $"didn't finish{(r.Reason.Length > 0 ? $" ({r.Reason})" : "")}";

		/// <summary>Every race result this player was in, one line each in TimeAttack/races.csv.</summary>
		private void SaveRaceResults() {
			RaceResultRow mine = race_.Results.FirstOrDefault(r => r.Id == MySteamId);
			if (mine == null) {
				return;
			}
			try {
				Directory.CreateDirectory(TimesFolder);
				string path = Path.Combine(TimesFolder, "races.csv");
				var text = new StringBuilder();
				if (!File.Exists(path)) {
					text.Append("date,race,host,course_code,course,laps,mode,position,drivers,state,time_ms,penalty_s,winner,loader_version\n");
				}
				string Q(string f) => f.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + f.Replace("\"", "\"\"") + "\"" : f;
				text.Append(string.Join(",", new[] {
					DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Q(race_.Settings.Name), Q(race_.HostName), race_.CourseCode,
					Q(race_.Course?.Name ?? ""), race_.Settings.Laps.ToString(CultureInfo.InvariantCulture), race_.Settings.Open ? "open" : "spec",
					mine.Position.ToString(CultureInfo.InvariantCulture), race_.Results.Count.ToString(CultureInfo.InvariantCulture), mine.State.ToString().ToLowerInvariant(),
					mine.State == RacerState.Finished ? ((long)Math.Round(mine.Time * 1000)).ToString(CultureInfo.InvariantCulture) : "",
					mine.Penalty.ToString("0.###", CultureInfo.InvariantCulture), Q(race_.Results[0].Name), Version,
				})).Append('\n');
				File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			LedgerEvent(race_.IsGame ? "game" : "race", EventLedger.RaceFields(race_, mine));
		}

		// ---- The window ----

		/// <summary>The Battle view: 1v1 battles, or races and tournaments.</summary>
		private void DrawBattleOrRaces() {
			battleViewMode_ = Choice(BattleModesView, battleViewMode_);
			if (battleViewMode_ == 0) {
				DrawBattleView();
			} else {
				DrawRacesView();
			}
		}

		private void DrawRacesView() {
			if (!raceSync_.IsAvailable) {
				GUILayout.Label(raceSync_.Status, Theme.PillNeutral);
				return;
			}
			raceScroll_ = GUILayout.BeginScrollView(raceScroll_);
			if (!string.IsNullOrEmpty(raceResult_)) {
				GUILayout.Label(raceResult_, Theme.Body);
			}
			if (race_ != null) {
				DrawRaceLobby();
			} else {
				DrawOpenRaces();
				DrawHostRaceForm();
			}
			GUILayout.EndScrollView();
		}

		private void DrawOpenRaces() {
			GUILayout.Label("Open races in this room", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (openRaces_.Count == 0) {
				GUILayout.Label("No open races right now. Host one below, and everyone in the room with TRC Tune Loader sees it.", Theme.Small);
			}
			foreach (var (lobby, _, _) in openRaces_.Values.ToList()) {
				Course course = courses_.ByCode(lobby.CourseCode);
				string where = lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode] : course?.Name ?? lobby.CourseCode;
				bool full = lobby.Entries.Count >= lobby.Settings.MaxPlayers;
				bool running = lobby.Phase != RacePhase.Lobby && lobby.Phase != RacePhase.Qualifying && lobby.Phase != RacePhase.Finished;
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{BattleMessage.PlainName(lobby.HostName)}</b>: {where}  <color={Theme.MutedHex}>{lobby.Entries.Count}/{lobby.Settings.MaxPlayers} · " +
					$"{(lobby.Settings.Laps > 1 ? $"{lobby.Settings.Laps} laps · " : "")}{(lobby.Settings.Open ? "Open" : "TRC spec")}{(lobby.Lines.Count > 0 ? " · tournament" : "")}{(running ? " · racing now" : "")}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && !full && hasPose_;
				if (GUILayout.Button(full ? "Full" : running ? "Join (next race)" : "Join", Theme.ButtonPrimary, GUILayout.Width(130))) {
					JoinRace(lobby);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void DrawHostRaceForm() {
			GUILayout.Label("Host a race", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (!hasPose_ || map_ == null) {
				GUILayout.Label("Join a room and drive onto the map first.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			GUILayout.Label("What to play", Theme.SectionLabel);
			hostGame_ = Choice(RoomModeNames, hostGame_);
			if (hostGame_ != 0) {
				DrawGameForm();
				GUILayout.EndVertical();
				return;
			}
			List<Course> courses = courses_.ForMap(map_.Key).Where(c => c.Usable).ToList();
			if (courses.Count == 0) {
				GUILayout.Label("No courses on this map yet: make one with the Course builder.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			GUILayout.Label("Course", Theme.SectionLabel);
			foreach (Course course in courses) {
				string grid = course.GridObjects.Count > 0 ? "map grid" : course.GridPath.Count >= 2 ? "laid-out grid" : "grid straight back";
				string text = $"<b>{course.Name}</b>  <color={Theme.MutedHex}>{course.KindName} · {TimeAttackRun.Distance(course.Length)} · {grid}</color>";
				if (GUILayout.Button(text, hostCourse_ == course.Code ? Theme.RowSelected : Theme.Row)) {
					hostCourse_ = course.Code;
				}
			}
			Course picked = courses.FirstOrDefault(c => c.Code == hostCourse_);
			if (picked?.Traps.Count > 0) {
				GUILayout.Label("Scoring", Theme.SectionLabel);
				hostTrapScoring_ = Choice(new[] { "Fastest time", $"Speed traps (highest total of {picked.Traps.Count})" }, hostTrapScoring_ ? 1 : 0) == 1;
			}
			if (picked?.IsCircuit == true) {
				GUILayout.Label("Laps", Theme.SectionLabel);
				hostLaps_ = Choice(RaceLapChoices.Select(l => l.ToString(CultureInfo.InvariantCulture)).ToArray(), hostLaps_);
				GUILayout.Label("Elimination (the last car each lap is out)", Theme.SectionLabel);
				hostElimination_ = Choice(new[] { "Off", "On" }, hostElimination_ ? 1 : 0) == 1;
			}
			GUILayout.Label("Grid", Theme.SectionLabel);
			hostOrder_ = Choice(GridOrders, hostOrder_);
			hostPole_ = Choice(PoleSides, hostPole_);
			hostLayout_ = Choice(Layouts, hostLayout_);
			if (hostOrder_ == (int)GridOrder.Qualifying || hostTournament_ == 4) {
				GUILayout.Label("Qualifying: minutes and runs each", Theme.SectionLabel);
				hostQualMinutes_ = Choice(QualifyingMinuteChoices.Select(m => $"{m} min").ToArray(), hostQualMinutes_);
				hostQualRuns_ = Choice(QualifyingRunChoices, hostQualRuns_);
			}
			GUILayout.Label("Jump start", Theme.SectionLabel);
			hostJump_ = Choice(JumpRules, hostJump_);
			GUILayout.Label("Mode", Theme.SectionLabel);
			hostMode_ = Choice(BattleModes, hostMode_);
			GUILayout.Label(hostMode_ == 0 ? "Every car TRC spec, all on the same tune set, no flagged mods." : "Any setup. It doesn't count.", Theme.Small);
			GUILayout.Label("Drivers (most)", Theme.SectionLabel);
			hostMax_ = Choice(MaxDriverChoices.Select(m => m.ToString(CultureInfo.InvariantCulture)).ToArray(), hostMax_);

			GUILayout.Label("Tournament", Theme.SectionLabel);
			hostTournament_ = Choice(TournamentKinds, hostTournament_);
			DrawTournamentOptions(courses, picked);

			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && picked != null;
			if (GUILayout.Button(hostTournament_ > 0 ? "Open the tournament" : "Open the race", Theme.ButtonPrimary)) {
				HostRace(picked);
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		/// <summary>The room game's options: how long, and its own rules.</summary>
		private void DrawGameForm() {
			GUILayout.Label(RoomModeRules[hostGame_], Theme.Small);
			if (hostGame_ != (int)RoomMode.Convoy) {
				GUILayout.Label("Round length", Theme.SectionLabel);
				hostRoundMinutes_ = Choice(RoundMinuteChoices.Select(m => $"{m} min").ToArray(), hostRoundMinutes_);
			}
			if (hostGame_ == (int)RoomMode.CopsAndRunners) {
				GUILayout.Label("Cops at the start", Theme.SectionLabel);
				hostCops_ = Choice(CopChoices.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray(), hostCops_);
			}
			if (hostGame_ == (int)RoomMode.KingOfTheHill) {
				GUILayout.Label("Seconds on the hill to win", Theme.SectionLabel);
				hostHill_ = Choice(HillSecondChoices.Select(s => $"{s} s").ToArray(), hostHill_);
			}
			if (hostGame_ == (int)RoomMode.KeepUp) {
				GUILayout.Label("Gap allowed", Theme.SectionLabel);
				hostKeepUp_ = Choice(KeepUpChoices.Select(m => $"{m} m").ToArray(), hostKeepUp_);
			}
			GUILayout.Label("Drivers (most)", Theme.SectionLabel);
			hostMax_ = Choice(MaxDriverChoices.Select(m => m.ToString(CultureInfo.InvariantCulture)).ToArray(), hostMax_);
			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button($"Open {RoomModeNames[hostGame_].ToLowerInvariant()}", Theme.ButtonPrimary)) {
				HostRace(null);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawTournamentOptions(List<Course> courses, Course picked) {
			switch (hostTournament_) {
				case 1:
					GUILayout.Label($"Races score {string.Join("-", TournamentSettings.DefaultPoints.Take(10))}... down to 16th, +1 for the fastest lap on circuits. Most points wins.", Theme.Small);
					GUILayout.Label("Rounds (pick a course above, then add it)", Theme.SectionLabel);
					for (int i = 0; i < hostRounds_.Count; i++) {
						TournamentRound r = hostRounds_[i];
						GUILayout.BeginHorizontal();
						GUILayout.Label($"{i + 1}. {r.CourseName}{(r.Laps > 1 ? $", {r.Laps} laps" : "")}{(r.ReverseGrid ? ", reverse grid" : "")}", Theme.Body);
						GUILayout.FlexibleSpace();
						if (GUILayout.Button("Remove", Theme.Button)) {
							hostRounds_.RemoveAt(i);
						}
						GUILayout.EndHorizontal();
					}
					GUILayout.BeginHorizontal();
					roundReverse_ = Choice(new[] { "Normal grid", "Reverse grid" }, roundReverse_ ? 1 : 0) == 1;
					bool enabled = GUI.enabled;
					GUI.enabled = enabled && picked != null && hostRounds_.Count < 12;
					if (GUILayout.Button("Add round", Theme.Button)) {
						hostRounds_.Add(new TournamentRound { CourseCode = picked.Code, CourseName = picked.Name, Laps = picked.IsCircuit ? RaceLapChoices[hostLaps_] : 1, ReverseGrid = roundReverse_ });
					}
					GUI.enabled = enabled;
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
					break;
				case 2:
					GUILayout.Label("1v1 battles in a bracket seeded by the grid order (qualifying is best). Top seeds get byes when the field isn't full.", Theme.Small);
					GUILayout.Label("Battles", Theme.SectionLabel);
					koFormat_ = Choice(KnockoutFormats, koFormat_);
					koBestOf_ = Choice(BestOfChoices.Select(b => $"Best of {b}").ToArray(), koBestOf_);
					GUILayout.Label("Final", Theme.SectionLabel);
					koFinalFormat_ = Choice(KnockoutFormats, koFinalFormat_);
					koFinalBestOf_ = Choice(BestOfChoices.Select(b => $"Best of {b}").ToArray(), koFinalBestOf_);
					koThird_ = Choice(new[] { "Race for 3rd", "No race for 3rd" }, koThird_ ? 0 : 1) == 0;
					break;
				case 3:
					GUILayout.Label("Heats race first; the top of each heat goes through to one final race.", Theme.Small);
					GUILayout.Label("Drivers per heat", Theme.SectionLabel);
					heatSize_ = Choice(HeatSizes.Select(h => h.ToString(CultureInfo.InvariantCulture)).ToArray(), heatSize_);
					GUILayout.Label("Through to the final from each heat", Theme.SectionLabel);
					heatAdvance_ = Choice(AdvanceChoices.Select(h => $"Top {h}").ToArray(), heatAdvance_);
					break;
				case 4:
					GUILayout.Label("Everyone drives time attacks on the course for the qualifying time; the best time wins. No traffic, so lag doesn't matter.", Theme.Small);
					break;
			}
		}

		private void DrawRaceLobby() {
			RaceSession r = race_;
			double now = BattleNow;
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label(r.Settings.Name.Length > 0 ? r.Settings.Name : $"{r.HostName}'s race", Theme.Heading);
			string laps = r.Course?.IsCircuit == true ? $" · {r.Settings.Laps} lap{(r.Settings.Laps == 1 ? "" : "s")}{(r.Settings.Elimination ? ", elimination" : "")}" : "";
			if (r.IsGame) {
				GUILayout.Label($"{RoomModeNames[(int)r.Settings.Mode]} · anywhere on the map{(r.Settings.Mode != RoomMode.Convoy ? $" · {r.Settings.RoundMinutes} min" : "")}", Theme.Small);
				GUILayout.Label(RoomModeRules[(int)r.Settings.Mode], Theme.Small);
			} else {
				GUILayout.Label($"{r.Course?.Name ?? r.CourseCode}{laps} · {(r.Settings.Open ? "Open" : "TRC spec")}{(r.Settings.SpeedTrapScoring ? " · speed trap race" : "")} · grid {GridOrders[(int)Math.Min((int)r.Settings.Order, 3)].ToLowerInvariant()}, " +
					$"{(r.Settings.PoleLeft ? "pole left" : "pole right")} · jump start {(r.Settings.Jump == JumpRule.Disqualify ? "disqualifies" : $"+{r.Settings.JumpPenaltySeconds:0} s")}", Theme.Small);
			}
			GUILayout.Label(r.Message, Theme.Body);
			if (r.RoundTrip > 0) {
				GUILayout.Label($"Connection to the host: {r.RoundTrip * 1000:0} ms there and back", Theme.Small);
			}
			GUILayout.EndVertical();

			if (r.Lines.Count > 0) {
				GUILayout.BeginVertical(Theme.Card);
				foreach (string line in r.Lines) {
					GUILayout.Label(line, line == r.Lines[0] ? Theme.Body : Theme.Small);
				}
				GUILayout.EndVertical();
			}

			if (r.Results != null) {
				GUILayout.Label("Results", Theme.SectionLabel);
				GUILayout.BeginVertical(Theme.Card);
				double winner = r.Results.FirstOrDefault()?.State == RacerState.Finished ? r.Results[0].Time : 0;
				foreach (RaceResultRow row in r.Results) {
					string gap = row.State == RacerState.Finished && row.Position > 1 && winner > 0 ? $" <color={Theme.MutedHex}>{TimeAttackRun.FormatGap(row.Time - winner)}</color>" : "";
					string lap = row.BestLap > 0 ? $" <color={Theme.MutedHex}>· best lap {TimeAttackRun.FormatTime(row.BestLap)}</color>" : "";
					GUILayout.BeginHorizontal();
					string incidents = row.Incidents > 0 ? $" <color={Theme.WarnHex}>· {row.Incidents} incident(s)</color>" : "";
					GUILayout.Label($"<b>P{row.Position}</b>  {row.Name}  {RowText(row)}{(r.IsGame ? "" : gap + lap)}{incidents}", row.Id == MySteamId ? Theme.Body : Theme.Small);
					GUILayout.FlexibleSpace();
					// The host as race director: penalties after the race, and everyone sees the new results.
					if (r.IsHost && !r.IsGame) {
						if (GUILayout.Button("+5 s", Theme.Button)) {
							r.Penalize(row.Id, 5, false, BattleNow);
							TrcLog.Info($"Race {r.Id}: +5 s for {row.Name}.");
						}
						if (GUILayout.Button("+10 s", Theme.Button)) {
							r.Penalize(row.Id, 10, false, BattleNow);
							TrcLog.Info($"Race {r.Id}: +10 s for {row.Name}.");
						}
						if (GUILayout.Button("DQ", Theme.Button)) {
							r.Penalize(row.Id, 0, true, BattleNow);
							TrcLog.Info($"Race {r.Id}: {row.Name} disqualified by the host.");
						}
						if ((row.Penalty > 0 || row.State == RacerState.Disqualified) && GUILayout.Button("Clear", Theme.Button)) {
							r.Penalize(row.Id, 0, false, BattleNow);
						}
					}
					GUILayout.EndHorizontal();
				}
				GUILayout.EndVertical();
			}

			GUILayout.Label($"Drivers ({r.Drivers.Count}/{r.Settings.MaxPlayers})", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			bool racing = r.Phase == RacePhase.Gridding || r.Phase == RacePhase.Countdown || r.Phase == RacePhase.Running;
			List<RaceStanding> standings = racing ? r.Standings(now) : null;
			foreach (RaceDriver d in racing ? standings.Select(s => s.Driver).Concat(r.Drivers.Where(x => !x.Racer)) : r.Drivers.OrderBy(x => x.Slot < 0 ? 99 : x.Slot)) {
				var parts = new List<string>();
				if (d.Slot >= 0 && r.Phase == RacePhase.Gridding) {
					parts.Add($"grid {d.Slot + 1}");
				}
				if (racing && d.Racer) {
					parts.Add($"P{standings.First(s => s.Driver == d).Position}");
				}
				if (!d.Racer) {
					parts.Add("watching");
				}
				if (d.Car.Length > 0) {
					parts.Add($"{d.Car}{(d.TuneSet.Length > 0 ? $", {d.TuneSet}" : "")}{(d.Spec ? "" : r.Settings.Open ? "" : " (not spec)")}");
				}
				if (r.Phase == RacePhase.Gridding && d.Racer) {
					parts.Add(d.Ready ? $"<color={Theme.GoodHex}>ready</color>" : "lining up");
				}
				string rating = RatingOf(d.Id, r.Course?.Kind ?? CourseKind.Touge);
				if (rating.Length > 0) {
					parts.Add(rating);
				}
				if (d.BestQualifying > 0) {
					parts.Add($"qualified {TimeAttackRun.FormatTime(d.BestQualifying)}");
				}
				if (d.State == RacerState.Finished && racing) {
					parts.Add($"finished {TimeAttackRun.FormatTime(d.Time)}");
				} else if (d.State != RacerState.Racing && racing) {
					parts.Add($"<color={Theme.BadHex}>{d.State.ToString().ToLowerInvariant()}</color>");
				}
				if (r.IsGame && d.Role != RoomRole.None) {
					parts.Insert(0, d.Role.ToString().ToLowerInvariant());
				}
				if (r.IsGame && racing) {
					parts.Add($"{d.Points} pts{(d.Out ? " (out)" : "")}");
				}
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{d.Name}</b>{(d.Id == r.HostId ? " (host)" : "")}  <color={Theme.MutedHex}>{string.Join(" · ", parts)}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (r.IsHost && d.Id != MySteamId && GUILayout.Button("Remove", Theme.Button)) {
					r.Kick(d.Id, now);
				}
				GUILayout.EndHorizontal();
				// Race control: the host sees each driver's incidents as their mod reported them.
				if (r.IsHost && d.IncidentLog.Count > 0) {
					GUILayout.Label($"<color={Theme.WarnHex}>    {string.Join("  ·  ", d.IncidentLog.Skip(Math.Max(0, d.IncidentLog.Count - 3)))}</color>", Theme.Small);
				}
			}
			GUILayout.EndVertical();

			GUILayout.BeginHorizontal();
			if (r.Phase == RacePhase.Gridding && r.MySpot().HasValue && GUILayout.Button("Teleport to my spot", Theme.ButtonPrimary)) {
				TeleportToRaceSpot();
			}
			if (r.Phase == RacePhase.Qualifying && GUILayout.Button("Teleport to start", Theme.ButtonPrimary)) {
				TeleportToQualifyingStart();
			}
			if (r.IsHost) {
				DrawHostButtons(r, now);
			}
			GUILayout.FlexibleSpace();
			if (GUILayout.Button(r.IsHost ? "Close race" : "Leave race", Theme.Button)) {
				LeaveRace();
			}
			GUILayout.EndHorizontal();
		}

		private void DrawHostButtons(RaceSession r, double now) {
			switch (r.Phase) {
				case RacePhase.Lobby:
				case RacePhase.Finished:
					if (hostTournament_ > 0 && tournament_ == null && !r.IsGame) {
						if (r.Settings.Order == GridOrder.Qualifying && GUILayout.Button("Start qualifying", Theme.Button)) {
							r.StartQualifying(now);
						}
						if (r.Drivers.Count >= 2 && GUILayout.Button("Start the tournament", Theme.ButtonPrimary)) {
							StartTournament();
							if (tournament_.Settings.Kind != TournamentKind.Knockout) {
								NextTournamentStep();
							}
						}
					} else if (tournament_ != null) {
						if (!tournament_.Done && GUILayout.Button(NextStepLabel(), Theme.ButtonPrimary)) {
							NextTournamentStep();
						}
					} else {
						if (r.Settings.Order == GridOrder.Qualifying && GUILayout.Button("Start qualifying", Theme.Button)) {
							r.StartQualifying(now);
						}
						if (GUILayout.Button(r.Phase == RacePhase.Finished ? "Run it back" : "Start race", Theme.ButtonPrimary)) {
							r.StartRace(now);
						}
					}
					if (r.Phase == RacePhase.Finished && GUILayout.Button("Back to lobby", Theme.Button)) {
						r.BackToLobby();
					}
					break;
				case RacePhase.Qualifying:
					if (GUILayout.Button("End qualifying", Theme.Button)) {
						r.EndQualifying(now);
					}
					break;
				case RacePhase.Gridding:
					if (GUILayout.Button("Go now", Theme.Button)) {
						r.GoNow(now);
					}
					if (GUILayout.Button("Call it off", Theme.Button)) {
						r.EndRace(now, 0);
					}
					break;
				case RacePhase.Countdown:
				case RacePhase.Running:
					if (r.EndAt < 0 && GUILayout.Button("End in 60 s", Theme.Button)) {
						r.EndRace(now, 60);
					}
					if (GUILayout.Button("End now", Theme.Button)) {
						r.EndRace(now, 0);
					}
					break;
			}
		}

		private string NextStepLabel() {
			switch (tournament_.Settings.Kind) {
				case TournamentKind.Points:
					return $"Start round {tournament_.RoundIndex + 1}";
				case TournamentKind.Heats:
					return tournament_.InFinal ? "Start the final" : $"Start heat {tournament_.HeatIndex + 1}";
				case TournamentKind.Knockout:
					return race_.MatchId != 0 ? "Call the battle again" : "Call the next battle";
				default:
					return "Start the shootout";
			}
		}

		// ---- Over the track ----

		private bool RaceHudVisible => hasPose_ && race_ != null && !race_.Ended &&
			(race_.IAmRacing || race_.Phase == RacePhase.Qualifying || (race_.Phase == RacePhase.Finished && race_.Results != null && race_.MyDriver?.Racer == true));

		/// <summary>The race over the track: where to line up, the countdown, your place and the gaps, then the result.</summary>
		private void DrawRaceHud(float screenWidth, float screenHeight) {
			RaceSession r = race_;
			TimeAttackRun run = r.Run;
			double now = BattleNow;
			if (run != null && run.State == TimeAttackRun.Phase.Countdown && run.CountdownNumber > 0) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), run.CountdownNumber.ToString(CultureInfo.InvariantCulture), Theme.HudBig);
			} else if (run != null && run.ShowGo(now)) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), "GO!", Theme.HudBig);
			}

			if (r.Phase == RacePhase.Qualifying && run != null && run.State == TimeAttackRun.Phase.Running) {
				string left = r.QualifyingEnds >= 0 ? $"{Math.Max(0, r.QualifyingEnds - now) / 60.0:0.0} min left" : "";
				DrawTimer(screenWidth, screenHeight, run, $"Qualifying · {left}{(r.MyDriver?.BestQualifying > 0 ? $" · best {TimeAttackRun.FormatTime(r.MyDriver.BestQualifying)}" : "")}");
				return;
			}
			if (r.IsGame && (r.Phase == RacePhase.Countdown || r.Phase == RacePhase.Running)) {
				DrawGameHud(screenWidth, screenHeight, r, now);
				return;
			}
			if (r.Phase == RacePhase.Running && run != null && run.State == TimeAttackRun.Phase.Running && r.MyDriver?.State == RacerState.Racing) {
				List<RaceStanding> standings = r.Standings(now);
				RaceStanding mine = standings.FirstOrDefault(s => s.Driver.Id == MySteamId);
				var lines = new List<string>();
				string lap = r.Course.IsCircuit ? $" · Lap {run.Lap}/{run.Laps}" : "";
				lines.Add($"<b>P{mine?.Position}/{standings.Count}</b>{lap}");
				if (mine != null) {
					RaceStanding ahead = standings.LastOrDefault(s => s.Position < mine.Position && s.Driver.State == RacerState.Racing);
					RaceStanding behind = standings.FirstOrDefault(s => s.Position > mine.Position && s.Driver.State == RacerState.Racing);
					var gaps = new List<string>();
					if (ahead != null) {
						gaps.Add($"{ahead.Progress - mine.Progress:0} m to P{ahead.Position}");
					}
					if (behind != null) {
						gaps.Add($"P{behind.Position} {mine.Progress - behind.Progress:0} m back");
					}
					if (gaps.Count > 0) {
						lines.Add(string.Join(" · ", gaps));
					}
				}
				if (mine != null) {
					var (yellow, blue) = RaceFlags.For(mine.Progress, r.Course.IsCircuit ? r.Course.Length : 0f,
						standings.Where(s => s != mine).Select(s => (s.Driver.Name, s.Progress, s.Driver.Kmh, s.Driver.State != RacerState.Racing && s.Driver.State != RacerState.Finished)));
					if (yellow.Length > 0) {
						lines.Add($"<color={Theme.WarnHex}>{yellow}</color>");
					}
					if (blue.Length > 0) {
						lines.Add($"<color=#8FB8FF>{blue}</color>");
					}
				}
				if (r.EndAt >= 0) {
					lines.Add($"<color={Theme.WarnHex}>The race ends in {Math.Max(0, r.EndAt - now):0} s</color>");
				}
				DrawTimer(screenWidth, screenHeight, run, string.Join("\n", lines));
				return;
			}
			string text;
			if (r.Phase == RacePhase.Finished && r.Results != null) {
				RaceResultRow me = r.Results.FirstOrDefault(x => x.Id == MySteamId);
				string podium = string.Join("  ", r.Results.Take(3).Select(x => $"P{x.Position} {x.Name}"));
				text = $"<b>{(me != null ? $"P{me.Position} of {r.Results.Count}: {RowText(me)}" : "Race finished")}</b>\n<color={Theme.MutedHex}>{podium}</color>";
			} else {
				text = $"<b>{r.Settings.Name}</b> · {r.Course?.Name}\n<color={Theme.MutedHex}>{r.Message}</color>";
			}
			DrawHudPill(screenWidth, screenHeight, text);
		}

		/// <summary>A room game over the track: the start count, your part and points, the top three, the time left, and cut-up passes.</summary>
		private void DrawGameHud(float screenWidth, float screenHeight, RaceSession r, double now) {
			if (r.Phase == RacePhase.Countdown) {
				DrawHudPill(screenWidth, screenHeight, $"<b>{RoomModeNames[(int)r.Settings.Mode]}</b>\n<color={Theme.MutedHex}>{r.Message}</color>");
				return;
			}
			var lines = new List<string> { $"<b>{RoomModeNames[(int)r.Settings.Mode]}</b>{(r.EndAt >= 0 ? $" · {Math.Max(0, r.EndAt - now) / 60.0:0.0} min left" : "")}", r.Message };
			if (r.Settings.Mode != RoomMode.Convoy) {
				lines.Add($"<color={Theme.MutedHex}>{string.Join("   ", r.Standings(now).Take(3).Select(s => $"{s.Position}. {s.Driver.Name} {s.Driver.Points}"))}</color>");
			}
			if (CutUpFlashVisible) {
				lines.Add(cutUpFlash_);
			}
			DrawHudPill(screenWidth, screenHeight, string.Join("\n", lines));
		}

		/// <summary>An open race this player isn't in, for a while after it first appears.</summary>
		private RaceMessage RaceToShow() {
			if (race_ != null || !hasPose_) {
				return null;
			}
			foreach (var (lobby, _, first) in openRaces_.Values) {
				if (lobby.Phase == RacePhase.Lobby && lobby.Entries.Count < lobby.Settings.MaxPlayers && Time.unscaledTime - first < RaceInviteSeconds) {
					return lobby;
				}
			}
			return null;
		}

		private void DrawRaceInviteHud(float screenWidth, float screenHeight) {
			RaceMessage lobby = RaceToShow();
			if (lobby == null) {
				return;
			}
			Course course = courses_.ByCode(lobby.CourseCode);
			string what = lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode].ToLowerInvariant() : lobby.Lines.Count > 0 ? "tournament" : "race";
			string text = $"<b>{BattleMessage.PlainName(lobby.HostName)} opened a {what}</b>\n" +
				$"<color={Theme.MutedHex}>{(lobby.Settings.Mode != RoomMode.Race ? "anywhere on the map" : course?.Name ?? "a course")} · {lobby.Entries.Count}/{lobby.Settings.MaxPlayers} · press {shortcut_} to join (Battle, Races)</color>";
			if (hudRect_.width <= 0f) {
				DrawHudPill(screenWidth, screenHeight, text);
				return;
			}
			var content = new GUIContent(text);
			float width = Mathf.Min(340f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			float x = Mathf.Clamp(hudRect_.center.x - width / 2f, 0f, Mathf.Max(0f, screenWidth - width));
			float y = hudRect_.yMax + HudGap + height <= screenHeight ? hudRect_.yMax + HudGap : Mathf.Max(0f, hudRect_.y - HudGap - height);
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(new Rect(x, y, width, height), content, Theme.HudPill);
			}
		}
	}
}
