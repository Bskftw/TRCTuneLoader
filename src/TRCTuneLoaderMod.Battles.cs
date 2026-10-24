using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Battles in game: the Battle view of the Time attack tab (challenge a player, answer a challenge, the battle
	/// itself), the battle fed from Update, and what's drawn over the track. The battle logic is in src/Battles/;
	/// messages go through BattleSync.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>Players need at least this TRC Tune Loader version to battle (battle messages changed in 0.10.1 and 0.11.0).</summary>
		private static readonly System.Version BattleVersion = new System.Version(0, 11, 0);
		private static readonly int[] BattleLapChoices = { 1, 2, 3, 5 };
		private static readonly string[] BattleFormats = { "Side-by-side race", "Cat and mouse" };
		private static readonly string[] BattleModes = { "TRC spec", "Open" };
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
		private int pickedMode_;
		private int pickedBattleLaps_;
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
				TrcLog.Info($"Battle {battle_.Id} vs {battle_.PeerName} on {battle_.Course.Name}: {battle_.Message} ({BattleLog.ResultOf(battle_)}, {battle_.Runs.Count} run(s)).");
				if (battle_.Started && !battleLogged_) {
					battleLogged_ = true;
					try {
						BattleLog.Append(TimesFolder, battle_, lastBattleCar_?.Car ?? "", lastBattleCar_?.TuneSet ?? "", Version);
					} catch (Exception e) {
						TrcLog.Error(e);
					}
				}
			}
		}

		private void Route(BattleMessage message, double now) {
			if (message.To != MySteamId) {
				return;
			}
			if (message.Kind == BattleKind.Challenge) {
				RouteChallenge(message);
				return;
			}
			LogReceived(message);
			if (message.Kind == BattleKind.Leave) {
				int taken = invites_.FindIndex(i => i.Challenge.BattleId == message.BattleId && i.Challenge.From == message.From);
				if (taken >= 0) {
					string name = BattleMessage.PlainName(invites_[taken].Challenge.FromName);
					invites_.RemoveAt(taken);
					closedChallenges_.Add(message.BattleId);
					bool inBattleWithThem = battle_ != null && battle_.Active && battle_.Started && battle_.Peer == message.From;
					if (!inBattleWithThem) {
						battleResult_ = $"{name} took their challenge back.";
					}
				}
			}
			battle_?.Receive(message, now);
		}

		/// <summary>
		/// A challenge for this player (every copy: the host sends it until our Seen gets there). It's answered
		/// "already in a battle" only during a battle; while this player's own challenge waits, it's shown like
		/// any other, and when it's the same battle the other way round, the two become one.
		/// </summary>
		private void RouteChallenge(BattleMessage challenge) {
			if (closedChallenges_.Contains(challenge.BattleId) || (battle_ != null && battle_.Id == challenge.BattleId)) {
				return;
			}
			string name = BattleMessage.PlainName(challenge.FromName);
			if (battle_ != null && battle_.Active && battle_.Started) {
				closedChallenges_.Add(challenge.BattleId);
				SendBattle(new BattleMessage {
					Kind = BattleKind.Answer, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Reason = "already in a battle",
				});
				TrcLog.Info($"Battle {challenge.BattleId}: challenge from {name} declined by itself, you're in a battle.");
				return;
			}

			Course course = CourseForChallenge(challenge, out string problem);
			SendBattle(new BattleMessage {
				Kind = BattleKind.Seen, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Accept = course != null, Reason = problem,
			});
			if (invites_.Exists(i => i.Challenge.BattleId == challenge.BattleId)) {
				return;
			}
			invites_.Add((challenge, Time.unscaledTime));
			string courseName = challenge.Course?.Name ?? course?.Name ?? "";
			TrcLog.Info($"Battle {challenge.BattleId}: challenge from {name} on {courseName} ({challenge.CourseCode}), " +
				$"{BattleFormats[(int)challenge.Format]}, {(challenge.Open ? "Open" : "TRC spec")}.{(course == null ? $" Can't accept: {problem}." : "")}");

			if (course != null && battle_ != null && battle_.YieldsTo(challenge)) {
				TrcLog.Info($"Battle {challenge.BattleId}: you and {name} challenged each other to the same battle, so it goes ahead on {name}'s challenge.");
				AnswerChallenge(challenge, true);
			}
		}

		/// <summary>
		/// A battle started: challenges still waiting are answered "already in a battle" (the opponent's own,
		/// left over from challenging each other, just goes), so none pops up during the battle.
		/// </summary>
		private void CloseInvitesForBattle() {
			foreach (var (challenge, _) in invites_) {
				closedChallenges_.Add(challenge.BattleId);
				if (challenge.From != battle_.Peer) {
					SendBattle(new BattleMessage {
						Kind = BattleKind.Answer, BattleId = challenge.BattleId, From = MySteamId, To = challenge.From, Reason = "already in a battle",
					});
				}
			}
			invites_.Clear();
		}

		private void SendBattle(BattleMessage message) {
			int packets = battleSync_.Send(message);
			if (message.Kind != BattleKind.Heartbeat && loggedSteps_.Add($"sent {message.BattleId} {message.Kind}")) {
				string how = packets == 0 ? " (KSL didn't send it)" : packets > 1 ? $" ({packets} packets)" : "";
				TrcLog.Info($"Battle {message.BattleId}: sent {message.Kind}{how}.");
			}
		}

		/// <summary>The first message of each kind in a battle goes in the log.</summary>
		private void LogReceived(BattleMessage message) {
			if (!loggedSteps_.Add($"got {message.BattleId} {message.Kind}")) {
				return;
			}
			string detail = message.Kind == BattleKind.Answer ? (message.Accept ? ": accepted" : $": declined{(message.Reason.Length > 0 ? $" ({message.Reason})" : "")}")
				: message.Kind == BattleKind.Seen ? (message.Accept ? ": the challenge got there" : $": the challenge got there, but they can't accept it ({message.Reason})")
				: message.Kind == BattleKind.Heartbeat ? " (the battle connection works)"
				: "";
			TrcLog.Info($"Battle {message.BattleId}: got {message.Kind}{detail}.");
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
			};
			return lastBattleCar_;
		}

		private string MyName() => players_.FirstOrDefault(p => p.Player.IsLocal)?.Player.Name ?? "";

		private void StopOtherCourseWork() {
			run_ = null;
			pathRecorder_ = null;
			recorder_.Clear();
		}

		private void ChallengePlayer(PlayerRow opponent, Course course) {
			if (battle_ != null && battle_.Active) {
				battleResult_ = "Finish or leave the battle you're in first.";
				return;
			}
			StopOtherCourseWork();
			uint id = (uint)battleIds_.Next(1, int.MaxValue);
			battle_ = BattleSession.Challenge(id, MySteamId, MyName(), opponent.Player.SteamId, opponent.Player.Name, course,
				(BattleFormat)pickedFormat_, pickedMode_ == 1, CheckBattleCar, BattleNow, laps: BattleLapChoices[pickedBattleLaps_]);
			battleLogged_ = false;
			battleEndedAt_ = -1f;
			battleResult_ = "";
			TrcLog.Info($"Battle {id}: challenged {battle_.PeerName} ({opponent.Player.SteamId}) on {course.Name} ({course.Code}, {course.Path.Count} path points), " +
				$"{BattleFormats[pickedFormat_]}, {BattleModes[pickedMode_]}.");
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

		// ---- Battle view ----

		private void DrawBattleView() {
			if (!battleSync_.IsAvailable) {
				GUILayout.Label(battleSync_.Status, Theme.PillNeutral);
				return;
			}
			RefreshPlayersIfDue();
			battleScroll_ = GUILayout.BeginScrollView(battleScroll_);

			if (!string.IsNullOrEmpty(battleResult_)) {
				GUILayout.Label(battleResult_, Theme.Body);
			}
			foreach (var (challenge, _) in invites_.ToList()) {
				DrawInvite(challenge);
			}
			if (battle_ != null && (battle_.Active || battle_.Started)) {
				DrawBattleCard();
			}
			if (battle_ == null || !battle_.Active) {
				DrawNewChallenge();
			}

			GUILayout.EndScrollView();
		}

		private void DrawInvite(BattleMessage challenge) {
			Course course = CourseForChallenge(challenge, out string problem);
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"{BattleMessage.PlainName(challenge.FromName)} challenges you", Theme.Heading);
			string courseName = challenge.Course?.Name ?? course?.Name ?? challenge.CourseCode;
			GUILayout.Label($"{courseName} · {BattleFormats[(int)challenge.Format]} · {(challenge.Open ? "Open" : "TRC spec")}", Theme.Body);
			if (course == null) {
				GUILayout.Label($"Can't accept: {problem}", Theme.PillWarn);
			}
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && course != null && hasPose_;
			if (GUILayout.Button("Accept", Theme.ButtonPrimary)) {
				AnswerChallenge(challenge, true);
			}
			GUI.enabled = enabled;
			if (GUILayout.Button("Decline", Theme.Button)) {
				AnswerChallenge(challenge, false);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void DrawBattleCard() {
			BattleSession b = battle_;
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"Battle vs {b.PeerName}", Theme.Heading);
			string role = b.Format == BattleFormat.CatAndMouse ? $" · run {b.RunIndex + 1}: you {(b.IAmLeader ? "lead" : "chase")}" : $" · run {b.RunIndex + 1}";
			string laps = b.Course.IsCircuit ? $" · {b.Laps} lap{(b.Laps == 1 ? "" : "s")}" : "";
			GUILayout.Label($"{b.Course.Name}{laps} · {BattleFormats[(int)b.Format]} · {(b.Open ? "Open" : "TRC spec")}{(b.Active ? role : "")}", Theme.Small);
			GUILayout.Label(b.Message, b.Active ? Theme.Body : b.Winner == b.Me ? Theme.PillGood : b.Winner == b.Peer ? Theme.PillBad : Theme.PillNeutral);

			foreach (BattleRun run in b.Runs) {
				string mine = run.Mine.Finished ? TimeAttackRun.FormatTime(run.Mine.Time) : "DNF";
				string theirs = run.Theirs.Finished ? TimeAttackRun.FormatTime(run.Theirs.Time) : "DNF";
				string led = b.Format == BattleFormat.CatAndMouse ? (run.Leader == b.Me ? " (you led)" : " (you chased)") : "";
				GUILayout.Label($"Run {run.Index + 1}{led}: you {mine}, {b.PeerName} {theirs}. {run.Text}", Theme.Small);
			}
			if (!b.Active && b.Started) {
				GUILayout.Label(b.Open ? "Open battle: it doesn't count." : b.Counted && b.Winner != 0 ? "TRC spec battle: both cars were spec the whole way." : "Doesn't count: a car wasn't TRC spec at a start or finish.", Theme.Small);
			}
			if (b.Active && b.RoundTrip > 0) {
				GUILayout.Label($"Connection: {b.RoundTrip * 1000:0} ms there and back", Theme.Small);
			}

			GUILayout.BeginHorizontal();
			if (b.State == BattleSession.Phase.Gridding && GUILayout.Button("Teleport to my spot", Theme.ButtonPrimary)) {
				TeleportToBattleSpot();
			}
			if (b.Active) {
				if (GUILayout.Button(b.State == BattleSession.Phase.Inviting ? "Take the challenge back" : "Leave battle", Theme.Button)) {
					b.Leave(BattleNow);
				}
			} else {
				PlayerRow peer = players_.FirstOrDefault(p => p.Player.SteamId == b.Peer);
				if (peer != null && b.Course.Usable && GUILayout.Button("Rematch", Theme.ButtonPrimary)) {
					pickedFormat_ = (int)b.Format;
					pickedMode_ = b.Open ? 1 : 0;
					pickedBattleLaps_ = Math.Max(0, Array.IndexOf(BattleLapChoices, b.Laps));
					ChallengePlayer(peer, b.Course);
				}
				// A course that came with the challenge (theirs, not one this PC has): keep it to practise on.
				if (!b.IsHost && b.Course.Source == CourseSource.Custom && string.IsNullOrEmpty(b.Course.FilePath) &&
					GUILayout.Button("Save this course", Theme.Button)) {
					SaveChallengeCourse(b.Course, $"{b.PeerName}'s course");
				}
				if (GUILayout.Button("Close", Theme.Button)) {
					battle_ = null;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void DrawNewChallenge() {
			GUILayout.Label("Challenge a player", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (!hasPose_ || map_ == null) {
				GUILayout.Label("Join a room and drive onto the map: you challenge players in the same room, on a course of that map.", Theme.Small);
				GUILayout.EndVertical();
				return;
			}

			List<PlayerRow> rivals = players_.Where(p => !p.Player.IsLocal && p.Report != null).ToList();
			GUILayout.Label("Opponent (players here with TRC Tune Loader)", Theme.SectionLabel);
			if (rivals.Count == 0) {
				GUILayout.Label(string.IsNullOrEmpty(playersError_) ? "Nobody else in this room runs TRC Tune Loader yet." : playersError_, Theme.Small);
			}
			foreach (PlayerRow rival in rivals) {
				bool canBattle = System.Version.TryParse(rival.Report.LoaderVersion, out System.Version theirs) && theirs >= BattleVersion;
				string text = canBattle ? $"<b>{rival.Player.Name}</b>  <color={Theme.MutedHex}>{rival.Player.CarName} · {rival.SetupText}</color>"
					: $"<b>{rival.Player.Name}</b>  <color={Theme.MutedHex}>needs TRC Tune Loader {BattleVersion} or newer (has {rival.Report.LoaderVersion})</color>";
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && canBattle;
				if (GUILayout.Button(text, pickedOpponent_ == rival.Player.SteamId ? Theme.RowSelected : Theme.Row)) {
					pickedOpponent_ = rival.Player.SteamId;
				}
				GUI.enabled = enabled;
			}

			List<Course> courses = courses_.ForMap(map_.Key).Where(c => c.Usable).ToList();
			GUILayout.Label("Course", Theme.SectionLabel);
			if (courses.Count == 0) {
				GUILayout.Label("No courses on this map yet: make one with the Course builder.", Theme.Small);
			}
			foreach (Course course in courses) {
				string text = $"<b>{course.Name}</b>  <color={Theme.MutedHex}>{course.KindName} · {TimeAttackRun.Distance(course.Length)} · {course.SourceName}</color>";
				if (GUILayout.Button(text, pickedCourse_ == course.Code ? Theme.RowSelected : Theme.Row)) {
					pickedCourse_ = course.Code;
				}
			}

			GUILayout.Label("Format", Theme.SectionLabel);
			pickedFormat_ = Choice(BattleFormats, pickedFormat_);
			GUILayout.Label(pickedFormat_ == 1
				? $"Runs swap who leads. The chaser wins a run by passing and staying ahead to the finish; the leader wins by finishing {BattleRules.LeaderGapSeconds:0.0} s or more ahead (1.0 s after both have led once, then 0.5 s). Otherwise swap and go again."
				: "Side by side from a standing start. The faster time to the finish wins.", Theme.Small);
			if (courses.FirstOrDefault(c => c.Code == pickedCourse_)?.IsCircuit == true) {
				GUILayout.Label("Laps", Theme.SectionLabel);
				pickedBattleLaps_ = Choice(BattleLapChoices.Select(l => l.ToString(CultureInfo.InvariantCulture)).ToArray(), pickedBattleLaps_);
			}
			GUILayout.Label("Mode", Theme.SectionLabel);
			pickedMode_ = Choice(BattleModes, pickedMode_);
			GUILayout.Label(pickedMode_ == 0 ? "Both cars TRC spec, on the same tune set, with no flagged mods." : "Any setup. It doesn't count.", Theme.Small);

			PlayerRow opponent = rivals.FirstOrDefault(r => r.Player.SteamId == pickedOpponent_);
			Course picked = courses.FirstOrDefault(c => c.Code == pickedCourse_);
			GUILayout.BeginHorizontal();
			bool ready = GUI.enabled;
			GUI.enabled = ready && opponent != null && picked != null;
			if (GUILayout.Button(opponent == null ? "Challenge" : $"Challenge {opponent.Player.Name}", Theme.ButtonPrimary)) {
				ChallengePlayer(opponent, picked);
			}
			GUI.enabled = ready;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		// ---- Over the track ----

		/// <summary>The battle over the track: the countdown, the timer with the gap, and messages.</summary>
		private void DrawBattleHud(float screenWidth, float screenHeight) {
			BattleSession b = battle_;
			TimeAttackRun run = b.Run;
			if (b.State == BattleSession.Phase.Countdown && run.CountdownNumber > 0) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), run.CountdownNumber.ToString(CultureInfo.InvariantCulture), Theme.HudBig);
			} else if (b.State == BattleSession.Phase.Running && run.ShowGo(BattleNow)) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), "GO!", Theme.HudBig);
			}

			string title = $"<b>vs {b.PeerName}</b> · {b.Course.Name}";
			if (b.Format == BattleFormat.CatAndMouse && b.Active) {
				title += $" · {(b.IAmLeader ? "you lead" : "you chase")}";
			}
			if (b.State == BattleSession.Phase.Running && run.Active) {
				float gap = shownGap_;
				string place = Mathf.Abs(gap) < SideBySideMetres ? "Side by side" : gap > 0 ? $"Ahead by {gap:0} m" : $"Behind by {-gap:0} m";
				if (b.Course.IsCircuit) {
					place = $"Lap {run.Lap}/{b.Laps} · {place}";
				}
				if (b.PeerOut.Length > 0) {
					place += $"\n<color={Theme.GoodHex}>{b.PeerOut}</color>";
				}
				DrawTimer(screenWidth, screenHeight, run, place);
			} else {
				DrawHudPill(screenWidth, screenHeight, $"{title}\n<color={Theme.MutedHex}>{b.Message}</color>");
			}
		}

		/// <summary>
		/// A challenge waiting for an answer, while driving. When a time attack or a recording already has the
		/// timer's spot, the challenge shows just under it (or over it, near the bottom of the screen).
		/// </summary>
		private void DrawInviteHud(float screenWidth, float screenHeight) {
			BattleMessage challenge = invites_[invites_.Count - 1].Challenge;
			string text = $"<b>{BattleMessage.PlainName(challenge.FromName)} challenges you</b>\n<color={Theme.MutedHex}>{BattleFormats[(int)challenge.Format]} · press {shortcut_} to answer</color>";
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
