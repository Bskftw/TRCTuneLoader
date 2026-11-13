// 1v1 battles in game, fed from Update: challenging a player, starting a battle, answering a challenge, its course, and the car checks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Battles in game: the Battle view of the Time attack tab (challenge a player, answer a challenge, the battle
	/// itself), the battle fed from Update, and what's drawn over the track. This file is the part fed from Update;
	/// routing messages is BattleRouting.cs, the pages and HUD are in src/pages/ (BattlePage, NewChallengePage,
	/// BattleHud), the battle logic is BattleSession in src/racing/, and messages go through BattleSync.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>Players need at least this TRC Tune Loader version to battle (battle messages changed in 0.10.1, 0.11.0, 0.12.0 and 0.13.0).</summary>
		private static readonly System.Version BattleVersion = new System.Version(0, 13, 0);
		private static readonly int[] BattleLapChoices = { 1, 2, 3, 5 };
		private static readonly string[] BattleFormats = { "Side-by-side race", "Cat and mouse", "Highway battle (SP)", "Speed traps" };
		private const float InviteSeconds = 45f;

		/// <summary>A battle on the track survives the car being out of reach this long (a teleport, a loading hitch).</summary>
		private const float NoCarGraceSeconds = 3f;

		private readonly BattleSync battleSync_ = new BattleSync();
		private readonly System.Random battleIds_ = new System.Random();
		private BattleSession battle_;
		private bool battleLogged_;
		private float battleEndedAt_ = -1f;
		private BattleCar lastBattleCar_;
		private readonly List<(BattleMessage Challenge, float At)> invites_ = new List<(BattleMessage, float)>();

		/// <summary>Challenges already answered or taken back: copies still on the way are ignored.</summary>
		private readonly HashSet<uint> closedChallenges_ = new HashSet<uint>();

		/// <summary>What the log already says, so it tells each step of a battle once.</summary>
		private readonly HashSet<string> loggedSteps_ = new HashSet<string>(StringComparer.Ordinal);
		private string loggedState_ = "";
		private float noCarSince_ = -1f;

		/// <summary>The gap shown over the track (metres, + = ahead), eased so it doesn't jump with every heartbeat.</summary>
		private float shownGap_;
		private bool shownGapSet_;
		private const float GapEaseSeconds = 0.5f;

		/// <summary>Closer than this (about half a car) shows as side by side.</summary>
		private const float SideBySideMetres = 2f;

		private ulong pickedOpponent_;
		private string pickedCourse_ = "";
		private int pickedFormat_;
		/// <summary>0 TRC spec, 1 a regulation (<see cref="pickedRule_"/>), 2 Open; and just for fun (no ratings).</summary>
		private int pickedMode_;
		private string pickedRule_ = "";
		private bool pickedFun_;
		private string pickedConsole_ = "";
		private int pickedBattleLaps_;

		/// <summary>Regulations (and the fun flag) in battles and races need this on the other side.</summary>
		private static readonly System.Version RuleVersion = new System.Version(0, 16, 0);
		private string battleResult_ = "";
		private Vector2 battleScroll_;

		private static double BattleNow => Time.realtimeSinceStartupAsDouble;
		private ulong MySteamId => Kino.Info.SteamId;

		/// <summary>A battle is on the track (lined up, running, or just finished).</summary>
		private bool BattleOnTrack => battle_ != null && battle_.Started && (battle_.Active || Time.unscaledTime - battleEndedAt_ < ResultSeconds);

		private void SetupBattles() {
			battleSync_.Start(MySteamId);
			TrcLog.Info(battleSync_.Status);
		}

		/// <summary>Called from Update every frame (after UpdateCourses, which reads the car).</summary>
		private void UpdateBattles() {
			double now = BattleNow;
			battleSync_.Pump(now);
			foreach (BattleMessage message in battleSync_.Drain()) {
				Route(message, now);
			}
			invites_.RemoveAll(i => Time.unscaledTime - i.At > InviteSeconds);

			if (battle_ == null) {
				return;
			}

			if (battle_.Started && battle_.Active && !hasPose_) {
				if (noCarSince_ < 0f) {
					noCarSince_ = Time.unscaledTime;
				} else if (Time.unscaledTime - noCarSince_ > NoCarGraceSeconds) {
					battle_.Leave(now);
					battleResult_ = "You left the track, so you left the battle.";
				}
			} else {
				noCarSince_ = -1f;
			}
			if (hasPose_ || !battle_.Started) {
				bool? onRoad = null;
				if (battle_.State == BattleSession.Phase.Running && battle_.Course.RoadColliders.Count > 0 && adapter_.TryCheckOverRoad(battle_.Course.RoadColliders, out bool overRoad)) {
					onRoad = overRoad;
				}
				battle_.Tick(now, pose_.Position, pose_.Forward, pose_.SpeedKmh, Time.timeScale, onRoad);
			}
			foreach (BattleMessage message in battle_.TakeOutbox()) {
				SendBattle(message);
			}
			LogBattleState();
			UpdateShownGap(now);
			if (battle_.Started && battle_.Active && invites_.Count > 0) {
				CloseInvitesForBattle();
			}

			if (!battle_.Active && battleEndedAt_ < 0) {
				battleEndedAt_ = Time.unscaledTime;
				ReportMatchBattle(battle_);
				RateBattle(battle_);
				if (battle_.Started) { visit_?.Battle(battle_.Winner == battle_.Me); }
				TrcLog.Info($"Battle {battle_.Id} vs {battle_.PeerName} on {battle_.Course.Name}: {battle_.Message} ({BattleLog.ResultOf(battle_)}, {battle_.Runs.Count} run(s)).");
				if (battle_.Started && !battleLogged_) {
					battleLogged_ = true;
					try {
						BattleLog.Append(TimesFolder, battle_, lastBattleCar_?.Car ?? "", lastBattleCar_?.TuneSet ?? "", Version);
						LedgerEvent("battle", EventLedger.BattleFields(battle_, lastBattleCar_?.Car ?? "", lastBattleCar_?.TuneSet ?? ""));
					} catch (Exception e) {
						TrcLog.Error(e);
					}
				}
			}
		}

		private void UpdateShownGap(double now) {
			if (battle_.State != BattleSession.Phase.Running || battle_.Run == null || !battle_.Run.Active) {
				shownGapSet_ = false;
				return;
			}
			float gap = battle_.Run.TotalProgress - battle_.PeerProgressAt(now);
			shownGap_ = shownGapSet_ ? shownGap_ + (gap - shownGap_) * (1f - Mathf.Exp(-Time.unscaledDeltaTime / GapEaseSeconds)) : gap;
			shownGapSet_ = true;
		}

		private void LogBattleState() {
			string state = $"{battle_.Id} {battle_.State} {battle_.RunIndex}";
			if (state == loggedState_ || !battle_.Active) {
				return;
			}
			loggedState_ = state;
			TrcLog.Info($"Battle {battle_.Id}: {battle_.State}, run {battle_.RunIndex + 1}. {battle_.Message}");
		}

		/// <summary>What the battle needs to know about this player's car: TRC spec (no flagged mods either), set, code and car.</summary>
		private BattleCar CheckBattleCar() {
			RunCheck check = CheckRunCar();
			lastBattleCar_ = new BattleCar {
				Spec = check.State == SpecState.Spec && check.Flagged == 0,
				TuneSet = check.Set,
				TuneCode = check.Code,
				Car = check.Car,
				RuleOk = MyCarMeetsActiveRule(),
			};
			return lastBattleCar_;
		}

		/// <summary>This player's name: as the room shows it, or (outside a room) CarX's own nickname for them.</summary>
		private string MyName() {
			string name = players_.FirstOrDefault(p => p.Player.IsLocal)?.Player.Name ?? "";
			return name.Length > 0 ? name : adapter_.PlayerName;
		}

		private void StopOtherCourseWork() {
			run_ = null;
			pathRecorder_ = null;
			recorder_.Clear();
		}

		private void ChallengePlayer(PlayerRow opponent, Course course) {
			if (InRace) {
				battleResult_ = "Finish or leave your race first.";
				return;
			}
			var format = (BattleFormat)pickedFormat_;
			if (format == BattleFormat.SpeedTraps && sync_?.ReportFrom(opponent.Player.SteamId) is TrcSync.Report report &&
				report.LoaderVersion.Length > 0 && RoomBoard.IsNewer("0.15.1", report.LoaderVersion)) {
				// Their copy never sees a speed trap challenge (it goes out as a newer protocol).
				battleResult_ = $"{opponent.Player.Name} has TRC Tune Loader {report.LoaderVersion}: speed trap battles need 0.15.1 or newer on both sides.";
				return;
			}
			if (format == BattleFormat.SpeedTraps && course.Traps.Count == 0) {
				// A course without traps of its own: the map's free-roam traps along it, like a speed trap race.
				List<CourseGate> along = TrapsAlong(course);
				if (along.Count == 0) {
					battleResult_ = "That course has no speed traps on it: place some along it in Time attack > Speed traps, or add them in the course's Edit.";
					return;
				}
				course = course.WithTraps(along);
			}
			Regulation regulation = pickedMode_ == 1 ? RegulationByCode(pickedRule_) : null;
			if (pickedMode_ == 1 && regulation == null) {
				battleResult_ = "Pick a regulation first (or make one in the Regulations tab).";
				return;
			}
			if (regulation != null && sync_?.ReportFrom(opponent.Player.SteamId) is TrcSync.Report theirs &&
				(!System.Version.TryParse(theirs.LoaderVersion, out System.Version v) || v < RuleVersion)) {
				battleResult_ = $"{opponent.Player.Name} has TRC Tune Loader {theirs.LoaderVersion}: battles run to a regulation need {RuleVersion} or newer on both sides.";
				return;
			}
			StartBattle(opponent.Player.SteamId, opponent.Player.Name, course, format, pickedMode_ == 2, BattleLapChoices[pickedBattleLaps_], 0, 0, regulation, pickedFun_);
		}

		/// <summary>Challenges a driver: from the Battle view, or by itself for a knockout battle the race host called.</summary>
		private void StartBattle(ulong peer, string peerName, Course course, BattleFormat format, bool open, int laps, uint matchId, int matchGame,
			Regulation regulation = null, bool fun = false) {
			if (battle_ != null && battle_.Active) {
				battleResult_ = "Finish or leave the battle you're in first.";
				return;
			}
			StopOtherCourseWork();
			uint id = (uint)battleIds_.Next(1, int.MaxValue);
			if (course != null) {
				MeasureRoad(course);
			}
			battle_ = BattleSession.Challenge(id, MySteamId, MyName(), peer, peerName, course, format, open, CheckBattleCar, BattleNow,
				laps: laps, matchId: matchId, matchGame: matchGame, regulation: regulation, fun: fun);
			battleLogged_ = false;
			battleEndedAt_ = -1f;
			battleResult_ = "";
			TrcLog.Info($"Battle {id}: challenged {battle_.PeerName} ({peer}) on {course.Name} ({course.Code}, {course.Path.Count} path points), " +
				$"{BattleFormats[(int)format]}, {RuleText(open, regulation, fun)}{(regulation != null ? $" ({regulation.Code})" : "")}{(matchId != 0 ? $", tournament battle {matchId} game {matchGame}" : "")}.");
		}

		private void AnswerChallenge(BattleMessage challenge, bool accept) {
			invites_.RemoveAll(i => i.Challenge.BattleId == challenge.BattleId);
			closedChallenges_.Add(challenge.BattleId);
			double now = BattleNow;
			Course course = CourseForChallenge(challenge, out string problem);
			var session = BattleSession.FromChallenge(challenge, MySteamId, MyName(), course ?? new Course(), CheckBattleCar, now);

			if (!accept || course == null) {
				session.Decline(accept ? problem : "", now);
				foreach (BattleMessage message in session.TakeOutbox()) {
					SendBattle(message);
				}
				battleResult_ = accept ? $"Couldn't accept: {problem}" : "";
				TrcLog.Info($"Battle {challenge.BattleId}: {(accept ? $"couldn't accept: {problem}" : "declined")}.");
				return;
			}

			if (battle_ != null && battle_.Active) {
				// Taking back this player's own challenge (or leaving a battle): that message has to go out before the session is dropped.
				battle_.Leave(now);
				foreach (BattleMessage message in battle_.TakeOutbox()) {
					SendBattle(message);
				}
				TrcLog.Info($"Battle {battle_.Id} vs {battle_.PeerName}: {battle_.Message}, to accept {session.PeerName}'s challenge.");
			}
			StopOtherCourseWork();
			battle_ = session;
			battle_.Accept(now);
			battleLogged_ = false;
			battleEndedAt_ = -1f;
			battleResult_ = "";
			TrcLog.Info($"Battle {challenge.BattleId}: accepted {battle_.PeerName}'s challenge on {course.Name} ({course.Code}).");
			CloseWindow();
		}

		/// <summary>
		/// The course a challenge is on: the one sent with it (checked against its code, and given this map's road
		/// and signs when it lines up with a map course here), or an official course this PC has.
		/// </summary>
		private Course CourseForChallenge(BattleMessage challenge, out string problem) {
			problem = "";
			if (challenge.Format == BattleFormat.Highway) {
				// No course: anywhere on the map.
				if (map_ == null) {
					problem = "drive onto the map first";
					return null;
				}
				return Course.FreeRoam(map_.Key);
			}
			Course course = challenge.Course ?? courses_.ByCode(challenge.CourseCode);
			if (course == null) {
				problem = "you don't have this course (another version of the map?)";
				return null;
			}
			if (challenge.Course != null && course.Code != challenge.CourseCode) {
				problem = "the course arrived damaged";
				return null;
			}
			if (map_ == null || course.MapKey != map_.Key) {
				problem = "you're not on the course's map";
				return null;
			}
			if (challenge.Course != null) {
				Course local = courses_.ForMap(map_.Key).FirstOrDefault(c => c.Source == CourseSource.Map && c.IsCircuit == course.IsCircuit && c.SameLines(course));
				if (local != null) {
					course.RoadKey = local.RoadKey;
					course.RoadColliders = local.RoadColliders;
					course.StartObject = local.StartObject;
					course.FinishObject = local.FinishObject;
				}
			}
			if (challenge.Format == BattleFormat.SpeedTraps) {
				// The challenger's traps, so both drivers score the same ones.
				if (challenge.Traps.Count == 0) {
					problem = "the challenge came without its speed traps";
					return null;
				}
				course = course.WithTraps(challenge.Traps);
			}
			return course;
		}

		private void TeleportToBattleSpot() {
			if (battle_ == null || battle_.State != BattleSession.Phase.Gridding) {
				return;
			}
			if (Time.unscaledTime < teleportReadyAt_) {
				battleResult_ = "Wait a moment before teleporting again.";
				return;
			}
			if (!adapter_.TryMoveCarTo(battle_.MySpot(), battle_.Course.Start.Forward, out string error)) {
				battleResult_ = $"Couldn't teleport: {error}";
				return;
			}
			teleportReadyAt_ = Time.unscaledTime + TeleportCooldownSeconds;
			battleResult_ = "";
			CloseWindow();
		}
	}
}
